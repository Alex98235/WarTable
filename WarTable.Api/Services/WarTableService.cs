using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarTable.Api.Data;
using WarTable.Api.Models;

namespace WarTable.Api.Services;

/// <summary>
/// Partial update payload for an objective. A null member means "leave unchanged";
/// an empty string means "clear".
/// </summary>
public record ObjectivePatch(
	string? Title = null,
	string? Description = null,
	PlanState? PlanState = null,
	int? SortOrder = null);

/// <summary>Partial update payload for a campaign. Same semantics as <see cref="ObjectivePatch"/>.</summary>
public record CampaignPatch(
	string? Title = null,
	string? Tags = null,
	DateTime? ArchivedAt = null,
	string? Charter = null);

/// <summary>Partial update payload for a step. A null member means "leave unchanged".</summary>
public record StepPatch(
	string? Title = null,
	string? Context = null,
	string? Assignee = null,
	string? Assigner = null,
	string? Status = null);

/// <summary>Partial update payload for a Bunker note. Same semantics as <see cref="ObjectivePatch"/>.</summary>
public record BunkerNotePatch(
	string? Title = null,
	string? Body = null,
	bool? Pinned = null,
	string? Tags = null,
	Guid? SupersedesId = null,
	string? Basis = null,
	DateTime? ApprovedAt = null);

/// <summary>Partial update payload for an operator preference. Same semantics as <see cref="ObjectivePatch"/>.</summary>
public record PreferencePatch(
	string? Text = null,
	string? Rationale = null,
	bool? Active = null,
	string? Tags = null);

/// <summary>Result of resolving a campaign-scoped <c>#N</c> alias to a concrete object.</summary>
public record AliasResolution(
	string Kind,
	Guid Id,
	string Title,
	bool Deleted,
	Guid CampaignId,
	int Alias);

/// <summary>Valid step statuses.</summary>
public static class StepStatuses
{
	public const string Open = "open";
	public const string InProgress = "in-progress";
	public const string Done = "done";
	public const string Flagged = "flagged";
	public const string Blocked = "blocked";

	public static readonly string[] All = [Open, InProgress, Done, Flagged, Blocked];

	/// <summary>Returns true when the status is one of the known values.</summary>
	public static bool IsValid(string status)
	{
		return All.Contains(status);
	}
}

/// <summary>Derived objective statuses (computed from child steps, stored as cache).</summary>
public static class ObjectiveStatuses
{
	public const string Planned = "planned";
	public const string InProgress = "in-progress";
	public const string NeedsReview = "needs-review";
	public const string Blocked = "blocked";
	public const string Completed = "completed";
}

/// <summary>
/// Business logic layer over the WarTable EF Core context.
/// </summary>
public partial class WarTableService
{
	private readonly WarTableDbContext _db;

	private static readonly JsonSerializerOptions MetadataJsonOptions = new(JsonSerializerDefaults.Web);

	public WarTableService(WarTableDbContext db)
	{
		_db = db;
	}

	private const int BriefingStepLimit = 20;
	private const int BriefingNoteLimit = 10;
	private const int BriefingSeedLimit = 10;
	private const int BriefingDocumentLimit = 10;
	private const int BriefingDocumentExcerptLength = 800;
	private const int CampaignCharterExcerptLength = 200;
	private const int BriefingPreferenceLimit = 20;
	private const int BriefingActivityLimit = 20;
	private const int AttentionLimit = 50;

	// ── Actors and events ──

	/// <summary>Blank actors normalize to <c>human</c>.</summary>
	private static string NormalizeActor(string? actor)
		=> string.IsNullOrWhiteSpace(actor) ? "human" : actor.Trim();

	/// <summary>
	/// Appends an activity event and persists it together with any pending changes
	/// on the context. One call per mutation keeps provenance atomic with the write.
	/// </summary>
	public async Task RecordEventAsync(
		Guid? campaignId,
		string actor,
		ActivityEntityType entityType,
		Guid entityId,
		string action,
		string summary,
		object? metadata = null)
	{
		var nextSequence = (await _db.ActivityEvents.MaxAsync(e => (long?)e.Sequence) ?? 0) + 1;

		_db.ActivityEvents.Add(new ActivityEvent
		{
			CampaignId = campaignId,
			Actor = NormalizeActor(actor),
			EntityType = entityType,
			EntityId = entityId,
			Action = action,
			Summary = summary,
			Metadata = metadata is null ? "{}" : JsonSerializer.Serialize(metadata, MetadataJsonOptions),
			CreatedAt = DateTime.UtcNow,
			Sequence = nextSequence,
		});

		await _db.SaveChangesAsync();
	}

	// ── Derived status helpers ──

	/// <summary>
	/// Computes the derived objective status from its child steps.
	/// Priority: flagged > blocked > in-progress > completed > partial > planned.
	/// </summary>
	private static string DeriveObjectiveStatus(IEnumerable<Step> steps)
	{
		var list = steps?.ToList();
		if (list is null || list.Count == 0)
			return ObjectiveStatuses.Planned;

		var statuses = list.Select(s => s.Status).ToHashSet();

		if (statuses.Contains(StepStatuses.Flagged))
			return ObjectiveStatuses.NeedsReview;
		if (statuses.Contains(StepStatuses.Blocked))
			return ObjectiveStatuses.Blocked;
		if (statuses.Contains(StepStatuses.InProgress))
			return ObjectiveStatuses.InProgress;
		if (statuses.All(s => s == StepStatuses.Done))
			return ObjectiveStatuses.Completed;
		if (statuses.Any(s => s != StepStatuses.Open))
			return ObjectiveStatuses.InProgress; // partial

		return ObjectiveStatuses.Planned;
	}

	/// <summary>
	/// Computes an objective's status from the database, optionally replacing or
	/// excluding one step to account for pending in-memory changes.
	/// </summary>
	private async Task<string> ComputeObjectiveStatusAsync(
		Guid objectiveId,
		Step? overrideStep = null,
		Guid? excludeStepId = null)
	{
		var steps = await _db.Steps.AsNoTracking()
			.Where(s => s.ObjectiveId == objectiveId)
			.ToListAsync();

		if (overrideStep is not null)
		{
			steps.RemoveAll(s => s.Id == overrideStep.Id);
			if (overrideStep.DeletedAt is null)
				steps.Add(overrideStep);
		}

		if (excludeStepId.HasValue)
			steps.RemoveAll(s => s.Id == excludeStepId.Value);

		return DeriveObjectiveStatus(steps);
	}

	private async Task ApplyObjectiveStatusAsync(Guid objectiveId, string status, DateTime updatedAt)
	{
		var objective = await _db.Objectives.FirstOrDefaultAsync(o => o.Id == objectiveId);
		if (objective is not null)
		{
			objective.Status = status;
			objective.UpdatedAt = updatedAt;
		}
	}

	// ── Alias allocation ──

	/// <summary>
	/// Atomically claims the next campaign-scoped alias (shared by objectives and
	/// Bunker notes) and advances the counter in a single SQLite statement.
	/// <para>
	/// A read-modify-write on the tracked <see cref="Campaign"/> is not safe here:
	/// concurrent contexts - including the separate stdio MCP process - can read the
	/// same <c>NextAliasNumber</c> and then collide on the unique
	/// <c>(CampaignId, Alias)</c> index. <c>UPDATE ... RETURNING</c> runs under
	/// SQLite's write lock, so each caller gets a distinct value even across
	/// processes. The <c>MAX</c> guard also self-heals a counter that is behind the
	/// highest persisted alias. Gaps are acceptable; aliases are never reused.
	/// </para>
	/// </summary>
	private async Task<int> AllocateAliasAsync(Guid campaignId)
	{
		var connection = _db.Database.GetDbConnection();
		var openedHere = connection.State != ConnectionState.Open;
		if (openedHere)
			await connection.OpenAsync();

		try
		{
			await using var command = connection.CreateCommand();
			command.CommandText = """
                UPDATE "Campaigns"
                SET "NextAliasNumber" = MAX(
                        "NextAliasNumber",
                        (SELECT COALESCE(MAX("Alias"), 0) + 1
                         FROM (SELECT "Alias" FROM "Objectives" WHERE "CampaignId" = $campaignId
                               UNION ALL
                               SELECT "Alias" FROM "BunkerNotes" WHERE "CampaignId" = $campaignId)))
                    + 1
                WHERE "Id" = $campaignId
                RETURNING "NextAliasNumber" - 1;
                """;

			var parameter = command.CreateParameter();
			parameter.ParameterName = "$campaignId";
			parameter.Value = campaignId;
			command.Parameters.Add(parameter);

			var result = await command.ExecuteScalarAsync();
			if (result is null || result is DBNull)
				throw new InvalidOperationException($"Campaign not found: {campaignId}");

			return Convert.ToInt32(result);
		}
		finally
		{
			if (openedHere)
				await connection.CloseAsync();
		}
	}

