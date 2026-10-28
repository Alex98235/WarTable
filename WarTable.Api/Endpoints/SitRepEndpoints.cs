using WarTable.Api.Models;
using WarTable.Api.Services;

namespace WarTable.Api.Endpoints;

/// <summary>
/// Request body for appending a sitrep. Objectives are referenced by their campaign-scoped
/// alias numbers (<c>#N</c>), never by title or count.
/// </summary>
public record SitRepRequest(
    Guid CampaignId,
    string Summary,
    string? Source,
    int[]? Completed,
    int[]? Blocked,
    int[]? Next);

/// <summary>
/// Maps HTTP endpoints for SitRep CRUD under /api/sitrep.
/// </summary>
public static class SitRepEndpoints
{
    public static void MapSitRepEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/sitrep").WithTags("SitReps");

        group.MapGet("/", async (Guid campaignId, WarTableService service) =>
        {
            var sitreps = await service.GetSitRepsAsync(campaignId);
            return Results.Ok(sitreps);
        });

        group.MapPost("/", async (SitRepRequest request, WarTableService service) =>
        {
            if (request.CampaignId == Guid.Empty)
                return Results.BadRequest(new { error = "campaignId is required" });
            if (string.IsNullOrWhiteSpace(request.Summary))
                return Results.BadRequest(new { error = "summary is required" });

            try
            {
                var created = await service.CreateSitRepAsync(
                    request.CampaignId,
                    request.Summary,
                    request.Source,
                    request.Completed ?? Array.Empty<int>(),
                    request.Blocked ?? Array.Empty<int>(),
                    request.Next ?? Array.Empty<int>());

                return created is null
                    ? Results.NotFound(new { error = "campaign not found" })
                    : Results.Created($"/api/sitrep/{created.Id}", created);
            }
            catch (UnknownAliasException ex)
            {
                return Results.BadRequest(new
                {
                    error = "Unknown objective aliases",
                    aliases = ex.Aliases,
                });
            }
        });
    }
}
