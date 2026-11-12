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

	/// <summary>Seed and draft objectives are visible but not actionable.</summary>
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

	/// <summary>
	/// Explicit note-link counts populated for plan overviews. Null elsewhere, so
	/// existing objective payloads keep their shape.
	/// </summary>
	[NotMapped]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public KnowledgeCounts? Knowledge { get; set; }
}

/// <summary>
/// Plan state: a seed is a captured unfinished thought, a draft is a shaped park,
/// and active objectives are being worked.
/// </summary>
public enum PlanState
{
	Seed,
	Draft,
	Active,
}
