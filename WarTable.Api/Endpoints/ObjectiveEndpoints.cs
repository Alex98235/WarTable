using WarTable.Api.Models;
using WarTable.Api.Services;

namespace WarTable.Api.Endpoints;

/// <summary>
/// Request body for reordering objectives.
/// </summary>
public record ObjectiveReorderRequest(Guid CampaignId, Dictionary<Guid, int> Orders);

/// <summary>
/// Maps HTTP endpoints for Objective CRUD under /api/objectives.
/// </summary>
public static class ObjectiveEndpoints
{
    public static void MapObjectiveEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/objectives").WithTags("Objectives");

        // Reorder must be mapped before {id:guid} to avoid route ambiguity,
        // though the :guid constraint on the other route prevents actual conflicts.
        group.MapPut("/reorder", async (ObjectiveReorderRequest request, WarTableService service) =>
        {
            var ok = await service.ReorderObjectivesAsync(request.CampaignId, request.Orders);
            return ok ? Results.Ok() : Results.BadRequest("No objectives to reorder");
        });

        group.MapGet("/", async (Guid? campaignId, string? lane, WarTableService service) =>
        {
            try
            {
                var objectives = await service.GetObjectivesAsync(campaignId, lane);
                return Results.Ok(objectives);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapGet("/{id:guid}", async (Guid id, WarTableService service) =>
        {
            var objective = await service.GetObjectiveAsync(id);
            return objective is null ? Results.NotFound() : Results.Ok(objective);
        });

        group.MapPost("/", async (Objective objective, WarTableService service) =>
        {
            try
            {
                var created = await service.CreateObjectiveAsync(objective);
                return Results.Created($"/api/objectives/{created.Id}", created);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPut("/{id:guid}", async (Guid id, ObjectivePatch patch, WarTableService service) =>
        {
            try
            {
                var updated = await service.UpdateObjectiveAsync(id, patch);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapDelete("/{id:guid}", async (Guid id, WarTableService service) =>
        {
            var deleted = await service.DeleteObjectiveAsync(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        });
    }
}
