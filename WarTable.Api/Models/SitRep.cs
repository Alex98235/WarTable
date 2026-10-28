namespace WarTable.Api.Models;

/// <summary>
/// A session report: an append-only journal entry describing what happened during
/// a work session. SitReps never mutate objectives — the objectives table is the
/// single source of truth for current state.
/// </summary>
public class SitRep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CampaignId { get; set; }

    /// <summary>Short, campaign-scoped identifier shown as <c>#N</c>. Never reused.</summary>
    public int Alias { get; set; }

    /// <summary>"human" or "agent:&lt;name&gt;". Attribution matters when sessions overlap.</summary>
    public string Source { get; set; } = "human";

    public string Summary { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Campaign? Campaign { get; set; }
    public List<SitRepObjective> Objectives { get; set; } = new();
}
