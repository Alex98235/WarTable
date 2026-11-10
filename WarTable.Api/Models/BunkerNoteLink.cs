namespace WarTable.Api.Models;

using System.Text.Json.Serialization;

/// <summary>Optional many-to-many link from a Bunker note to an objective and/or step.</summary>
public class BunkerNoteLink
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid NoteId { get; set; }
	public Guid? ObjectiveId { get; set; }
	public Guid? StepId { get; set; }

	[JsonIgnore]
	public BunkerNote? Note { get; set; }
}