	// ── Campaigns ──

	public async Task<List<Campaign>> GetCampaignsAsync(string? search = null, bool includeArchived = false)
	{
		var query = _db.Campaigns.AsNoTracking().AsQueryable();

		if (!includeArchived)
			query = query.Where(c => c.ArchivedAt == null);

		if (!string.IsNullOrWhiteSpace(search))
		{
			var lower = search.ToLowerInvariant();
			query = query.Where(c =>
				c.Title.ToLower().Contains(lower) ||
				c.Tags.ToLower().Contains(lower) ||
				(c.Charter != null && (c.Charter.ToLower().Contains(lower) || c.Title.ToLower().Contains(lower))));
		}

		var campaigns = await query
			.OrderByDescending(c => c.UpdatedAt)
			.ToListAsync();

		// List views need a short charter preview.
		foreach (var campaign in campaigns)
		{
			if (!string.IsNullOrWhiteSpace(campaign.Charter))
				campaign.CharterExcerpt = Excerpt(campaign.Charter, CampaignCharterExcerptLength);
		}

		return campaigns;
	}

	public async Task<Campaign?> GetCampaignAsync(Guid id)
	{
		var campaign = await _db.Campaigns
			.AsNoTracking()
			.Include(c => c.Objectives)
			.Include(c => c.BunkerNotes)
				.ThenInclude(n => n.Links)
			.AsSplitQuery()
			.FirstOrDefaultAsync(c => c.Id == id);

		if (campaign is null)
			return null;

		campaign.Objectives = campaign.Objectives
			.OrderBy(o => o.SortOrder)
			.ThenBy(o => o.CreatedAt)
			.ToList();

		campaign.BunkerNotes = campaign.BunkerNotes
			.OrderByDescending(n => n.Pinned)
			.ThenByDescending(n => n.CreatedAt)
			.ToList();

		foreach (var note in campaign.BunkerNotes)
			PopulateNoteLinks(note);

		if (!string.IsNullOrWhiteSpace(campaign.Charter))
			campaign.CharterExcerpt = Excerpt(campaign.Charter, CampaignCharterExcerptLength);

		return campaign;
	}

	public async Task<Campaign> CreateCampaignAsync(string title, string charter, string tags, string actor)
	{
		var now = DateTime.UtcNow;
		var campaign = new Campaign
		{
			Title = title,
			Intent = "",
			Charter = string.IsNullOrWhiteSpace(charter) ? null : charter,
			Tags = tags,
			CreatedAt = now,
			UpdatedAt = now,
			NextAliasNumber = 1,
		};

		_db.Campaigns.Add(campaign);

		if (!string.IsNullOrWhiteSpace(charter))
			campaign.CharterExcerpt = Excerpt(charter, CampaignCharterExcerptLength);

		await RecordEventAsync(
			campaign.Id, actor, ActivityEntityType.Campaign, campaign.Id,
			"Created", $"Created campaign \"{campaign.Title}\"",
			new { campaign_id = campaign.Id, title = campaign.Title });

		return campaign;
	}

	public async Task<Campaign?> UpdateCampaignAsync(Guid id, CampaignPatch patch, string actor)
	{
		var campaign = await _db.Campaigns.FirstOrDefaultAsync(c => c.Id == id);
		if (campaign is null)
			return null;

		if (patch.Title is not null) campaign.Title = patch.Title;
		if (patch.Tags is not null) campaign.Tags = patch.Tags;
		if (patch.ArchivedAt is not null) campaign.ArchivedAt = patch.ArchivedAt;
		if (patch.Charter is not null) { campaign.Charter = patch.Charter; }
		campaign.UpdatedAt = DateTime.UtcNow;

		if (!string.IsNullOrWhiteSpace(campaign.Charter))
			campaign.CharterExcerpt = Excerpt(campaign.Charter, CampaignCharterExcerptLength);

		await RecordEventAsync(
			campaign.Id, actor, ActivityEntityType.Campaign, campaign.Id,
			"Updated", $"Updated campaign \"{campaign.Title}\"",
			new { title = campaign.Title, archived_at = campaign.ArchivedAt });

		return campaign;
	}

	/// <summary>Hard delete for now. Dependencies are removed explicitly; the rest cascade.</summary>
	public async Task<bool> DeleteCampaignAsync(Guid id, string actor)
	{
		var campaign = await _db.Campaigns.FirstOrDefaultAsync(c => c.Id == id);
		if (campaign is null)
			return false;

		var title = campaign.Title;

		// StepDependency uses Restrict to avoid cascade paths, so clear the edges
		// that touch any step in this campaign before deleting.
		var stepIds = await _db.Steps.IgnoreQueryFilters()
			.Where(s => _db.Objectives.IgnoreQueryFilters()
				.Any(o => o.Id == s.ObjectiveId && o.CampaignId == id))
			.Select(s => s.Id)
			.ToListAsync();

		if (stepIds.Count > 0)
		{
			var dependencies = await _db.StepDependencies
				.Where(d => stepIds.Contains(d.StepId) || stepIds.Contains(d.DependsOnStepId))
				.ToListAsync();
			_db.StepDependencies.RemoveRange(dependencies);
		}

		_db.Campaigns.Remove(campaign);

		// Event is operator-global so it survives the campaign row.
		await RecordEventAsync(
			null, actor, ActivityEntityType.Campaign, id,
			"Deleted", $"Deleted campaign \"{title}\"",
			new { campaign_id = id, title });

		return true;
	}

	// ── Objectives ──

	public async Task<List<Objective>> GetObjectivesAsync(Guid? campaignId = null, bool includeDrafts = true)
	{
		var query = _db.Objectives.AsNoTracking().AsQueryable();

		if (campaignId.HasValue)
			query = query.Where(o => o.CampaignId == campaignId.Value);

		if (!includeDrafts)
			query = query.Where(o => o.PlanState == PlanState.Active);

		return await query
			.OrderBy(o => o.SortOrder)
			.ThenBy(o => o.CreatedAt)
			.ToListAsync();
	}

	public async Task<Objective?> GetObjectiveAsync(Guid id)
	{
		var objective = await _db.Objectives
			.AsNoTracking()
			.Include(o => o.Steps)
				.ThenInclude(s => s.Updates)
			.Include(o => o.Steps)
				.ThenInclude(s => s.Evidence)
			.AsSplitQuery()
			.FirstOrDefaultAsync(o => o.Id == id);

		if (objective is null)
			return null;

		objective.Steps = objective.Steps
			.OrderBy(s => s.SortOrder)
			.ThenBy(s => s.CreatedAt)
			.ToList();

		await PopulateDependencyRefsForStepsAsync(objective.Steps);
		return objective;
	}

	public async Task<Objective> CreateObjectiveAsync(
		Guid campaignId,
		string title,
		string description,
		PlanState planState,
		string actor)
	{
		// Atomic across connections and processes; also validates the campaign exists.
		var alias = await AllocateAliasAsync(campaignId);

		var now = DateTime.UtcNow;
		var maxSort = await _db.Objectives
			.Where(o => o.CampaignId == campaignId)
			.Select(o => (int?)o.SortOrder)
			.MaxAsync() ?? -1;

		var objective = new Objective
		{
			CampaignId = campaignId,
			Alias = alias,
			Title = title,
			Description = description,
			PlanState = planState,
			Status = ObjectiveStatuses.Planned,
			SortOrder = maxSort + 1,
			NextStepNumber = 1,
			CreatedAt = now,
			UpdatedAt = now,
		};

		_db.Objectives.Add(objective);

		await RecordEventAsync(
			campaignId, actor, ActivityEntityType.Objective, objective.Id,
			"Created", $"Created objective #{objective.Alias}: {objective.Title}",
			new { objective_id = objective.Id, alias = objective.Alias, plan_state = objective.PlanState.ToString() });

		return objective;
	}

