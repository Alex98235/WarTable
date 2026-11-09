namespace WarTable.Api.Models;

using System.Text.Json.Serialization;

/// <summary>A durable outcome and the unit of planning within a campaign.</summary>
public class Objective
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CampaignId { get; set; }

    /// <summary>Campaign-scoped identifier shown as <c>#N</c>. Never reused.</summary>
    public int Alias { get; set; }

    public string Title { get; set; } = "";

    /// <summary>Markdown. Outcome and constraints.</summary>
    public string Description { get; set; } = "";

    /// <summary>Draft objectives are visible but not actionable.</summary>
    public PlanState PlanState { get; set; } = PlanState.Draft;

    /// <summary>Derived cache computed from the child steps.</summary>
    public string Status { get; set; } = "planned";

    /// <summary>Position within the campaign (0-based).</summary>
    public int SortOrder { get; set; }

    /// <summary>Next per-objective step number. Monotonic; never reused.</summary>
    public int NextStepNumber { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    // ── Relationships (loaded by the service, not deserialized from JSON) ──
    [JsonIgnore]
    public Campaign? Campaign { get; set; }

    public List<Step> Steps { get; set; } = new();
}

/// <summary>Whether an objective is parked (Draft) or being worked (Active).</summary>
public enum PlanState
{
    Draft,
    Active,
}
