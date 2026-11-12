namespace WarTable.Api.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Operator-scoped soft rule. Global to the single operator and always injected.
/// Retire rather than delete.
/// </summary>
public class Preference : IConcurrencyGuarded
{
	public Guid Id { get; set; } = Guid.NewGuid();

	/// <summary>Optimistic concurrency token. Exposed as <c>rowVersion</c> in JSON.</summary>
	[JsonPropertyName("rowVersion")]
	public Guid RowVersion { get; set; } = Guid.NewGuid();

	/// <summary>The preference itself, e.g. "Dislikes green."</summary>
	public string Text { get; set; } = "";

	/// <summary>Why, or where it came from.</summary>
	public string Rationale { get; set; } = "";

	/// <summary><c>human</c> or <c>agent</c>.</summary>
	public string Source { get; set; } = "human";

	public bool Active { get; set; } = true;
	public string Tags { get; set; } = "";
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