	public async Task<Objective?> UpdateObjectiveAsync(Guid id, ObjectivePatch patch, string actor)
	{
		var objective = await _db.Objectives.FirstOrDefaultAsync(o => o.Id == id);
		if (objective is null)
			return null;

		var oldPlanState = objective.PlanState;
		var planStateChanged = patch.PlanState.HasValue && patch.PlanState.Value != objective.PlanState;

		if (patch.Title is not null) objective.Title = patch.Title;
		if (patch.Description is not null) objective.Description = patch.Description;
		if (patch.PlanState is not null) objective.PlanState = patch.PlanState.Value;
		if (patch.SortOrder is not null) objective.SortOrder = patch.SortOrder.Value;
		objective.UpdatedAt = DateTime.UtcNow;

		var action = planStateChanged ? "PlanStateChanged" : "Updated";
		var summary = planStateChanged
			? $"Objective #{objective.Alias} plan state changed {oldPlanState} -> {objective.PlanState}"
			: $"Updated objective #{objective.Alias}: {objective.Title}";

		await RecordEventAsync(
			objective.CampaignId, actor, ActivityEntityType.Objective, objective.Id,
			action, summary,
			new
			{
				alias = objective.Alias,
				old_plan_state = oldPlanState.ToString(),
				new_plan_state = objective.PlanState.ToString(),
			});

		return objective;
	}

	public async Task<bool> ReorderObjectivesAsync(Guid campaignId, Dictionary<Guid, int> orders, string actor)
	{
		if (orders is null || orders.Count == 0)
			return false;

		var ids = orders.Keys.ToList();
		var objectives = await _db.Objectives
			.Where(o => o.CampaignId == campaignId && ids.Contains(o.Id))
			.ToListAsync();

		if (objectives.Count == 0)
			return false;

		var now = DateTime.UtcNow;
		foreach (var objective in objectives)
		{
			if (orders.TryGetValue(objective.Id, out var order))
			{
				objective.SortOrder = order;
				objective.UpdatedAt = now;
			}
		}

		await RecordEventAsync(
			campaignId, actor, ActivityEntityType.Campaign, campaignId,
			"Reordered", $"Reordered {objectives.Count} objective(s)",
			new { count = objectives.Count });

		return true;
	}

	/// <summary>
	/// Soft delete. The row is retained so old <c>#N</c> references stay resolvable.
	/// Child steps are soft-deleted as well.
	/// </summary>
	public async Task<bool> DeleteObjectiveAsync(Guid id, string actor)
	{
		var objective = await _db.Objectives.FirstOrDefaultAsync(o => o.Id == id);
		if (objective is null)
			return false;

		var now = DateTime.UtcNow;
		objective.DeletedAt = now;
		objective.UpdatedAt = now;

		var steps = await _db.Steps
			.Where(s => s.ObjectiveId == id)
			.ToListAsync();
		foreach (var step in steps)
		{
			step.DeletedAt = now;
			step.UpdatedAt = now;
		}

		// StepDependency uses Restrict. Clear edges touching the deleted steps so
		// surviving steps are not left with dangling dependency references.
		var stepIds = steps.Select(s => s.Id).ToList();
		if (stepIds.Count > 0)
		{
			var dependencies = await _db.StepDependencies
				.Where(d => stepIds.Contains(d.StepId) || stepIds.Contains(d.DependsOnStepId))
				.ToListAsync();
			_db.StepDependencies.RemoveRange(dependencies);
		}

		await RecordEventAsync(
			objective.CampaignId, actor, ActivityEntityType.Objective, objective.Id,
			"Deleted", $"Deleted objective #{objective.Alias}: {objective.Title}",
			new { objective_id = objective.Id, alias = objective.Alias });

		return true;
	}

	// ── Steps ──

	public async Task<List<Step>> GetStepsAsync(Guid objectiveId)
	{
		var steps = await _db.Steps.AsNoTracking()
			.Include(s => s.Updates)
			.Include(s => s.Evidence)
			.Where(s => s.ObjectiveId == objectiveId)
			.OrderBy(s => s.SortOrder)
			.ThenBy(s => s.CreatedAt)
			.ToListAsync();

		foreach (var step in steps)
		{
			step.Updates = step.Updates.OrderByDescending(u => u.CreatedAt).ToList();
			step.Evidence = step.Evidence.OrderBy(e => e.CreatedAt).ToList();
		}

		await PopulateDependencyRefsForStepsAsync(steps);
		return steps;
	}

	public async Task<Step?> GetStepAsync(Guid id)
	{
		var step = await _db.Steps.AsNoTracking()
			.Include(s => s.Updates)
			.Include(s => s.Evidence)
			.FirstOrDefaultAsync(s => s.Id == id);

		if (step is null)
			return null;

		step.Updates = step.Updates.OrderByDescending(u => u.CreatedAt).ToList();
		step.Evidence = step.Evidence.OrderBy(e => e.CreatedAt).ToList();

		await PopulateDependencyRefsForStepsAsync([step]);
		return step;
	}

	public async Task<Step> CreateStepAsync(
		Guid objectiveId,
		string title,
		string context,
		string assignee,
		string assigner,
		IEnumerable<Guid> dependsOn,
		string actor)
	{
		var objective = await _db.Objectives.FirstOrDefaultAsync(o => o.Id == objectiveId)
			?? throw new InvalidOperationException($"Objective not found: {objectiveId}");

		var dependencyIds = await NormalizeAndValidateDependenciesAsync(
			objective.CampaignId, Guid.Empty, dependsOn);

		var now = DateTime.UtcNow;
		var maxSort = await _db.Steps
			.Where(s => s.ObjectiveId == objectiveId)
			.Select(s => (int?)s.SortOrder)
			.MaxAsync() ?? -1;

		var step = new Step
		{
			ObjectiveId = objectiveId,
			StepNumber = objective.NextStepNumber++,
			Title = title,
			Context = context,
			Assignee = assignee,
			Assigner = string.IsNullOrWhiteSpace(assigner) ? "orchestrator" : assigner,
			Status = StepStatuses.Open,
			SortOrder = maxSort + 1,
			CreatedAt = now,
			UpdatedAt = now,
		};

		_db.Steps.Add(step);

		foreach (var dependencyId in dependencyIds)
		{
			_db.StepDependencies.Add(new StepDependency
			{
				StepId = step.Id,
				DependsOnStepId = dependencyId,
			});
		}

		objective.Status = await ComputeObjectiveStatusAsync(objectiveId, step);
		objective.UpdatedAt = now;

		await RecordEventAsync(
			objective.CampaignId, actor, ActivityEntityType.Step, step.Id,
			"Created", $"Created step #{objective.Alias}.{step.StepNumber}: {step.Title}",
			new
			{
				objective_id = objectiveId,
				objective_alias = objective.Alias,
				step_number = step.StepNumber,
				depends_on = dependencyIds,
			});

		return step;
	}

