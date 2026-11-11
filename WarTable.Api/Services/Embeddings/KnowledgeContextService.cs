using System.Text.RegularExpressions;
using WarTable.Api.Models;

namespace WarTable.Api.Services.Embeddings;

/// <summary>Bounded, relevant campaign knowledge attached to a work surface.</summary>
public sealed record KnowledgeContext(
	Guid CampaignId,
	Guid? ObjectiveId,
	Guid? StepId,
	string Query,
	bool SemanticUsed,
	bool Degraded,
	string? DegradationReason,
	IReadOnlyList<KnowledgeContextGroup> Groups);

/// <summary>One kind bucket within a <see cref="KnowledgeContext"/>.</summary>
public sealed record KnowledgeContextGroup(
	BunkerNoteKind Kind,
	int Total,
	int Omitted,
	IReadOnlyList<KnowledgeContextNote> Notes);

/// <summary>A note excerpt; full bodies remain available only through <c>get_note</c>.</summary>
public sealed record KnowledgeContextNote(
	Guid NoteId,
	int Alias,
	BunkerNoteKind Kind,
	string Title,
	string Excerpt,
	string? HeadingPath,
	IReadOnlyList<string> MatchedBy,
	double Score,
	bool Pinned,
	DateTime UpdatedAt);

/// <summary>
/// Builds bounded knowledge context for a step, objective, or ad-hoc query.
/// Explicit links, requested tags and campaign directives are always considered;
/// semantic recall is layered on top when embeddings are available. Every failure
/// path degrades to the deterministic sources instead of failing the call.
/// </summary>
public sealed class KnowledgeContextService
{
	private const int QueryLimit = 1200;
	private const int KeywordQueryLimit = 200;
	private const int ExcerptLimit = 240;

	private static readonly BunkerNoteKind[] GroupOrder =
	[
		BunkerNoteKind.Directive,
		BunkerNoteKind.Charter,
		BunkerNoteKind.Decision,
		BunkerNoteKind.Gotcha,
		BunkerNoteKind.Reference,
		BunkerNoteKind.Context,
		BunkerNoteKind.Question,
		BunkerNoteKind.DesignDoc,
	];

	private static readonly IReadOnlyDictionary<BunkerNoteKind, int> Quotas =
		new Dictionary<BunkerNoteKind, int>
		{
			[BunkerNoteKind.Directive] = 10,
			[BunkerNoteKind.Decision] = 5,
			[BunkerNoteKind.Gotcha] = 5,
			[BunkerNoteKind.Reference] = 3,
			[BunkerNoteKind.Context] = 3,
			[BunkerNoteKind.Question] = 3,
			[BunkerNoteKind.Charter] = 1,
			[BunkerNoteKind.DesignDoc] = 3,
		};

	private static readonly string[] MatchedByOrder =
	[
		"linked-step",
		"linked-objective",
		"tag",
		"directive",
		"charter",
		"semantic",
		"keyword",
	];

	private readonly WarTableService _service;
	private readonly KnowledgeSearchService _search;
	private readonly ILogger<KnowledgeContextService> _logger;

	public KnowledgeContextService(
		WarTableService service,
		KnowledgeSearchService search,
		ILogger<KnowledgeContextService> logger)
	{
		_service = service;
		_search = search;
		_logger = logger;
	}

	public async Task<KnowledgeContext> GetForStepAsync(
		Guid campaignId,
		Step step,
		CancellationToken cancellationToken = default)
	{
		Objective? objective = null;
		if (step.ObjectiveId != Guid.Empty)
		{
			objective = await _service.GetObjectiveAsync(step.ObjectiveId);
			if (objective is not null && objective.CampaignId != campaignId)
			{
				throw new InvalidOperationException(
					$"Step {step.Id} does not belong to campaign {campaignId}.");
			}
		}

		return await BuildAsync(
			campaignId,
			objective,
			step,
			objective?.Id ?? (step.ObjectiveId == Guid.Empty ? null : step.ObjectiveId),
			step.Id,
			null,
			null,
			cancellationToken);
	}

	public async Task<KnowledgeContext> GetForObjectiveAsync(
		Guid campaignId,
		Objective objective,
		CancellationToken cancellationToken = default)
		=> await BuildAsync(
			campaignId,
			objective,
			null,
			objective.Id,
			null,
			null,
			null,
			cancellationToken);

