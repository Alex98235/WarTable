using WarTable.Api.Models;
using WarTable.Api.Services;

namespace WarTable.Api.Endpoints;

/// <summary>Body for creating an objective. Plan state defaults to Backlog.</summary>
public record CreateObjectiveRequest(
	Guid CampaignId,
	string Title,
	string Description = "",
	PlanState? PlanState = null,
	string? Actor = null);

/// <summary>Body for patching an objective. Omitted members are left unchanged.</summary>
public record UpdateObjectiveRequest(
	string? Title = null,
	string? Description = null,
	PlanState? PlanState = null,
	int? SortOrder = null,
	Guid? RowVersion = null,
	string? Actor = null);

/// <summary>Body for reordering objectives within a campaign.</summary>
public record ObjectiveReorderRequest(
	Guid CampaignId,
	Dictionary<Guid, int> Orders,
	string? Actor = null);

/// <summary>
/// Maps HTTP endpoints for Objective CRUD under /api/objectives.
/// </summary>
public static class ObjectiveEndpoints
{
	private static string Actor(string? actor)
		=> string.IsNullOrWhiteSpace(actor) ? "human" : actor;

	public static void MapObjectiveEndpoints(this WebApplication app)
	{
		var group = app.MapGroup("/api/objectives").WithTags("Objectives");

		// Reorder must be mapped before {id:guid} to avoid route ambiguity.
		group.MapPut("/reorder", async (ObjectiveReorderRequest request, WarTableService service) =>
		{
			var ok = await service.ReorderObjectivesAsync(
				request.CampaignId,
				request.Orders,
				Actor(request.Actor));
			return ok
				? Results.Ok()
				: Results.BadRequest(new { error = "No objectives to reorder" });
		});

		group.MapGet("/", async (Guid? campaignId, bool? includeNotReady, WarTableService service) =>
		{
			var objectives = await service.GetObjectivesAsync(campaignId, includeNotReady ?? true);
			return Results.Ok(objectives);
		});

		group.MapGet("/{id:guid}", async (Guid id, WarTableService service) =>
		{
			var objective = await service.GetObjectiveAsync(id);
			return objective is null ? Results.NotFound() : Results.Ok(objective);
		});

		group.MapPost("/", async (CreateObjectiveRequest request, WarTableService service) =>
		{
			try
			{
				var created = await service.CreateObjectiveAsync(
					request.CampaignId,
					request.Title,
					request.Description,
					request.PlanState ?? PlanState.Backlog,
					Actor(request.Actor));
				return Results.Created($"/api/objectives/{created.Id}", created);
			}
			catch (InvalidOperationException ex)
			{
				return Results.BadRequest(new { error = ex.Message });
			}
		});

		group.MapPut("/{id:guid}", async (Guid id, UpdateObjectiveRequest request, WarTableService service) =>
		{
			try
			{
				var missing = ConcurrencyHelper.RequireRowVersion(request.RowVersion);
				if (missing is not null)
					return missing;

				var patch = new ObjectivePatch(
					request.Title,
					request.Description,
					request.PlanState,
					request.SortOrder);
				var updated = await service.UpdateObjectiveAsync(id, patch, Actor(request.Actor), request.RowVersion);
				return updated is null ? Results.NotFound() : Results.Ok(updated);
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
		});

		group.MapDelete("/{id:guid}", async (Guid id, Guid? rowVersion, string? actor, WarTableService service) =>
		{
			try
			{
				var missing = ConcurrencyHelper.RequireRowVersion(rowVersion);
				if (missing is not null)
					return missing;

				var deleted = await service.DeleteObjectiveAsync(id, Actor(actor), rowVersion);
				return deleted ? Results.NoContent() : Results.NotFound();
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
		});
	}
}