	public async Task<Step?> UpdateStepAsync(
		Guid id,
		StepPatch patch,
		IEnumerable<Guid>? dependsOn,
		string actor)
	{
		var step = await _db.Steps
			.Include(s => s.Objective)
			.FirstOrDefaultAsync(s => s.Id == id);
		if (step is null || step.Objective is null)
			return null;

		var oldStatus = step.Status;

		if (patch.Title is not null) step.Title = patch.Title;
		if (patch.Context is not null) step.Context = patch.Context;
		if (patch.Assignee is not null) step.Assignee = patch.Assignee;
		if (patch.Assigner is not null) step.Assigner = patch.Assigner;
		if (patch.Status is not null)
		{
			if (!StepStatuses.IsValid(patch.Status))
				throw new InvalidOperationException(
					$"Invalid step status '{patch.Status}'. Valid values: {string.Join(", ", StepStatuses.All)}");
			step.Status = patch.Status;
		}

		if (dependsOn is not null)
		{
			var dependencyIds = await NormalizeAndValidateDependenciesAsync(
				step.Objective.CampaignId, step.Id, dependsOn);

			var existing = await _db.StepDependencies
				.Where(d => d.StepId == step.Id)
				.ToListAsync();
			_db.StepDependencies.RemoveRange(existing);

			foreach (var dependencyId in dependencyIds)
			{
				_db.StepDependencies.Add(new StepDependency
				{
					StepId = step.Id,
					DependsOnStepId = dependencyId,
				});
			}
		}

		var now = DateTime.UtcNow;
		step.UpdatedAt = now;

		var status = await ComputeObjectiveStatusAsync(step.ObjectiveId, step);
		await ApplyObjectiveStatusAsync(step.ObjectiveId, status, now);

		var statusChanged = !string.Equals(oldStatus, step.Status, StringComparison.Ordinal);
		var action = statusChanged ? "StatusChanged" : "Updated";
		var summary = statusChanged
			? $"Step #{step.Objective.Alias}.{step.StepNumber} status changed {oldStatus} -> {step.Status}"
			: $"Updated step #{step.Objective.Alias}.{step.StepNumber}: {step.Title}";

		await RecordEventAsync(
			step.Objective.CampaignId, actor, ActivityEntityType.Step, step.Id,
			action, summary,
			new
			{
				objective_id = step.ObjectiveId,
				step_number = step.StepNumber,
				old_status = oldStatus,
				new_status = step.Status,
				depends_on = dependsOn,
			});

		return step;
	}

	public async Task<Step?> UpdateStepStatusAsync(Guid id, string status, string actor)
	{
		if (!StepStatuses.IsValid(status))
			throw new InvalidOperationException(
				$"Invalid step status '{status}'. Valid values: {string.Join(", ", StepStatuses.All)}");

		var step = await _db.Steps
			.Include(s => s.Objective)
			.FirstOrDefaultAsync(s => s.Id == id);
		if (step is null || step.Objective is null)
			return null;

		var oldStatus = step.Status;
		var now = DateTime.UtcNow;
		step.Status = status;
		step.UpdatedAt = now;

		var derived = await ComputeObjectiveStatusAsync(step.ObjectiveId, step);
		await ApplyObjectiveStatusAsync(step.ObjectiveId, derived, now);

		await RecordEventAsync(
			step.Objective.CampaignId, actor, ActivityEntityType.Step, step.Id,
			"StatusChanged", $"Step #{step.Objective.Alias}.{step.StepNumber} status changed {oldStatus} -> {status}",
			new
			{
				objective_id = step.ObjectiveId,
				step_number = step.StepNumber,
				old_status = oldStatus,
				new_status = status,
			});

		return step;
	}

	/// <summary>Reordering does not change <see cref="Step.StepNumber"/>.</summary>
	public async Task<bool> ReorderStepsAsync(Guid objectiveId, Dictionary<Guid, int> orders, string actor)
	{
		if (orders is null || orders.Count == 0)
			return false;

		var objective = await _db.Objectives.FirstOrDefaultAsync(o => o.Id == objectiveId);
		if (objective is null)
			return false;

		var ids = orders.Keys.ToList();
		var steps = await _db.Steps
			.Where(s => s.ObjectiveId == objectiveId && ids.Contains(s.Id))
			.ToListAsync();

		if (steps.Count == 0)
			return false;

		var now = DateTime.UtcNow;
		foreach (var step in steps)
		{
			if (orders.TryGetValue(step.Id, out var order))
			{
				step.SortOrder = order;
				step.UpdatedAt = now;
			}
		}

		await RecordEventAsync(
			objective.CampaignId, actor, ActivityEntityType.Objective, objectiveId,
			"Reordered", $"Reordered {steps.Count} step(s) in objective #{objective.Alias}",
			new { objective_id = objectiveId, count = steps.Count });

		return true;
	}

	/// <summary>
	/// Soft delete. Refuses to break inbound dependencies unless <paramref name="force"/>
	/// is true; force removes every edge touching the step and counts them.
	/// Outbound edges are always removed and counted. Step numbers are never reused.
	/// </summary>
	public async Task<DeleteStepResult> DeleteStepAsync(Guid id, bool force, string actor)
	{
		var step = await _db.Steps
			.Include(s => s.Objective)
			.FirstOrDefaultAsync(s => s.Id == id);
		if (step is null || step.Objective is null)
			return new(false, 0);

		var dependents = await (from d in _db.StepDependencies.AsNoTracking()
								join s in _db.Steps.AsNoTracking() on d.StepId equals s.Id
								join o in _db.Objectives.AsNoTracking() on s.ObjectiveId equals o.Id
								where d.DependsOnStepId == step.Id && s.DeletedAt == null
								orderby o.Alias, s.StepNumber
								select new { o.Alias, s.StepNumber })
								.ToListAsync();

		if (dependents.Count > 0 && !force)
		{
			var refs = string.Join(", ", dependents.Select(d => $"#{d.Alias}.{d.StepNumber}"));
			throw new InvalidOperationException(
				$"Step #{step.Objective.Alias}.{step.StepNumber} is a dependency of {refs}. " +
				"Remove those dependencies first or pass force=true.");
		}

		var now = DateTime.UtcNow;
		step.DeletedAt = now;
		step.UpdatedAt = now;

		var dependencies = await _db.StepDependencies
			.Where(d => d.StepId == step.Id || d.DependsOnStepId == step.Id)
			.ToListAsync();
		_db.StepDependencies.RemoveRange(dependencies);

		var derived = await ComputeObjectiveStatusAsync(step.ObjectiveId, excludeStepId: step.Id);
		await ApplyObjectiveStatusAsync(step.ObjectiveId, derived, now);

		await RecordEventAsync(
			step.Objective.CampaignId, actor, ActivityEntityType.Step, step.Id,
			"Deleted", $"Deleted step #{step.Objective.Alias}.{step.StepNumber}: {step.Title}",
			new
			{
				objective_id = step.ObjectiveId,
				step_number = step.StepNumber,
				removed_dependencies = dependencies.Count,
			});

		return new(true, dependencies.Count);
	}

	public async Task<StepUpdate?> AddStepUpdateAsync(Guid stepId, string body, StepUpdateKind kind, string actor)
	{
		var info = await GetStepContextAsync(stepId);
		if (info is null)
			return null;

		var update = new StepUpdate
		{
			StepId = stepId,
			Actor = NormalizeActor(actor),
			Kind = kind,
			Body = body,
			CreatedAt = DateTime.UtcNow,
		};

		_db.StepUpdates.Add(update);

		await RecordEventAsync(
			info.Value.CampaignId, actor, ActivityEntityType.Step, stepId,
			"UpdateAdded", $"Added {kind} update to step #{info.Value.ObjectiveAlias}.{info.Value.StepNumber}",
			new { objective_id = info.Value.ObjectiveId, step_number = info.Value.StepNumber, kind = kind.ToString() });

		return update;
	}

	public async Task<Evidence?> AddEvidenceAsync(
		Guid stepId,
		string label,
		EvidenceKind kind,
		string value,
		string actor)
	{
		var info = await GetStepContextAsync(stepId);
		if (info is null)
			return null;

		var evidence = new Evidence
		{
			StepId = stepId,
			Label = label,
			Kind = kind,
			Value = value,
			CreatedBy = NormalizeActor(actor),
			CreatedAt = DateTime.UtcNow,
		};

		_db.Evidence.Add(evidence);

		await RecordEventAsync(
			info.Value.CampaignId, actor, ActivityEntityType.Step, stepId,
			"EvidenceAdded", $"Added evidence to step #{info.Value.ObjectiveAlias}.{info.Value.StepNumber}: {label}",
			new { objective_id = info.Value.ObjectiveId, step_number = info.Value.StepNumber, kind = kind.ToString() });

		return evidence;
	}

