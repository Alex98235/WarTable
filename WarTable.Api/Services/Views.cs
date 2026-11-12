using WarTable.Api.Models;
using WarTable.Api.Services.Embeddings;

namespace WarTable.Api.Services;

/// <summary>Compact objective identity for alias lookups; never the full plan.</summary>
public record ObjectiveSummary(
	Guid Id,
	Guid CampaignId,
	int Alias,
	string Title,
	string Description,
	PlanState PlanState,
	string Status,
	DateTime UpdatedAt);

/// <summary>
/// Alias resolution plus a compact objective summary and its bounded knowledge context.
/// Notes keep the original top-level fields and leave objective/knowledge null.
/// </summary>
public record AliasResolutionDetail(
	string Kind,
	Guid Id,
	string Title,
	bool Deleted,
	Guid CampaignId,
	int Alias,
	ObjectiveSummary? Objective,
	KnowledgeContext? Knowledge,
	string? KnowledgeError = null);

/// <summary>A resolved step with its bounded knowledge context.</summary>
public record StepLookupDetail(
	Step Step,
	KnowledgeContext? Knowledge,
	string? KnowledgeError = null);

/// <summary>Outcome of a step delete, including any dependency edges removed.</summary>
public record DeleteStepResult(bool Deleted, int RemovedDependencies);

/// <summary>One actionable step in a briefing, flattened with its objective context.</summary>
public record BriefingStep(
	Guid Id,
	Guid ObjectiveId,
	int ObjectiveAlias,
	string ObjectiveTitle,
	int StepNumber,
	string Title,
	string Status,
	string Assignee)
{
	/// <summary>Human-facing <c>#N.M</c> reference.</summary>
	public string Ref => $"#{ObjectiveAlias}.{StepNumber}";
}

/// <summary>An unfinished thought captured before drafting. Never actionable.</summary>
public record BriefingSeed(
	Guid Id,
	int Alias,
	string Title,
	string Description,
	DateTime UpdatedAt);

/// <summary>A bounded framing-document excerpt for campaign orientation.</summary>
public record BriefingDocument(
	Guid Id,
	int Alias,
	string Title,
	BunkerNoteKind Kind,
	string Excerpt,
	string Tags,
	DateTime UpdatedAt);

/// <summary>
/// Bounded, structured context for starting work. Lists are capped so the briefing
/// stays token-aware as a campaign grows.
/// </summary>
public record Briefing(
	Campaign Campaign,
	List<BriefingStep> ReadySteps,
	List<BriefingStep> BlockedSteps,
	List<BriefingStep> FlaggedSteps,
	List<BunkerNote> Directives,
	List<Preference> Preferences,
	List<BunkerNote> RecentDecisions,
	BriefingDocument? Charter,
	List<BriefingDocument> DesignDocs,
	List<BriefingSeed> Seeds,
	List<ActivityEvent> RecentActivity);

/// <summary>One cross-campaign attention inbox item.</summary>
public record AttentionItem(
	string Kind,
	Guid CampaignId,
	string CampaignTitle,
	Guid EntityId,
	string Title,
	string Detail,
	DateTime? Since,
	Guid? ObjectiveId = null,
	int? ObjectiveAlias = null,
	int? StepNumber = null,
	string? Status = null,
	int? NoteAlias = null);

/// <summary>Cross-campaign inbox. Draft plans are separated so they are not urgent.</summary>
public record Attention(
	List<AttentionItem> FlaggedSteps,
	List<AttentionItem> BlockedObjectives,
	List<AttentionItem> StaleInProgressSteps,
	List<AttentionItem> DraftObjectives,
	List<AttentionItem> SeedObjectives);
