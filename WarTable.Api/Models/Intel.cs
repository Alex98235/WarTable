namespace WarTable.Api.Models;

/// <summary>
/// Durable knowledge captured during work: a decision, context, gotcha or reference.
/// Always belongs to a campaign — campaigns are the namespace boundary for aliases.
/// </summary>
public class Intel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CampaignId { get; set; }

    /// <summary>Short, campaign-scoped identifier shown as <c>#N</c>. Never reused.</summary>
    public int Alias { get; set; }

    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public IntelCategory Category { get; set; } = IntelCategory.Context;
    public string Tags { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Soft-delete marker. Kept so old <c>#N</c> references stay resolvable.</summary>
    public DateTime? DeletedAt { get; set; }

    public Campaign? Campaign { get; set; }
}

public enum IntelCategory { Decision, Context, Gotcha, Reference }