	/// <summary>
	/// Removes evidence scoped to a step. A hard delete: evidence is a pointer to
	/// an external artifact, not a record, so once the citation is withdrawn there
	/// is nothing to retain. Records a <c>Deleted</c> activity event on the step's
	/// campaign naming the step and the withdrawn label.
	/// </summary>
	public async Task<Evidence?> RemoveEvidenceAsync(Guid stepId, Guid evidenceId, string actor)
	{
		var info = await GetStepContextAsync(stepId);
		if (info is null)
			return null;

		var evidence = await _db.Evidence.FirstOrDefaultAsync(e => e.Id == evidenceId && e.StepId == stepId);
		if (evidence is null)
			return null;

		_db.Evidence.Remove(evidence);

		await RecordEventAsync(
			info.Value.CampaignId, actor, ActivityEntityType.Step, stepId,
			"Deleted", $"Removed evidence from step #{info.Value.ObjectiveAlias}.{info.Value.StepNumber}: {evidence.Label}",
			new { objective_id = info.Value.ObjectiveId, step_number = info.Value.StepNumber, kind = evidence.Kind.ToString() });

		return evidence;
	}

	public async Task<List<StepUpdate>> GetStepUpdatesAsync(Guid stepId)
	{
		return await _db.StepUpdates.AsNoTracking()
			.Where(u => u.StepId == stepId)
			.OrderByDescending(u => u.CreatedAt)
			.ToListAsync();
	}

	public async Task<List<Evidence>> GetEvidenceAsync(Guid stepId)
	{
		return await _db.Evidence.AsNoTracking()
			.Where(e => e.StepId == stepId)
			.OrderBy(e => e.CreatedAt)
			.ToListAsync();
	}

	// ── Plans, briefing, aliases ──

	public async Task<List<Objective>> GetPlanAsync(Guid campaignId, bool includeDrafts = true)
	{
		var query = _db.Objectives.AsNoTracking()
			.Where(o => o.CampaignId == campaignId);

		if (!includeDrafts)
			query = query.Where(o => o.PlanState == PlanState.Active);

		var objectives = await query
			.Include(o => o.Steps)
				.ThenInclude(s => s.Updates)
			.Include(o => o.Steps)
				.ThenInclude(s => s.Evidence)
			.OrderBy(o => o.SortOrder)
			.ThenBy(o => o.CreatedAt)
			.AsSplitQuery()
			.ToListAsync();

		foreach (var objective in objectives)
		{
			objective.Steps = objective.Steps
				.OrderBy(s => s.SortOrder)
				.ThenBy(s => s.CreatedAt)
				.ToList();
		}

		await PopulateDependencyRefsForStepsAsync(objectives.SelectMany(o => o.Steps));
		return objectives;
	}

	/// <summary>Resolves an objective first, then a Bunker note. Soft-deleted objects resolve.</summary>
	public async Task<AliasResolution?> ResolveAliasAsync(Guid campaignId, int alias)
	{
		var objective = await _db.Objectives
			.IgnoreQueryFilters()
			.AsNoTracking()
			.FirstOrDefaultAsync(o => o.CampaignId == campaignId && o.Alias == alias);
		if (objective is not null)
		{
			return new AliasResolution(
				"Objective", objective.Id, objective.Title,
				objective.DeletedAt is not null, campaignId, alias);
		}

		var note = await _db.BunkerNotes
			.IgnoreQueryFilters()
			.AsNoTracking()
			.FirstOrDefaultAsync(n => n.CampaignId == campaignId && n.Alias == alias);
		if (note is not null)
		{
			return new AliasResolution(
				"Note", note.Id, note.Title,
				note.DeletedAt is not null, campaignId, alias);
		}

		return null;
	}

	/// <summary>Resolves <c>#N.M</c> to a step.</summary>
	public async Task<Step?> GetStepByRefAsync(Guid campaignId, int objectiveAlias, int stepNumber)
	{
		var objective = await _db.Objectives.AsNoTracking()
			.FirstOrDefaultAsync(o => o.CampaignId == campaignId && o.Alias == objectiveAlias);
		if (objective is null)
			return null;

		var step = await _db.Steps.AsNoTracking()
			.Include(s => s.Updates)
			.Include(s => s.Evidence)
			.FirstOrDefaultAsync(s => s.ObjectiveId == objective.Id && s.StepNumber == stepNumber);
		if (step is null)
			return null;

		step.Updates = step.Updates.OrderByDescending(u => u.CreatedAt).ToList();
		step.Evidence = step.Evidence.OrderBy(e => e.CreatedAt).ToList();

		await PopulateDependencyRefsForStepsAsync([step]);
		return step;
	}

	// ── Bunker ──

	public async Task<List<BunkerNote>> GetNotesAsync(
		Guid? campaignId,
		string? search,
		BunkerNoteKind? kind,
		BunkerNoteStatus? status,
		bool includeDeleted = false,
		bool? includeDormant = null)
	{
		var query = _db.BunkerNotes.AsNoTracking().AsQueryable();

		if (includeDeleted)
			query = query.IgnoreQueryFilters();
		else if (campaignId.HasValue)
			query = query.Where(n => n.CampaignId == campaignId.Value);

		if (includeDeleted && campaignId.HasValue)
			query = query.Where(n => n.CampaignId == campaignId.Value);

		if (!string.IsNullOrWhiteSpace(search))
		{
			var lower = search.ToLowerInvariant();
			query = query.Where(n =>
				n.Title.ToLower().Contains(lower) ||
				n.Body.ToLower().Contains(lower) ||
				n.Tags.ToLower().Contains(lower));
		}

		if (kind.HasValue)
			query = query.Where(n => n.Kind == kind.Value);

		if (status.HasValue)
		{
			if (status.Value == BunkerNoteStatus.Active)
				query = query.Where(n => !_db.BunkerNotes.Any(x =>
					x.SupersedesId == n.Id && x.DeletedAt == null));
			else if (status.Value == BunkerNoteStatus.Superseded)
				query = query.Where(n => _db.BunkerNotes.Any(x =>
					x.SupersedesId == n.Id && x.DeletedAt == null));
		}

		var notes = await query
			.Include(n => n.Links)
			.OrderByDescending(n => n.Pinned)
			.ThenByDescending(n => n.CreatedAt)
			.ToListAsync();

		foreach (var note in notes)
			PopulateNoteLinks(note);

		await PopulateDerivedStatusAsync(notes);

		if (includeDormant is not null && notes.Count > 0)
		{
			var now = DateTime.UtcNow;
			var (liveObjectiveIds, liveStepIds) = await GetLiveLinkTargetsAsync();
			foreach (var note in notes)
				NoteDormancy.MarkDormancy(note, now, liveObjectiveIds, liveStepIds);

			if (includeDormant == false)
				notes = notes.Where(n => !n.Dormant).ToList();
		}

		return notes;
	}

	/// <summary>
	/// Ids of objectives and steps that count as live link targets for dormancy:
	/// not finished and not deleted. An objective is live unless soft-deleted or
	/// completed; a step is live unless it or its objective is soft-deleted and it
	/// is not done.
	/// </summary>
	private async Task<(IReadOnlySet<Guid> LiveObjectiveIds, IReadOnlySet<Guid> LiveStepIds)>
		GetLiveLinkTargetsAsync()
	{
		var liveObjectiveIds = (await _db.Objectives.AsNoTracking()
				.Where(o => o.DeletedAt == null && o.Status != ObjectiveStatuses.Completed)
				.Select(o => o.Id)
				.ToListAsync())
			.ToHashSet();

		var liveStepIds = (await (from s in _db.Steps.AsNoTracking()
								  join o in _db.Objectives.AsNoTracking() on s.ObjectiveId equals o.Id
								  where s.DeletedAt == null &&
										s.Status != StepStatuses.Done &&
										o.DeletedAt == null
								  select s.Id).ToListAsync())
			.ToHashSet();

		return (liveObjectiveIds, liveStepIds);
	}

	public async Task<BunkerNote?> GetNoteAsync(Guid id)
	{
		var note = await _db.BunkerNotes.AsNoTracking()
			.Include(n => n.Links)
			.FirstOrDefaultAsync(n => n.Id == id);
		if (note is not null)
		{
			PopulateNoteLinks(note);
			if (await _db.BunkerNotes.AnyAsync(x =>
					x.SupersedesId == note.Id && x.DeletedAt == null))
				note.Status = BunkerNoteStatus.Superseded;
		}
		return note;
	}

