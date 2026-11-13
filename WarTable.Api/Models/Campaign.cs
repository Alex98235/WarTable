namespace WarTable.Api.Models;

using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

/// <summary>
/// A namespace and folder for a body of work. A campaign is not a work item and
/// has no status; its objectives carry the state that matters.
/// </summary>
public class Campaign : IConcurrencyGuarded
{
	public Guid Id { get; set; } = Guid.NewGuid();

	/// <summary>Optimistic concurrency token. Exposed as <c>rowVersion</c> in JSON.</summary>
	[JsonPropertyName("rowVersion")]
	public Guid RowVersion { get; set; } = Guid.NewGuid();

	public string Title { get; set; } = "";

	/// <summary>
	/// Legacy storage from before the Charter note existed. Kept mapped so old
	/// SQLite files remain writable; never exposed or read by the application.
	/// </summary>
	[JsonIgnore]
	public string Intent { get; set; } = "";

	/// <summary>Campaign charter in markdown. Promoted from a Bunker note.</summary>

	public string? Charter { get; set; }

	public string Tags { get; set; } = "";
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

	/// <summary>Populated by the service for list views; not persisted on Campaign.</summary>
	[NotMapped]
	public string? CharterExcerpt { get; set; }

	/// <summary>
	/// Nullable archive marker. Archiving hides the campaign from the default
	/// list and keeps everything, including <see cref="NextAliasNumber"/>, so a
	/// restore leaves every #N reference in the campaign resolvable again.
	/// </summary>
	public DateTime? ArchivedAt { get; set; }

	/// <summary>
	/// Next alias number to hand out to a campaign-scoped object (objective or
	/// Bunker note). Monotonic: never decremented, so aliases are never reused.
	/// </summary>
	public int NextAliasNumber { get; set; } = 1;

	public List<Objective> Objectives { get; set; } = new();
	public List<BunkerNote> BunkerNotes { get; set; } = new();
}
