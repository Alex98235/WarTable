namespace WarTable.Api.Models;

using System.Text.Json.Serialization;

/// <summary>Append-only update attached to a step. Replaces SitReps.</summary>
public class StepUpdate
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid StepId { get; set; }

	/// <summary>Actor in the provenance convention: <c>human</c> or <c>agent:&lt;name&gt;</c>.</summary>
	public string Actor { get; set; } = "human";

	public StepUpdateKind Kind { get; set; } = StepUpdateKind.Note;

	/// <summary>Markdown.</summary>
	public string Body { get; set; } = "";

	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

	[JsonIgnore]
	public Step? Step { get; set; }
}

public enum StepUpdateKind
{
	Note,
	Blocker,
	Result,
	Handoff,
}