	/// <summary>Alias lookup includes soft-deleted notes so history stays resolvable.</summary>
	public async Task<BunkerNote?> GetNoteByAliasAsync(Guid campaignId, int alias)
	{
		var note = await _db.BunkerNotes.AsNoTracking()
			.IgnoreQueryFilters()
			.Include(n => n.Links)
			.FirstOrDefaultAsync(n => n.CampaignId == campaignId && n.Alias == alias);
		if (note is not null)
		{
			PopulateNoteLinks(note);
			if (await _db.BunkerNotes.AnyAsync(x =>
					x.SupersedesId == note.Id && x.DeletedAt == null))
				note.Status = BunkerNoteStatus.Superseded;
		}
		return note;
	}

	/// <summary>
	/// Populates the derived <see cref="BunkerNote.Status"/> on a collection of
	/// notes using a single query for the set of superseded note ids. Never
	/// stored — computed on read.
	/// </summary>
	private async Task PopulateDerivedStatusAsync(List<BunkerNote> notes)
	{
		if (notes.Count == 0)
			return;

		var ids = notes.Select(n => n.Id).ToHashSet();
		var supersededIds = await _db.BunkerNotes.AsNoTracking()
			.Where(x => x.SupersedesId != null &&
						ids.Contains(x.SupersedesId.Value) &&
						x.DeletedAt == null)
			.Select(x => x.SupersedesId!.Value)
			.ToListAsync();

		var set = supersededIds.ToHashSet();
		foreach (var note in notes)
		{
			if (set.Contains(note.Id))
				note.Status = BunkerNoteStatus.Superseded;
		}
	}

	public async Task<BunkerNote> CreateNoteAsync(
		Guid campaignId,
		BunkerNoteKind kind,
		string title,
		string body,
		string tags,
		bool pinned,
		string createdBy,
		IEnumerable<Guid> linkedObjectiveIds,
		IEnumerable<Guid> linkedStepIds,
		Guid? supersedesId,
		string? basis = null)
	{
		// Keep campaign-not-found precedence over link validation, as before.
		if (!await _db.Campaigns.AsNoTracking().AnyAsync(c => c.Id == campaignId))
			throw new InvalidOperationException($"Campaign not found: {campaignId}");

		var objectiveIds = (linkedObjectiveIds ?? Enumerable.Empty<Guid>()).Distinct().ToList();
		var stepIds = (linkedStepIds ?? Enumerable.Empty<Guid>()).Distinct().ToList();

		if (objectiveIds.Count > 0)
		{
			var found = await _db.Objectives.AsNoTracking()
				.Where(o => objectiveIds.Contains(o.Id) && o.CampaignId == campaignId)
				.Select(o => o.Id)
				.ToListAsync();
			if (found.Count != objectiveIds.Count)
				throw new InvalidOperationException("One or more linked objectives were not found in this campaign.");
		}

		if (stepIds.Count > 0)
		{
			var found = await (from s in _db.Steps.AsNoTracking()
							   join o in _db.Objectives.AsNoTracking() on s.ObjectiveId equals o.Id
							   where stepIds.Contains(s.Id) && o.CampaignId == campaignId
							   select s.Id).ToListAsync();
			if (found.Count != stepIds.Count)
				throw new InvalidOperationException("One or more linked steps were not found in this campaign.");
		}

		if (supersedesId.HasValue)
		{
			var exists = await _db.BunkerNotes.AsNoTracking()
				.AnyAsync(n => n.Id == supersedesId.Value && n.CampaignId == campaignId);
			if (!exists)
				throw new InvalidOperationException("The superseded note was not found in this campaign.");
		}

		// Atomic across connections and processes. Allocated after validation so an
		// invalid link request does not consume an alias.
		var alias = await AllocateAliasAsync(campaignId);

		var now = DateTime.UtcNow;
		var note = new BunkerNote
		{
			CampaignId = campaignId,
			Alias = alias,
			Kind = kind,
			Title = title,
			Body = body,
			Pinned = pinned,
			Tags = tags,
			CreatedBy = NormalizeActor(createdBy),
			CreatedAt = now,
			UpdatedAt = now,
			SupersedesId = supersedesId,
			Basis = basis,
		};

		foreach (var objectiveId in objectiveIds)
			note.Links.Add(new BunkerNoteLink { NoteId = note.Id, ObjectiveId = objectiveId });
		foreach (var stepId in stepIds)
			note.Links.Add(new BunkerNoteLink { NoteId = note.Id, StepId = stepId });

		_db.BunkerNotes.Add(note);
		PopulateNoteLinks(note);

		await RecordEventAsync(
			campaignId, createdBy, ActivityEntityType.Note, note.Id,
			"Created", $"Created {kind} note #{note.Alias}: {note.Title}",
			new
			{
				note_id = note.Id,
				alias = note.Alias,
				kind = kind.ToString(),
				linked_objective_ids = objectiveIds,
				linked_step_ids = stepIds,
			});

		return note;
	}

	public async Task<BunkerNote?> UpdateNoteAsync(Guid id, BunkerNotePatch patch, string actor)
	{
		var note = await _db.BunkerNotes
			.Include(n => n.Links)
			.FirstOrDefaultAsync(n => n.Id == id);
		if (note is null)
			return null;

		if (patch.Title is not null) note.Title = patch.Title;
		if (patch.Body is not null)
		{
			// A confirmation is of the text that was confirmed: changing the body
			// clears it. Assigning the same text again is not a change, so a client
			// that round-trips the body does not silently drop the approval.
			if (patch.Body != note.Body)
				note.ApprovedAt = null;
			note.Body = patch.Body;
		}
		if (patch.Pinned is not null) note.Pinned = patch.Pinned.Value;
		if (patch.Tags is not null) note.Tags = patch.Tags;
		if (patch.Basis is not null) note.Basis = patch.Basis;
		if (patch.ApprovedAt is not null) note.ApprovedAt = patch.ApprovedAt;
		if (patch.SupersedesId is not null)
		{
			if (patch.SupersedesId.Value == note.Id)
				throw new InvalidOperationException("A note cannot supersede itself.");

			var exists = await _db.BunkerNotes.AsNoTracking()
				.AnyAsync(n => n.Id == patch.SupersedesId.Value && n.CampaignId == note.CampaignId);
			if (!exists)
				throw new InvalidOperationException("The superseded note was not found in this campaign.");

			note.SupersedesId = patch.SupersedesId.Value;
		}

		note.UpdatedAt = DateTime.UtcNow;
		PopulateNoteLinks(note);

		var action = "Updated";
		var summary = $"Updated {note.Kind} note #{note.Alias}: {note.Title}";

		await RecordEventAsync(
			note.CampaignId, actor, ActivityEntityType.Note, note.Id,
			action, summary,
			new
			{
				alias = note.Alias,
				kind = note.Kind.ToString(),
			});

		return note;
	}

	public async Task<bool> DeleteNoteAsync(Guid id, string actor)
	{
		var note = await _db.BunkerNotes.FirstOrDefaultAsync(n => n.Id == id);
		if (note is null)
			return false;

		var now = DateTime.UtcNow;
		note.DeletedAt = now;
		note.UpdatedAt = now;

		await RecordEventAsync(
			note.CampaignId, actor, ActivityEntityType.Note, note.Id,
			"Deleted", $"Deleted {note.Kind} note #{note.Alias}: {note.Title}",
			new { note_id = note.Id, alias = note.Alias });

		return true;
	}

	public async Task<List<BunkerNote>> GetDirectivesAsync(Guid campaignId)
	{
		var notes = await _db.BunkerNotes.AsNoTracking()
			.Include(n => n.Links)
			.Where(n => n.CampaignId == campaignId &&
						n.Kind == BunkerNoteKind.Directive &&
						!_db.BunkerNotes.Any(x =>
							x.SupersedesId == n.Id && x.DeletedAt == null))
			.OrderByDescending(n => n.Pinned)
			.ThenByDescending(n => n.CreatedAt)
			.ToListAsync();

		foreach (var note in notes)
			PopulateNoteLinks(note);

		await PopulateDerivedStatusAsync(notes);
		return notes;
	}

	// ── Preferences ──

	public async Task<List<Preference>> GetPreferencesAsync(bool includeInactive = false)
	{
		var query = _db.Preferences.AsNoTracking().AsQueryable();

		if (!includeInactive)
			query = query.Where(p => p.Active);

		return await query
			.OrderByDescending(p => p.UpdatedAt)
			.ToListAsync();
	}

