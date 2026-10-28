namespace WarTable.Api.Models;

public class Objective
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CampaignId { get; set; }

    /// <summary>Short, campaign-scoped identifier shown as <c>#N</c>. Never reused.</summary>
    public int Alias { get; set; }

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>
    /// Kanban column. One of <see cref="ObjectiveLanes"/>. This is the single source of
    /// truth for where an objective sits; there is deliberately no separate status field.
    /// </summary>
    public string Lane { get; set; } = "backlog";

    public int Order { get; set; } = 0;
    public string Dependencies { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Soft-delete marker. Kept so old <c>#N</c> references stay resolvable.</summary>
    public DateTime? DeletedAt { get; set; }

    public Campaign? Campaign { get; set; }

    /// <summary>The ordered kanban lanes. Position in this array is display order.</summary>
    public static readonly string[] ObjectiveLanes =
        ["backlog", "ready", "in-progress", "review", "done"];
}