	public async Task<KnowledgeContext> GetAsync(
		Guid campaignId,
		Guid? objectiveId,
		Guid? stepId,
		string? query,
		string? tags,
		CancellationToken cancellationToken = default)
	{
		if (objectiveId is null &&
			stepId is null &&
			string.IsNullOrWhiteSpace(query) &&
			string.IsNullOrWhiteSpace(tags))
		{
			throw new ArgumentException(
				"Provide at least one of objective_id, step_id, query, or tags.",
				nameof(objectiveId));
		}

		Objective? objective = null;
		Step? step = null;

		if (objectiveId.HasValue)
		{
			objective = await _service.GetObjectiveAsync(objectiveId.Value)
				?? throw new InvalidOperationException($"Objective not found: {objectiveId}");
			if (objective.CampaignId != campaignId)
				throw new InvalidOperationException(
					$"Objective {objectiveId} does not belong to campaign {campaignId}.");
		}

		if (stepId.HasValue)
		{
			step = await _service.GetStepAsync(stepId.Value)
				?? throw new InvalidOperationException($"Step not found: {stepId}");

			var parent = objective ?? await _service.GetObjectiveAsync(step.ObjectiveId);
			if (parent is null || parent.CampaignId != campaignId)
				throw new InvalidOperationException(
					$"Step {stepId} does not belong to campaign {campaignId}.");
			if (parent.Id != step.ObjectiveId)
				throw new InvalidOperationException(
					$"Step {stepId} does not belong to objective {parent.Id}.");
			objective = parent;
		}

		return await BuildAsync(
			campaignId,
			objective,
			step,
			objective?.Id,
			stepId,
			query,
			tags,
			cancellationToken);
	}

	/// <summary>
	/// Counts explicit note-to-objective links by kind. No semantic search and no
	/// sidecar access: this is safe to call for every objective in a plan.
	/// </summary>
	public async Task<IReadOnlyDictionary<Guid, KnowledgeCounts>> GetCountsForObjectivesAsync(
		Guid campaignId,
		IReadOnlyList<Objective> objectives,
		CancellationToken cancellationToken = default)
	{
		var counts = objectives.ToDictionary(
			objective => objective.Id,
			_ => new KnowledgeCounts(0, 0, 0, 0, 0));

		if (objectives.Count == 0)
			return counts;

		var notes = await _service.GetNotesAsync(
			campaignId, null, null, BunkerNoteStatus.Active, includeDeleted: false);

		var stepToObjective = objectives
			.SelectMany(objective => objective.Steps.Select(step => (StepId: step.Id, ObjectiveId: objective.Id)))
			.GroupBy(item => item.StepId)
			.ToDictionary(group => group.Key, group => group.First().ObjectiveId);

		foreach (var objective in objectives)
		{
			var decisions = 0;
			var gotchas = 0;
			var references = 0;
			var context = 0;
			var questions = 0;

			foreach (var note in notes)
			{
				var linked = note.LinkedObjectiveIds.Contains(objective.Id) ||
					note.LinkedStepIds.Any(stepId =>
						stepToObjective.TryGetValue(stepId, out var ownerId) &&
						ownerId == objective.Id);
				if (!linked)
					continue;

				switch (note.Kind)
				{
					case BunkerNoteKind.Decision:
						decisions++;
						break;
					case BunkerNoteKind.Gotcha:
						gotchas++;
						break;
					case BunkerNoteKind.Reference:
						references++;
						break;
					case BunkerNoteKind.Context:
						context++;
						break;
					case BunkerNoteKind.Question:
						questions++;
						break;
				}
			}

			counts[objective.Id] = new KnowledgeCounts(
				decisions, gotchas, references, context, questions);
		}

		return counts;
	}

