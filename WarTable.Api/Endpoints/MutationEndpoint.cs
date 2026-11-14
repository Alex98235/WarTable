namespace WarTable.Api.Endpoints;

/// <summary>
/// One shape for every single-row mutation route: archive, restore and purge all
/// answer 204 on success, 404 when the row is absent or already in the requested
/// state, 400 for a domain refusal, and the shared concurrency envelope for a
/// stale <c>rowVersion</c>. Keeping it here means a new lifecycle verb is three
/// lines at the call site instead of a copied try/catch.
/// </summary>
internal static class MutationEndpoint
{
	public static async Task<IResult> Run(Guid? rowVersion, Func<Task<bool>> action)
	{
		var missing = ConcurrencyHelper.RequireRowVersion(rowVersion);
		if (missing is not null)
			return missing;

		try
		{
			return await action() ? Results.NoContent() : Results.NotFound();
		}
		catch (Exception ex)
		{
			var conflict = ConcurrencyHelper.HandleConflict(ex);
			if (conflict is not null)
				return conflict;
			if (ex is InvalidOperationException)
				return Results.BadRequest(new { error = ex.Message });
			throw;
		}
	}
}
