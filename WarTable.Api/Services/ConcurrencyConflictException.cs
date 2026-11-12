using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace WarTable.Api.Services;

/// <summary>
/// Thrown when an optimistic concurrency check fails — either because the
/// caller's supplied <c>rowVersion</c> does not match the current database row,
/// or because <c>DbUpdateConcurrencyException</c> was thrown at save time.
/// Carries the current entity so the caller can re-read and reapply.
/// </summary>
public class ConcurrencyConflictException : Exception
{
	/// <summary>The current (server-side) entity with its latest row version.</summary>
	public object Current { get; }

	public ConcurrencyConflictException(string message, object current)
		: base(message)
	{
		Current = current;
	}

	/// <summary>
	/// Factory: extracts the database values from a <see cref="DbUpdateConcurrencyException"/>
	/// and wraps them into a <see cref="ConcurrencyConflictException"/>.
	/// </summary>
	public static ConcurrencyConflictException FromDbException(
		Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException dbEx)
	{
		var entry = dbEx.Entries.FirstOrDefault();
		if (entry is null)
			return new ConcurrencyConflictException("Concurrency conflict occurred.", new { });

		// Detach so the entry can be re-queried if needed; grab the database values.
		var current = entry.GetDatabaseValues();
		entry.State = EntityState.Detached;

		var entity = current?.ToObject() ?? entry.Entity;
		return new ConcurrencyConflictException(
			"Write conflict: the row was modified by another client since you read it. " +
			"Re-read the current state and reapply your changes with the new rowVersion.",
			entity!);
	}
}
