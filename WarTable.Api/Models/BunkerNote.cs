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

	/// <summary>
	/// Human-only confirmation timestamp. Set only by the UI/HTTP path, never by
	/// an MCP tool, so an agent cannot self-certify its own output. A filled value
	/// records that a human reviewed the note; it is not evidence the content is true.
	/// </summary>
	public DateTime? ApprovedAt { get; set; }

	/// <summary>
	/// Pointer to the artifact a claim rests on: a WarTable reference such as
	/// <c>#12</c> or <c>#12.3</c>, a commit sha, or a URL. Nullable. Because this is
	/// set by the note author (usually an agent), it is a claim, not a guarantee:
	/// an unresolvable basis self-identifies the note as unfounded.
	/// </summary>
	public string? Basis { get; set; }

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

	/// <summary>
	/// Derived on read: true when nothing live points at this note any more (see
	/// <c>NoteDormancy</c>). Never stored — no column, no migration, no soft
	/// delete. Populated by the service only where dormancy is computed; everywhere
	/// else it stays false, which is the truth for pushed surfaces because dormant
	/// notes are filtered out there.
	/// </summary>
	[NotMapped]
	public bool Dormant { get; set; }

	/// <summary>
	/// Days since <see cref="UpdatedAt"/> for a dormant note; 0 elsewhere. Derived
	/// on read, never stored. Populated together with <see cref="Dormant"/>.
	/// </summary>
	[NotMapped]
	public int DormantDays { get; set; }
}

public enum BunkerNoteKind
{
	Directive,
	Decision,
	Gotcha,
	Reference,
	Context,
}

public enum BunkerNoteStatus
{
	Active,
	Superseded,
	Resolved,
}
