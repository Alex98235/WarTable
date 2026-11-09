namespace WarTable.Api.Models;

/// <summary>
/// Append-only event log entry. The activity feed is a projection of this table,
/// never authored prose. No FK to Campaign so hard-deleted campaign events survive.
/// </summary>
public class ActivityEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Monotonic sequence used as a resync cursor.</summary>
    public long Sequence { get; set; }

    /// <summary>Null for operator-global events such as preferences.</summary>
    public Guid? CampaignId { get; set; }

    public string Actor { get; set; } = "human";
    public ActivityEntityType EntityType { get; set; }
    public Guid EntityId { get; set; }

    /// <summary>Created, Updated, StatusChanged, PlanStateChanged, NoteAdded, EvidenceAdded, Deleted, etc.</summary>
    public string Action { get; set; } = "";

    /// <summary>Human-readable sentence.</summary>
    public string Summary { get; set; } = "";

    /// <summary>JSON metadata: old/new values, aliases, step refs. Defaults to "{}".</summary>
    public string Metadata { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum ActivityEntityType
{
    Campaign,
    Objective,
    Step,
    Note,
    Preference,
}