	public async Task<Preference> CreatePreferenceAsync(string text, string rationale, string tags, string source)
	{
		var now = DateTime.UtcNow;
		var preference = new Preference
		{
			Text = text,
			Rationale = rationale,
			Tags = tags,
			Source = string.IsNullOrWhiteSpace(source) ? "human" : source.Trim(),
			Active = true,
			CreatedAt = now,
			UpdatedAt = now,
		};

		_db.Preferences.Add(preference);

		await RecordEventAsync(
			null, preference.Source, ActivityEntityType.Preference, preference.Id,
			"Created", $"Captured preference: {preference.Text}",
			new { preference_id = preference.Id, source = preference.Source });

		return preference;
	}

	public async Task<Preference?> UpdatePreferenceAsync(Guid id, PreferencePatch patch)
	{
		var preference = await _db.Preferences.FirstOrDefaultAsync(p => p.Id == id);
		if (preference is null)
			return null;

		var wasActive = preference.Active;

		if (patch.Text is not null) preference.Text = patch.Text;
		if (patch.Rationale is not null) preference.Rationale = patch.Rationale;
		if (patch.Active is not null) preference.Active = patch.Active.Value;
		if (patch.Tags is not null) preference.Tags = patch.Tags;
		preference.UpdatedAt = DateTime.UtcNow;

		var action = wasActive != preference.Active
			? (preference.Active ? "Reactivated" : "Retired")
			: "Updated";

		await RecordEventAsync(
			null, preference.Source, ActivityEntityType.Preference, preference.Id,
			action, $"{action} preference: {preference.Text}",
			new { preference_id = preference.Id, active = preference.Active });

		return preference;
	}

	// ── Activity ──

	public async Task<List<ActivityEvent>> GetActivityAsync(Guid? campaignId, long? since, int limit = 50)
	{
		var query = _db.ActivityEvents.AsNoTracking().AsQueryable();

		if (campaignId.HasValue)
			query = query.Where(e => e.CampaignId == campaignId.Value);

		if (since.HasValue)
		{
			// Cursor semantics: return events after the cursor in ascending order.
			return await query
				.Where(e => e.Sequence > since.Value)
				.OrderBy(e => e.Sequence)
				.Take(limit)
				.ToListAsync();
		}

		return await query
			.OrderByDescending(e => e.Sequence)
			.Take(limit)
			.ToListAsync();
	}

	// ── Briefing and attention ──

	public async Task<Briefing> GetBriefingAsync(Guid campaignId, long? since = null)
	{
		var campaign = await _db.Campaigns.AsNoTracking()
			.FirstOrDefaultAsync(c => c.Id == campaignId)
			?? throw new InvalidOperationException($"Campaign not found: {campaignId}");

		var objectives = await _db.Objectives.AsNoTracking()
			.Where(o => o.CampaignId == campaignId)
			.Include(o => o.Steps)
				.ThenInclude(s => s.Dependencies)
					.ThenInclude(d => d.DependsOnStep)
			.AsSplitQuery()
			.ToListAsync();

		var ready = new List<BriefingStep>();
		var blocked = new List<BriefingStep>();
		var flagged = new List<BriefingStep>();

		foreach (var objective in objectives)
		{
			foreach (var step in objective.Steps)
			{
				var item = ToBriefingStep(objective, step);

				if (step.Status == StepStatuses.Flagged)
					flagged.Add(item);

				if (step.Status == StepStatuses.Blocked)
					blocked.Add(item);

				if (objective.PlanState == PlanState.Active &&
					step.Status == StepStatuses.Open &&
					step.Dependencies.All(d => d.DependsOnStep is not null &&
											   d.DependsOnStep.Status == StepStatuses.Done))
				{
					ready.Add(item);
				}
			}
		}

		var directives = (await GetDirectivesAsync(campaignId)).Take(BriefingNoteLimit).ToList();
		var preferences = (await GetPreferencesAsync()).Take(BriefingPreferenceLimit).ToList();

		var recentDecisions = await _db.BunkerNotes.AsNoTracking()
			.Include(n => n.Links)
			.Where(n => n.CampaignId == campaignId &&
						n.Kind == BunkerNoteKind.Decision &&
						!_db.BunkerNotes.Any(x =>
							x.SupersedesId == n.Id && x.DeletedAt == null))
			.OrderByDescending(n => n.CreatedAt)
			.Take(BriefingNoteLimit)
			.ToListAsync();
		foreach (var note in recentDecisions)
			PopulateNoteLinks(note);
		await PopulateDerivedStatusAsync(recentDecisions);

		var recentActivity = (await GetActivityAsync(campaignId, since)).Take(BriefingActivityLimit).ToList();

		if (!string.IsNullOrWhiteSpace(campaign.Charter))
			campaign.CharterExcerpt = Excerpt(campaign.Charter, CampaignCharterExcerptLength);

		var seeds = await _db.Objectives.AsNoTracking()
			.Where(o => o.CampaignId == campaignId && o.PlanState == PlanState.Seed)
			.OrderByDescending(o => o.UpdatedAt)
			.Take(BriefingSeedLimit)
			.Select(o => new BriefingSeed(o.Id, o.Alias, o.Title, o.Description, o.UpdatedAt))
			.ToListAsync();

		return new Briefing(
			campaign,
			ready.Take(BriefingStepLimit).ToList(),
			blocked.Take(BriefingStepLimit).ToList(),
			flagged.Take(BriefingStepLimit).ToList(),
			directives,
			preferences,
			recentDecisions,
			string.IsNullOrWhiteSpace(campaign.Charter) ? null : ToBriefingDocumentInline(campaign),
			new List<BriefingDocument>(),
			seeds,
			recentActivity);
	}

	public async Task<Attention> GetAttentionAsync(Guid? campaignId = null)
	{
		var hasCampaign = campaignId.HasValue;
		var cid = campaignId.GetValueOrDefault();
		var staleCutoff = DateTime.UtcNow.AddHours(-72);

		var flagged = await (from s in _db.Steps.AsNoTracking()
							 join o in _db.Objectives.AsNoTracking() on s.ObjectiveId equals o.Id
							 join c in _db.Campaigns.AsNoTracking() on o.CampaignId equals c.Id
							 where s.Status == StepStatuses.Flagged &&
								   (!hasCampaign || o.CampaignId == cid)
							 orderby s.UpdatedAt descending
							 select new AttentionItem(
								 "FlaggedStep", o.CampaignId, c.Title, s.Id, s.Title,
								 "Step is flagged for human attention", s.UpdatedAt,
								 o.Id, o.Alias, s.StepNumber, s.Status))
							 .Take(AttentionLimit)
							 .ToListAsync();

		var blockedObjectives = await (from o in _db.Objectives.AsNoTracking()
									   join c in _db.Campaigns.AsNoTracking() on o.CampaignId equals c.Id
									   where o.Status == ObjectiveStatuses.Blocked &&
											 (!hasCampaign || o.CampaignId == cid)
									   orderby o.UpdatedAt descending
									   select new AttentionItem(
										   "BlockedObjective", o.CampaignId, c.Title, o.Id, o.Title,
										   "Objective is blocked", o.UpdatedAt,
										   o.Id, o.Alias, null, o.Status))
									   .Take(AttentionLimit)
									   .ToListAsync();

		var staleInProgress = await (from s in _db.Steps.AsNoTracking()
									 join o in _db.Objectives.AsNoTracking() on s.ObjectiveId equals o.Id
									 join c in _db.Campaigns.AsNoTracking() on o.CampaignId equals c.Id
									 where s.Status == StepStatuses.InProgress &&
										   s.UpdatedAt < staleCutoff &&
										   (!hasCampaign || o.CampaignId == cid)
									 orderby s.UpdatedAt ascending
									 select new AttentionItem(
										 "StaleStep", o.CampaignId, c.Title, s.Id, s.Title,
										 "In-progress step has not been updated in 72 hours", s.UpdatedAt,
										 o.Id, o.Alias, s.StepNumber, s.Status))
									 .Take(AttentionLimit)
									 .ToListAsync();

		var draftObjectives = await (from o in _db.Objectives.AsNoTracking()
									 join c in _db.Campaigns.AsNoTracking() on o.CampaignId equals c.Id
									 where o.PlanState == PlanState.Draft &&
										   (!hasCampaign || o.CampaignId == cid)
									 orderby o.UpdatedAt descending
									 select new AttentionItem(
										 "DraftObjective", o.CampaignId, c.Title, o.Id, o.Title,
										 "Draft objective (parked)", o.CreatedAt,
										 o.Id, o.Alias, null, "Draft"))
									 .Take(AttentionLimit)
									 .ToListAsync();

		var seedObjectives = await (from o in _db.Objectives.AsNoTracking()
									join c in _db.Campaigns.AsNoTracking() on o.CampaignId equals c.Id
									where o.PlanState == PlanState.Seed &&
										  (!hasCampaign || o.CampaignId == cid)
									orderby o.UpdatedAt descending
									select new AttentionItem(
										"SeedObjective", o.CampaignId, c.Title, o.Id, o.Title,
										"Unshaped thought (needs drafting)", o.CreatedAt,
										o.Id, o.Alias, null, "Seed"))
									.Take(AttentionLimit)
									.ToListAsync();

		return new Attention(flagged, blockedObjectives, staleInProgress, draftObjectives, seedObjectives);
	}

