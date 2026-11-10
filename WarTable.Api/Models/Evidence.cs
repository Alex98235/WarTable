namespace WarTable.Api.Models;

using System.Text.Json.Serialization;

/// <summary>Optional proof attached to a step, making "done" inspectable.</summary>
public class Evidence
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid StepId { get; set; }

	public string Label { get; set; } = "";
	public EvidenceKind Kind { get; set; } = EvidenceKind.Link;

	/// <summary>URL, path, hash, or result text.</summary>
	public string Value { get; set; } = "";

	public string CreatedBy { get; set; } = "human";
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

	[JsonIgnore]
	public Step? Step { get; set; }
}

public enum EvidenceKind
{
	Link,
	File,
	Commit,
	Test,
	Screenshot,
	Other,
}
