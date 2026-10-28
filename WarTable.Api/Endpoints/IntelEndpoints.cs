using WarTable.Api.Models;
using WarTable.Api.Services;

namespace WarTable.Api.Endpoints;

/// <summary>
/// Maps HTTP endpoints for Intel CRUD under /api/intel.
/// Intel always belongs to a campaign — campaigns are the namespace for #N aliases.
/// </summary>
public static class IntelEndpoints
{
    public static void MapIntelEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/intel").WithTags("Intel");

        group.MapGet("/", async (string? search, IntelCategory? category, Guid? campaignId, WarTableService service) =>
        {
            var intel = await service.GetIntelAsync(search, category, campaignId);
            return Results.Ok(intel);
        });

        group.MapGet("/{id:guid}", async (Guid id, WarTableService service) =>
        {
            var item = await service.GetIntelAsync(id);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        group.MapPost("/", async (Intel intel, WarTableService service) =>
        {
            if (intel.CampaignId == Guid.Empty)
                return Results.BadRequest(new { error = "campaignId is required" });

            try
            {
                var created = await service.CreateIntelAsync(intel);
                return Results.Created($"/api/intel/{created.Id}", created);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPut("/{id:guid}", async (Guid id, IntelPatch patch, WarTableService service) =>
        {
            var updated = await service.UpdateIntelAsync(id, patch);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        group.MapDelete("/{id:guid}", async (Guid id, WarTableService service) =>
        {
            var deleted = await service.DeleteIntelAsync(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        });
    }
}
