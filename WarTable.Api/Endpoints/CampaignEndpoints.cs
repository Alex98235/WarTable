using WarTable.Api.Models;
using WarTable.Api.Services;

namespace WarTable.Api.Endpoints;

/// <summary>
/// Maps HTTP endpoints for Campaign CRUD operations under /api/campaigns.
/// </summary>
public static class CampaignEndpoints
{
    public static void MapCampaignEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/campaigns").WithTags("Campaigns");

        group.MapGet("/", async (string? search, CampaignStatus? status, WarTableService service) =>
        {
            var campaigns = await service.GetCampaignsAsync(search, status);
            return Results.Ok(campaigns);
        });

        group.MapGet("/{id:guid}", async (Guid id, WarTableService service) =>
        {
            var campaign = await service.GetCampaignAsync(id);
            return campaign is null ? Results.NotFound() : Results.Ok(campaign);
        });

        group.MapPost("/", async (Campaign campaign, WarTableService service) =>
        {
            var created = await service.CreateCampaignAsync(campaign);
            return Results.Created($"/api/campaigns/{created.Id}", created);
        });

        group.MapPut("/{id:guid}", async (Guid id, CampaignPatch patch, WarTableService service) =>
        {
            var updated = await service.UpdateCampaignAsync(id, patch);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        group.MapDelete("/{id:guid}", async (Guid id, WarTableService service) =>
        {
            var deleted = await service.DeleteCampaignAsync(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        });
    }
}
