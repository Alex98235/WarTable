using WarTable.Api.Services;

namespace WarTable.Api.Endpoints;

/// <summary>
/// Resolves a campaign-scoped <c>#N</c> alias to the object it identifies.
/// Soft-deleted objectives and intel still resolve so old references stay meaningful.
/// </summary>
public static class AliasEndpoints
{
    public static void MapAliasEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/objects").WithTags("Objects");

        group.MapGet("/resolve", async (Guid campaignId, int alias, WarTableService service) =>
        {
            if (campaignId == Guid.Empty)
                return Results.BadRequest(new { error = "campaignId is required" });

            var resolved = await service.ResolveAliasAsync(campaignId, alias);
            return resolved is null
                ? Results.NotFound(new { error = $"No object #{alias} in this campaign" })
                : Results.Ok(resolved);
        });
    }
}
