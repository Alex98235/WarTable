using System.Text.Json.Serialization;
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
	DateTime UpdatedAt,
	[property: JsonPropertyName("rowVersion")] Guid RowVersion);

/// <summary>
/// Alias resolution plus a compact objective summary and its bounded knowledge context.
/// Notes keep the original top-level fields and leave objective/knowledge null.
/// The row version is what makes an archived object reachable: restore and purge
/// both need it, and an archived object appears in no plan or list.
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
	string? KnowledgeError = null,
	[property: JsonPropertyName("rowVersion")] Guid RowVersion = default);

/// <summary>A resolved step with its bounded knowledge context.</summary>
public record StepLookupDetail(
	Step Step,
	KnowledgeContext? Knowledge,
	string? KnowledgeError = null);

/// <summary>Outcome of a step delete, including any dependency edges removed.</summary>
public record DeleteStepResult(bool Deleted, int RetainedDependencies);

/// <summary>One actionable step in a briefing, flattened with its objective context.</summary>
public record BriefingStep(
	Guid Id,
	Guid ObjectiveId,
	int ObjectiveAlias,
	string ObjectiveTitle,
	int StepNumber,
	string Title,
	string Status,
	string Assignee,
	DateTime UpdatedAt)
{
	/// <summary>Human-facing <c>#N.M</c> reference.</summary>
	public string Ref => $"#{ObjectiveAlias}.{StepNumber}";
}

/// <summary>
/// An unfinished thought captured before drafting. Never actionable.
/// </summary>
public record BriefingSeed(
	Guid Id,
	int Alias,
	string Title,
	string Description,
	DateTime UpdatedAt);

/// <summary>
/// A parked objective: planned but deliberately not started. Carries step counts,
/// never the steps themselves, so the briefing can say a plan exists without
/// dumping it.
/// </summary>
public record BriefingObjective(
	Guid Id,
	int Alias,
	string Title,
	int OpenSteps,
	int DoneSteps,
	DateTime UpdatedAt);

/// <summary>
/// A bounded briefing list that states how many entries it left out. Omission is
/// explicit on purpose: a reader must never mistake a truncated section for a
/// complete one.
/// </summary>
public record BriefingSection<T>(List<T> Items, int Omitted);

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
/// stays token-aware as a campaign grows, and each cap reports what it left out.
/// Activity is a separate trail; it is not part of the briefing.
/// </summary>
public record Briefing(
	Campaign Campaign,
	BriefingSection<BriefingStep> ReadySteps,
	BriefingSection<BriefingStep> InProgressSteps,
	BriefingSection<BriefingStep> BlockedSteps,
	BriefingSection<BriefingStep> FlaggedSteps,
	BriefingSection<BriefingObjective> ParkedObjectives,
	BriefingSection<BunkerNote> Directives,
	BriefingSection<Preference> Preferences,
	BriefingSection<BunkerNote> RecentDecisions,
	BriefingDocument? Charter,
	BriefingSection<BriefingSeed> Seeds);

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

/// <summary>Cross-campaign inbox. Backlog plans are separated so they are not urgent.</summary>
public record Attention(
	List<AttentionItem> FlaggedSteps,
	List<AttentionItem> BlockedObjectives,
	List<AttentionItem> StaleInProgressSteps,
	List<AttentionItem> BacklogObjectives,
	List<AttentionItem> SeedObjectives);