	private async Task<KnowledgeContext> BuildAsync(
		Guid campaignId,
		Objective? objective,
		Step? step,
		Guid? objectiveId,
		Guid? stepId,
		string? extraQuery,
		string? tags,
		CancellationToken cancellationToken)
	{
		var requestedTags = ParseTags(tags);
		var query = BuildQuery(objective, step, requestedTags, extraQuery);
		var keywordQuery = BuildKeywordQuery(objective, step, requestedTags, extraQuery);

		var notes = await _service.GetNotesAsync(
			campaignId, null, null, BunkerNoteStatus.Active, includeDeleted: false);
		var byId = notes.ToDictionary(note => note.Id);

		var candidates = new Dictionary<Guid, Candidate>();

		foreach (var note in notes)
		{
			if (stepId.HasValue && note.LinkedStepIds.Contains(stepId.Value))
				Add(candidates, note, "linked-step");

			if (objectiveId.HasValue && note.LinkedObjectiveIds.Contains(objectiveId.Value))
				Add(candidates, note, "linked-objective");

			if (requestedTags.Length > 0 && NoteMatchesTag(note, requestedTags))
				Add(candidates, note, "tag");

			if (note.Kind == BunkerNoteKind.Directive)
				Add(candidates, note, "directive");

			if (note.Kind == BunkerNoteKind.Charter)
				Add(candidates, note, "charter");
		}

		var semanticUsed = false;
		var degraded = false;
		string? degradationReason = null;

		if (query.Length > 0)
		{
			try
			{
				var response = await _search.SearchAsync(
					campaignId,
					query,
					KnowledgeSearchMode.Hybrid,
					50,
					keywordQuery,
					cancellationToken);

				degraded = response.Degraded;
				degradationReason = response.DegradationReason;
				semanticUsed = !response.Degraded;

				foreach (var result in response.Results)
				{
					if (!byId.TryGetValue(result.NoteId, out var note))
						continue;
					if (note.Status != BunkerNoteStatus.Active)
						continue;

					var candidate = Add(candidates, note, null);

					var both = string.Equals(
						result.MatchedBy, "both", StringComparison.OrdinalIgnoreCase);
					if (both ||
						result.MatchedBy.Contains("semantic", StringComparison.OrdinalIgnoreCase))
					{
						candidate.MatchedBy.Add("semantic");
					}
					if (both ||
						result.MatchedBy.Contains("keyword", StringComparison.OrdinalIgnoreCase))
					{
						candidate.MatchedBy.Add("keyword");
					}

					candidate.Score = Math.Max(candidate.Score, result.Score);
					candidate.Snippet ??= result.Snippet;
					candidate.HeadingPath ??= result.HeadingPath;
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				_logger.LogWarning(
					ex,
					"Knowledge context semantic search failed for campaign {CampaignId}; continuing without it.",
					campaignId);
				degraded = true;
				degradationReason = ex.Message;
				semanticUsed = false;
			}
		}

		var groups = candidates.Values
			.GroupBy(candidate => candidate.Note.Kind)
			.Select(group =>
			{
				var ordered = Rank(group).ToList();
				var quota = Quotas.TryGetValue(group.Key, out var value) ? value : 3;
				var selected = ordered
					.Take(quota)
					.Select(candidate => ToContextNote(candidate, query))
					.ToList();

				return new KnowledgeContextGroup(
					group.Key,
					ordered.Count,
					ordered.Count - selected.Count,
					selected);
			})
			.OrderBy(group => Array.IndexOf(GroupOrder, group.Kind))
			.ToList();

		return new KnowledgeContext(
			campaignId,
			objectiveId,
			stepId,
			query,
			semanticUsed,
			degraded,
			degradationReason,
			groups);
	}

	private static IEnumerable<Candidate> Rank(IEnumerable<Candidate> candidates)
		=> candidates
			.OrderByDescending(candidate => candidate.Note.Pinned)
			.ThenByDescending(candidate => candidate.Note.Kind == BunkerNoteKind.Directive)
			.ThenByDescending(candidate => candidate.Note.Kind == BunkerNoteKind.Charter)
			.ThenByDescending(candidate => candidate.MatchedBy.Contains("linked-step"))
			.ThenByDescending(candidate => candidate.MatchedBy.Contains("linked-objective"))
			.ThenByDescending(candidate => candidate.MatchedBy.Contains("tag"))
			.ThenByDescending(candidate => candidate.Score)
			.ThenByDescending(candidate => candidate.Note.UpdatedAt);

	private static Candidate Add(
		Dictionary<Guid, Candidate> candidates,
		BunkerNote note,
		string? matchedBy)
	{
		if (!candidates.TryGetValue(note.Id, out var candidate))
		{
			candidate = new Candidate(note);
			candidates[note.Id] = candidate;
		}

		if (!string.IsNullOrWhiteSpace(matchedBy))
			candidate.MatchedBy.Add(matchedBy);

		return candidate;
	}

	private static KnowledgeContextNote ToContextNote(Candidate candidate, string query)
	{
		var note = candidate.Note;
		var matchedBy = MatchedByOrder
			.Where(candidate.MatchedBy.Contains)
			.ToList();

		return new KnowledgeContextNote(
			note.Id,
			note.Alias,
			note.Kind,
			note.Title,
			Excerpt(note, candidate.Snippet, query),
			candidate.HeadingPath,
			matchedBy,
			candidate.Score,
			note.Pinned,
			note.UpdatedAt);
	}

	private static string Excerpt(BunkerNote note, string? snippet, string query)
	{
		var text = string.IsNullOrWhiteSpace(snippet) ? note.Body : snippet;
		var value = Regex.Replace(text ?? "", @"\s+", " ").Trim();
		if (value.Length == 0)
			value = Regex.Replace(note.Title ?? "", @"\s+", " ").Trim();
		if (value.Length <= ExcerptLimit)
			return value;

		var index = FindAnchor(value, query);
		if (index < 0)
			return Bound(value[..ExcerptLimit].TrimEnd());

		var start = Math.Max(0, index - ExcerptLimit / 3);
		start = Math.Min(start, Math.Max(0, value.Length - ExcerptLimit));

		var length = Math.Min(ExcerptLimit, value.Length - start);
		var excerpt = value.Substring(start, length).Trim();

		if (start > 0)
			excerpt = "…" + excerpt;
		if (start + length < value.Length)
			excerpt += "…";

		return Bound(excerpt);
	}

	private static string Bound(string value)
		=> value.Length <= ExcerptLimit
			? value
			: value[..(ExcerptLimit - 1)].TrimEnd() + "…";

	/// <summary>Finds the first meaningful query token so excerpts stay query-centered.</summary>
	private static int FindAnchor(string value, string query)
	{
		foreach (var token in query
			.Split([' ', ',', ';', '\n', '\r', '\t', '.', ':', '/'], StringSplitOptions.RemoveEmptyEntries)
			.Select(token => token.Trim().TrimStart('#'))
			.Where(token => token.Length >= 4)
			.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			var index = value.IndexOf(token, StringComparison.OrdinalIgnoreCase);
			if (index >= 0)
				return index;
		}

		return -1;
	}

	private static string BuildQuery(
		Objective? objective,
		Step? step,
		string[] requestedTags,
		string? extraQuery)
	{
		var parts = new List<string>();
		if (objective is not null)
		{
			if (!string.IsNullOrWhiteSpace(objective.Title))
				parts.Add(objective.Title);
			if (!string.IsNullOrWhiteSpace(objective.Description))
				parts.Add(objective.Description);
		}

		if (step is not null)
		{
			if (!string.IsNullOrWhiteSpace(step.Title))
				parts.Add(step.Title);
			if (!string.IsNullOrWhiteSpace(step.Context))
				parts.Add(step.Context);
		}

		if (requestedTags.Length > 0)
			parts.Add(string.Join(' ', requestedTags));

		if (!string.IsNullOrWhiteSpace(extraQuery))
			parts.Add(extraQuery.Trim());

		var query = string.Join('\n', parts).Trim();
		return query.Length <= QueryLimit ? query : query[..QueryLimit];
	}

	/// <summary>
	/// Keyword retrieval is a single case-insensitive substring match, so the
	/// 1200-character retrieval query would never hit. Prefer the most specific
	/// short needle: an explicit query, then the step title, objective title, or
	/// requested tags. The richer query is still used for embeddings.
	/// </summary>
	private static string BuildKeywordQuery(
		Objective? objective,
		Step? step,
		string[] requestedTags,
		string? extraQuery)
	{
		string value;
		if (!string.IsNullOrWhiteSpace(extraQuery))
			value = extraQuery.Trim();
		else if (step is not null && !string.IsNullOrWhiteSpace(step.Title))
			value = step.Title.Trim();
		else if (objective is not null && !string.IsNullOrWhiteSpace(objective.Title))
			value = objective.Title.Trim();
		else if (requestedTags.Length > 0)
			value = string.Join(' ', requestedTags);
		else
			value = "";

		return value.Length <= KeywordQueryLimit ? value : value[..KeywordQueryLimit];
	}

	private static string[] ParseTags(string? tags)
	{
		if (string.IsNullOrWhiteSpace(tags))
			return [];

		return tags
			.Split([',', ';', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(tag => tag.TrimStart('#').ToLowerInvariant())
			.Where(tag => tag.Length > 0)
			.Distinct()
			.ToArray();
	}

	private static bool NoteMatchesTag(BunkerNote note, string[] requestedTags)
	{
		var noteTags = ParseTags(note.Tags);
		if (noteTags.Length == 0)
			return false;

		return requestedTags.Any(requested =>
			noteTags.Any(noteTag =>
				string.Equals(noteTag, requested, StringComparison.OrdinalIgnoreCase) ||
				noteTag.Contains(requested, StringComparison.OrdinalIgnoreCase) ||
				requested.Contains(noteTag, StringComparison.OrdinalIgnoreCase)));
	}

	private sealed class Candidate
	{
		public Candidate(BunkerNote note) => Note = note;

		public BunkerNote Note { get; }
		public HashSet<string> MatchedBy { get; } = new(StringComparer.OrdinalIgnoreCase);
		public double Score { get; set; }
		public string? Snippet { get; set; }
		public string? HeadingPath { get; set; }
	}
}
