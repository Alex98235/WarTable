using WarTable.Api.Services;

namespace WarTable.Api.Endpoints;

/// <summary>
/// Shared helpers for HTTP endpoints that participate in optimistic concurrency.
/// Handles the <c>428 Precondition Required</c> (missing rowVersion) and
/// <c>409 Conflict</c> (stale rowVersion) responses in a single place.
/// </summary>
internal static class ConcurrencyHelper
{
	/// <summary>
	/// Returns <c>428 Precondition Required</c> when <paramref name="rowVersion"/>
	/// is null, signaling the caller to read the row first and resend with its
	/// <c>rowVersion</c>. When it is supplied, returns null (no error).
	/// </summary>
	public static IResult? RequireRowVersion(Guid? rowVersion)
	{
		if (!rowVersion.HasValue)
		{
			return Results.Json(
				new
				{
					error = "precondition_required",
					message = "This write requires a rowVersion. Read the current row first, " +
							  "then resend your changes with its rowVersion.",
				},
				statusCode: 428);
		}

		return null;
	}

	/// <summary>
	/// Catches <see cref="ConcurrencyConflictException"/> and translates it to
	/// a <c>409 Conflict</c> response carrying the current server-side entity.
	/// Returns null when the exception is not a concurrency conflict, so the
	/// caller can re-throw.
	/// </summary>
	public static IResult? HandleConflict(Exception ex)
	{
		if (ex is ConcurrencyConflictException conflict)
		{
			return Results.Json(
				new
				{
					error = "conflict",
					message = conflict.Message,
					current = conflict.Current,
				},
				statusCode: 409);
		}

		return null;
	}
}