	// ── Helpers ──

	private static BriefingStep ToBriefingStep(Objective objective, Step step)
	{
		return new BriefingStep(
			step.Id,
			objective.Id,
			objective.Alias,
			objective.Title,
			step.StepNumber,
			step.Title,
			step.Status,
			step.Assignee);
	}

	private static BriefingDocument ToBriefingDocumentInline(Campaign campaign)
	{
		return new BriefingDocument(
			campaign.Id,
			0,
			"Charter",
			BunkerNoteKind.Context,
			Excerpt(campaign.Charter, BriefingDocumentExcerptLength) ?? "",
			campaign.Tags,
			campaign.UpdatedAt);
	}

	private static BriefingDocument ToBriefingDocument(BunkerNote note)
	{
		return new BriefingDocument(
			note.Id,
			note.Alias,
			note.Title,
			note.Kind,
			Excerpt(note.Body, BriefingDocumentExcerptLength),
			note.Tags,
			note.UpdatedAt);
	}

	private static string Excerpt(string? body, int maxLength)
	{
		var text = (body ?? "").Trim();
		return text.Length <= maxLength
			? text
			: text[..maxLength].TrimEnd() + "…";
	}

	private static void PopulateNoteLinks(BunkerNote note)
	{
		note.LinkedObjectiveIds = note.Links
			.Where(l => l.ObjectiveId.HasValue)
			.Select(l => l.ObjectiveId!.Value)
			.Distinct()
			.ToList();

		note.LinkedStepIds = note.Links
			.Where(l => l.StepId.HasValue)
			.Select(l => l.StepId!.Value)
			.Distinct()
			.ToList();
	}

	private async Task<(Guid ObjectiveId, Guid CampaignId, int ObjectiveAlias, int StepNumber)?> GetStepContextAsync(
		Guid stepId)
	{
		var info = await (from s in _db.Steps.AsNoTracking()
						  join o in _db.Objectives.AsNoTracking() on s.ObjectiveId equals o.Id
						  where s.Id == stepId
						  select new
						  {
							  s.ObjectiveId,
							  CampaignId = o.CampaignId,
							  ObjectiveAlias = o.Alias,
							  s.StepNumber,
						  })
						  .FirstOrDefaultAsync();

		if (info is null)
			return null;

		return (info.ObjectiveId, info.CampaignId, info.ObjectiveAlias, info.StepNumber);
	}

	/// <summary>
	/// Validates a proposed dependency set: no self-reference, all steps exist in
	/// the same campaign, and the proposed set introduces no cycle.
	/// </summary>
	private async Task<List<Guid>> NormalizeAndValidateDependenciesAsync(
		Guid campaignId,
		Guid stepId,
		IEnumerable<Guid>? dependsOn)
	{
		var ids = (dependsOn ?? Enumerable.Empty<Guid>()).Distinct().ToList();
		if (ids.Count == 0)
			return ids;

		if (stepId != Guid.Empty && ids.Contains(stepId))
			throw new InvalidOperationException("A step cannot depend on itself.");

		var found = await (from s in _db.Steps.AsNoTracking()
						   join o in _db.Objectives.AsNoTracking() on s.ObjectiveId equals o.Id
						   where ids.Contains(s.Id)
						   select new { s.Id, o.CampaignId })
						  .ToListAsync();

		if (found.Count != ids.Count)
			throw new InvalidOperationException("One or more dependency steps were not found.");

		if (found.Any(f => f.CampaignId != campaignId))
			throw new InvalidOperationException("Dependencies must be within the same campaign.");

		await EnsureNoCycleAsync(campaignId, stepId, ids);
		return ids;
	}

	/// <summary>
	/// Cycle detection over the proposed graph, not just the persisted edges. The
	/// step's existing outgoing edges are excluded because they are being replaced.
	/// </summary>
	private async Task EnsureNoCycleAsync(Guid campaignId, Guid stepId, IReadOnlyCollection<Guid> proposedDependencies)
	{
		var edges = await (from d in _db.StepDependencies.AsNoTracking()
						   join s in _db.Steps.AsNoTracking() on d.StepId equals s.Id
						   join o in _db.Objectives.AsNoTracking() on s.ObjectiveId equals o.Id
						   where o.CampaignId == campaignId && d.StepId != stepId
						   select new { d.StepId, d.DependsOnStepId })
						  .ToListAsync();

		var graph = new Dictionary<Guid, List<Guid>>();
		void AddEdge(Guid from, Guid to)
		{
			if (!graph.TryGetValue(from, out var list))
			{
				list = new List<Guid>();
				graph[from] = list;
			}
			list.Add(to);
		}

		foreach (var edge in edges)
			AddEdge(edge.StepId, edge.DependsOnStepId);

		foreach (var dependencyId in proposedDependencies)
			AddEdge(stepId, dependencyId);

		var visited = new HashSet<Guid>();

		bool ReachesStep(Guid from)
		{
			if (from == stepId)
				return true;
			if (!visited.Add(from))
				return false;
			if (!graph.TryGetValue(from, out var next))
				return false;
			foreach (var target in next)
			{
				if (ReachesStep(target))
					return true;
			}
			return false;
		}

		foreach (var dependencyId in proposedDependencies)
		{
			if (ReachesStep(dependencyId))
				throw new InvalidOperationException(
					"The proposed dependencies introduce a cycle.");
		}
	}

	private async Task PopulateDependencyRefsForStepsAsync(IEnumerable<Step> steps)
	{
		var list = steps as ICollection<Step> ?? steps.ToList();
		if (list.Count == 0)
			return;

		var stepIds = list.Select(s => s.Id).ToList();

		var edges = await _db.StepDependencies.AsNoTracking()
			.Where(d => stepIds.Contains(d.StepId))
			.Select(d => new { d.StepId, d.DependsOnStepId })
			.ToListAsync();

		if (edges.Count == 0)
			return;

		var dependencyIds = edges.Select(e => e.DependsOnStepId).Distinct().ToList();

		var dependencyInfo = await (from s in _db.Steps.AsNoTracking().IgnoreQueryFilters()
									join o in _db.Objectives.AsNoTracking().IgnoreQueryFilters()
										on s.ObjectiveId equals o.Id
									where dependencyIds.Contains(s.Id)
									select new
									{
										s.Id,
										s.ObjectiveId,
										ObjectiveAlias = o.Alias,
										s.StepNumber,
										s.Title,
										s.Status,
									})
									.ToListAsync();

		var byId = dependencyInfo.ToDictionary(d => d.Id);

		foreach (var step in list)
		{
			step.DependsOn = edges
				.Where(e => e.StepId == step.Id)
				.Select(e => byId.TryGetValue(e.DependsOnStepId, out var dependency)
					? new StepDependencyRef(
						dependency.Id,
						dependency.ObjectiveId,
						dependency.ObjectiveAlias,
						dependency.StepNumber,
						dependency.Title,
						dependency.Status)
					: null)
				.Where(r => r is not null)
				.Cast<StepDependencyRef>()
				.ToList();
		}
	}
}
