namespace WarTable.Api.Models;

/// <summary>
/// Marker interface for entities that participate in optimistic concurrency
/// checks via a <see cref="RowVersion"/> GUID.
/// </summary>
public interface IConcurrencyGuarded
{
	/// <summary>
	/// Optimistic concurrency token. Regenerated on every successful write.
	/// Exposed to clients as <c>rowVersion</c>.
	/// </summary>
	Guid RowVersion { get; set; }
}
