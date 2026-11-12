namespace WarTable.Api.Models;

using System.Text.Json.Serialization;

/// <summary>Optional proof attached to a step, making "done" inspectable.</summary>
public class Evidence
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid StepId { get; set; }

	public string Label { get; set; } = "";
	public EvidenceKind Kind { get; set; } = EvidenceKind.Link;

	/// <summary>
	/// URL or commit sha. A kind survives only if a reader can dereference the
	/// value. WarTable stores no files — do not point at a path; if the proof is
	/// already in the repository, the commit is the citation.
	/// </summary>
	public string Value { get; set; } = "";

	public string CreatedBy { get; set; } = "human";
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

	[JsonIgnore]
	public Step? Step { get; set; }
}

/// <summary>
/// Kind of proof. A kind survives only if a reader can dereference the value.
/// File, Test, Screenshot, and Other were removed because they either name a
/// medium WarTable does not store or cannot be followed at all. If the proof
/// is already in the repository, the commit is the citation — do not cite a path.
/// </summary>
public enum EvidenceKind
{
	Link,
	Commit,
}
