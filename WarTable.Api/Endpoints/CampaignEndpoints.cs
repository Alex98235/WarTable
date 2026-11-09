using WarTable.Api.Services;

namespace WarTable.Api.Endpoints;

/// <summary>Body for creating a campaign.</summary>
public record CreateCampaignRequest(
    string Title,
    string Intent = "",
    string Tags = "",
    string? Actor = null);

/// <summary>Body for patching a campaign. Omitted members are left unchanged.</summary>
public record UpdateCampaignRequest(
    string? Title = null,
    string? Intent = null,
    string? Tags = null,
    DateTime? ArchivedAt = null,
    string? Actor = null);

/// <summary>
/// Maps HTTP endpoints for Campaign CRUD operations under /api/campaigns.
/// </summary>
public static class CampaignEndpoints
{
    private static string Actor(string? actor)
        => string.IsNullOrWhiteSpace(actor) ? "human" : actor;

    public static void MapCampaignEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/campaigns").WithTags("Campaigns");

        group.MapGet("/", async (string? search, bool? includeArchived, WarTableService service) =>
        {
            var campaigns = await service.GetCampaignsAsync(search, includeArchived ?? false);
            return Results.Ok(campaigns);
        });

        group.MapGet("/{id:guid}", async (Guid id, WarTableService service) =>
        {
            var campaign = await service.GetCampaignAsync(id);
            return campaign is null ? Results.NotFound() : Results.Ok(campaign);
        });

        group.MapPost("/", async (CreateCampaignRequest request, WarTableService service) =>
        {
            try
            {
                var created = await service.CreateCampaignAsync(
                    request.Title,
                    request.Intent,
                    request.Tags,
                    Actor(request.Actor));
                return Results.Created($"/api/campaigns/{created.Id}", created);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdateCampaignRequest request, WarTableService service) =>
        {
            try
            {
                var patch = new CampaignPatch(
                    request.Title,
                    request.Intent,
                    request.Tags,
                    request.ArchivedAt);
                var updated = await service.UpdateCampaignAsync(id, patch, Actor(request.Actor));
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapDelete("/{id:guid}", async (Guid id, string? actor, WarTableService service) =>
        {
            try
            {
                var deleted = await service.DeleteCampaignAsync(id, Actor(actor));
                return deleted ? Results.NoContent() : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }
}
