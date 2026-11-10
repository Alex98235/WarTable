namespace WarTable.Api.Models;

using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

/// <summary>
/// Campaign knowledge and campaign directives. The Bunker replaces Intel.
/// Directives are always applied; other kinds are searched on demand.
/// </summary>
public class BunkerNote
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid CampaignId { get; set; }

	/// <summary>Campaign-scoped identifier shown as <c>#N</c>. Shared counter with objectives.</summary>
	public int Alias { get; set; }

	public BunkerNoteKind Kind { get; set; } = BunkerNoteKind.Context;
	public string Title { get; set; } = "";

	/// <summary>Markdown.</summary>
	public string Body { get; set; } = "";

	public BunkerNoteStatus Status { get; set; } = BunkerNoteStatus.Active;
	public bool Pinned { get; set; }
	public string Tags { get; set; } = "";
	public string CreatedBy { get; set; } = "human";
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

	/// <summary>For decisions that replace earlier decisions.</summary>
	public Guid? SupersedesId { get; set; }

	public DateTime? DeletedAt { get; set; }

	[JsonIgnore]
	public Campaign? Campaign { get; set; }

	public List<BunkerNoteLink> Links { get; set; } = new();

	/// <summary>Populated by the service for serialization.</summary>
	[NotMapped]
	public List<Guid> LinkedObjectiveIds { get; set; } = new();

	/// <summary>Populated by the service for serialization.</summary>
	[NotMapped]
	public List<Guid> LinkedStepIds { get; set; } = new();
}

public enum BunkerNoteKind
{
	Directive,
	Decision,
	Gotcha,
	Reference,
	Context,
	Question,
}

public enum BunkerNoteStatus
{
	Active,
	Superseded,
	Resolved,
}
