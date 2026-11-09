namespace WarTable.Api.Models;

/// <summary>
/// A namespace and folder for a body of work. A campaign is not a work item and
/// has no status; its objectives carry the state that matters.
/// </summary>
public class Campaign
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";

    /// <summary>Markdown. Why this project exists. Replaces the old Description.</summary>
    public string Intent { get; set; } = "";

    public string Tags { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Nullable archive marker. Archive semantics are deferred.</summary>
    public DateTime? ArchivedAt { get; set; }

    /// <summary>
    /// Next alias number to hand out to a campaign-scoped object (objective or
    /// Bunker note). Monotonic: never decremented, so aliases are never reused.
    /// </summary>
    public int NextAliasNumber { get; set; } = 1;

    public List<Objective> Objectives { get; set; } = new();
    public List<BunkerNote> BunkerNotes { get; set; } = new();
}
