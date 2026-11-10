using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WarTable.Api.Models;
using WarTable.Api.Options;

namespace WarTable.Api.Services.Embeddings;

public enum KnowledgeSearchMode
{
	Keyword,
	Semantic,
	Hybrid,
}

public sealed record KnowledgeSearchResult(
	Guid NoteId,
	int Alias,
	string Title,
	BunkerNoteKind Kind,
	BunkerNoteStatus Status,
	Guid CampaignId,
	string Snippet,
	string? HeadingPath,
	double Score,
	string MatchedBy,
	DateTime UpdatedAt);

public sealed record KnowledgeSearchResponse(
	string Query,
	KnowledgeSearchMode Mode,
	bool Degraded,
	string? DegradationReason,
	IReadOnlyList<KnowledgeSearchResult> Results);

/// <summary>
/// Hybrid retrieval: keep the existing exact keyword search as the baseline and
/// merge semantic hits with reciprocal rank fusion. If embeddings are disabled
/// or unavailable, the same call still returns keyword results with Degraded=true.
/// </summary>
public sealed class KnowledgeSearchService
{
	private const int RrfK = 60;
	private readonly WarTableService _service;
	private readonly IKnowledgeIndex _index;
	private readonly IEmbeddingClient _client;
	private readonly EmbeddingProfileProvider _profiles;
	private readonly EmbeddingsOptions _options;
	private readonly ILogger<KnowledgeSearchService> _logger;

	public KnowledgeSearchService(
		WarTableService service,
		IKnowledgeIndex index,
		IEmbeddingClient client,
		EmbeddingProfileProvider profiles,
		IOptions<EmbeddingsOptions> options,
		ILogger<KnowledgeSearchService> logger)
	{
		_service = service;
		_index = index;
		_client = client;
		_profiles = profiles;
		_options = options.Value;
		_logger = logger;
	}

	public async Task<KnowledgeSearchResponse> SearchAsync(
		Guid? campaignId,
		string query,
		KnowledgeSearchMode mode = KnowledgeSearchMode.Hybrid,
		int? limit = null,
		CancellationToken cancellationToken = default)
	{
		query = (query ?? "").Trim();
		var take = Math.Clamp(limit ?? _options.TopK, 1, 50);
		if (query.Length == 0)
		{
			return new KnowledgeSearchResponse(
				query, mode, false, null, Array.Empty<KnowledgeSearchResult>());
		}

		var keywordNotes = await _service.GetNotesAsync(campaignId, query, null, null);
		var semanticHits = new List<SemanticHit>();
		string? degraded = null;

		if (mode != KnowledgeSearchMode.Keyword)
		{
			if (!_options.Enabled)
			{
				degraded = "Semantic search is disabled.";
			}
			else if (!_profiles.IsConfigured)
			{
				degraded = "Embedding model or endpoint is not configured.";
			}
			else
			{
				try
				{
					var hits = await SearchSemanticAsync(campaignId, query, cancellationToken);
					if (hits.Error is not null)
						degraded = hits.Error;
					else
						semanticHits = hits.Hits;
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "Semantic search failed; returning keyword results.");
					degraded = ex.Message;
				}
			}
		}

		if (mode == KnowledgeSearchMode.Semantic && degraded is null)
		{
			var results = semanticHits
				.Select(hit => ToResult(
					hit.Note, hit.Chunk.Text, hit.Chunk.HeadingPath, hit.Score, "semantic", query))
				.Take(take)
				.ToList();
			return new KnowledgeSearchResponse(query, mode, false, null, results);
		}

		if (mode == KnowledgeSearchMode.Keyword || degraded is not null)
		{
			var results = keywordNotes
				.Select((note, index) => ToResult(
					note, note.Body, null, 1.0 / (index + 1), "keyword", query))
				.Take(take)
				.ToList();
			return new KnowledgeSearchResponse(query, mode, degraded is not null, degraded, results);
		}

		// Hybrid: merge both rankings with reciprocal rank fusion.
		var candidates = new Dictionary<Guid, Candidate>();
		for (var i = 0; i < keywordNotes.Count; i++)
		{
			var candidate = GetCandidate(candidates, keywordNotes[i]);
			candidate.KeywordRank ??= i + 1;
		}

		for (var i = 0; i < semanticHits.Count; i++)
		{
			var hit = semanticHits[i];
			var candidate = GetCandidate(candidates, hit.Note);
			candidate.SemanticRank ??= i + 1;
			candidate.SemanticScore = Math.Max(candidate.SemanticScore, hit.Score);
			if (string.IsNullOrWhiteSpace(candidate.SemanticSnippet))
				candidate.SemanticSnippet = hit.Chunk.Text;
			if (string.IsNullOrWhiteSpace(candidate.SemanticHeading))
				candidate.SemanticHeading = hit.Chunk.HeadingPath;
		}

