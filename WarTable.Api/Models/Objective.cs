namespace WarTable.Api.Models;

using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

/// <summary>A durable outcome and the unit of planning within a campaign.</summary>
public class Objective : IConcurrencyGuarded
{
	public Guid Id { get; set; } = Guid.NewGuid();

	/// <summary>Optimistic concurrency token. Exposed as <c>rowVersion</c> in JSON.</summary>
	[JsonPropertyName("rowVersion")]
	public Guid RowVersion { get; set; } = Guid.NewGuid();

	public Guid CampaignId { get; set; }

	/// <summary>Campaign-scoped identifier shown as <c>#N</c>. Never reused.</summary>
	public int Alias { get; set; }

	public string Title { get; set; } = "";

	/// <summary>Markdown. Outcome and constraints.</summary>
	public string Description { get; set; } = "";

	/// <summary>Seed and backlog objectives are visible but not actionable.</summary>
	public PlanState PlanState { get; set; } = PlanState.Backlog;

	/// <summary>Derived cache computed from the child steps.</summary>
	public string Status { get; set; } = "not-started";

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

	/// <summary>
	/// Explicit note-link counts populated for plan overviews. Null elsewhere, so
	/// existing objective payloads keep their shape.
	/// </summary>
	[NotMapped]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public KnowledgeCounts? Knowledge { get; set; }
}

/// <summary>
/// Plan state names permission, not shape. A seed is a captured unfinished
/// thought and is never worked; a backlog objective is a shaped plan that is
/// deliberately not released; a ready objective's steps may be taken.
/// </summary>
public enum PlanState
{
	Seed,
	Backlog,
	Ready,
}
