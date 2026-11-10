using WarTable.Api.Models;

namespace WarTable.Api.Services;

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

/// <summary>
/// Bounded, structured context for starting work. Lists are capped so the briefing
/// stays token-aware as a campaign grows.
/// </summary>
public record Briefing(
    Campaign Campaign,
    List<BriefingStep> ReadySteps,
    List<BriefingStep> BlockedSteps,
    List<BriefingStep> FlaggedSteps,
    List<BunkerNote> OpenQuestions,
    List<BunkerNote> Directives,
    List<Preference> Preferences,
    List<BunkerNote> RecentDecisions,
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
    List<AttentionItem> OpenQuestions,
    List<AttentionItem> StaleInProgressSteps,
    List<AttentionItem> DraftObjectives,
    List<AttentionItem> SeedObjectives);