		var merged = candidates.Values
			.Select(candidate =>
			{
				var score = (candidate.KeywordRank.HasValue ? 1.0 / (RrfK + candidate.KeywordRank.Value) : 0)
					+ (candidate.SemanticRank.HasValue ? 1.0 / (RrfK + candidate.SemanticRank.Value) : 0);
				var matchedBy = candidate.KeywordRank.HasValue && candidate.SemanticRank.HasValue
					? "both"
					: candidate.SemanticRank.HasValue ? "semantic" : "keyword";
				var snippet = candidate.SemanticSnippet ?? candidate.Note.Body;
				return ToResult(candidate.Note, snippet, candidate.SemanticHeading, score, matchedBy, query);
			})
			.OrderByDescending(result => result.Score)
			.Take(take)
			.ToList();

		return new KnowledgeSearchResponse(query, mode, false, null, merged);
	}

	private async Task<(List<SemanticHit> Hits, string? Error)> SearchSemanticAsync(
		Guid? campaignId,
		string query,
		CancellationToken cancellationToken)
	{
		await _index.InitializeAsync(cancellationToken);
		var chunks = await _index.GetChunksAsync(campaignId, cancellationToken);
		if (chunks.Count == 0)
			return (new List<SemanticHit>(), "Knowledge index is empty.");

		var queryVector = (await _client.EmbedAsync(
			new[] { _profiles.RenderQuery(query) },
			cancellationToken)).FirstOrDefault();
		if (queryVector is null || queryVector.Length == 0)
			return (new List<SemanticHit>(), "Embedding service returned an empty query vector.");

		var profile = _profiles.Profile;
		var valid = chunks
			.Where(chunk =>
				string.Equals(chunk.Model, _options.Model, StringComparison.Ordinal) &&
				string.Equals(chunk.Profile, profile.Name, StringComparison.Ordinal) &&
				chunk.Vector.Length == queryVector.Length)
			.ToList();
		if (valid.Count == 0)
		{
			return (new List<SemanticHit>(),
				"The knowledge index was built with a different model, profile, or dimensions. Reindex the notes.");
		}

		var bestByNote = valid
			.GroupBy(chunk => chunk.SourceId)
			.Select(group =>
			{
				var best = group
					.Select(chunk => new { Chunk = chunk, Score = VectorMath.Cosine(queryVector, chunk.Vector) })
					.OrderByDescending(item => item.Score)
					.First();
				return new { SourceId = group.Key, best.Chunk, best.Score };
			})
			.Where(item => item.Score >= _options.MinScore)
			.OrderByDescending(item => item.Score)
			.Take(50)
			.ToList();

		var notes = await _service.GetNotesAsync(campaignId, null, null, null);
		var byId = notes.ToDictionary(note => note.Id);
		var hits = new List<SemanticHit>();
		foreach (var item in bestByNote)
		{
			if (byId.TryGetValue(item.SourceId, out var note))
				hits.Add(new SemanticHit(note, item.Chunk, item.Score));
		}

		return (hits, null);
	}

	private static Candidate GetCandidate(Dictionary<Guid, Candidate> candidates, BunkerNote note)
	{
		if (!candidates.TryGetValue(note.Id, out var candidate))
		{
			candidate = new Candidate(note);
			candidates[note.Id] = candidate;
		}

		return candidate;
	}

	private static KnowledgeSearchResult ToResult(
		BunkerNote note,
		string? text,
		string? headingPath,
		double score,
		string matchedBy,
		string query)
		=> new(
			note.Id,
			note.Alias,
			note.Title,
			note.Kind,
			note.Status,
			note.CampaignId,
			Snippet(text, note.Title, query),
			headingPath,
			score,
			matchedBy,
			note.UpdatedAt);

	private static string Snippet(string? text, string? title, string query)
	{
		var value = string.IsNullOrWhiteSpace(text) ? title ?? "" : text;
		value = Regex.Replace(value, @"\s+", " ").Trim();
		if (value.Length <= 240)
			return value;

		var index = value.IndexOf(query, StringComparison.OrdinalIgnoreCase);
		if (index < 0)
			return value[..240].TrimEnd() + "…";

		var start = Math.Max(0, index - 80);
		var length = Math.Min(240, value.Length - start);
		var snippet = value.Substring(start, length).Trim();
		return (start > 0 ? "…" : "") + snippet + (start + length < value.Length ? "…" : "");
	}

	private sealed class Candidate
	{
		public Candidate(BunkerNote note) => Note = note;

		public BunkerNote Note { get; }
		public int? KeywordRank { get; set; }
		public int? SemanticRank { get; set; }
		public double SemanticScore { get; set; }
		public string? SemanticSnippet { get; set; }
		public string? SemanticHeading { get; set; }
	}

	private sealed record SemanticHit(BunkerNote Note, StoredKnowledgeChunk Chunk, double Score);
}
