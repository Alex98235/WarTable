namespace WarTable.Api.Models;

public class Campaign
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public CampaignStatus Status { get; set; } = CampaignStatus.Planning;
    public string Tags { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Next alias number to hand out to a campaign-scoped object (objective, intel, sitrep).
    /// Monotonic: never decremented, so aliases are never reused.
    /// </summary>
    public int NextAliasNumber { get; set; } = 1;

    public List<Objective> Objectives { get; set; } = new();
    public List<Intel> IntelEntries { get; set; } = new();
    public List<SitRep> SitReps { get; set; } = new();
}

public enum CampaignStatus { Planning, Active, Completed, Abandoned }
