using WarTable.Api.Services;

namespace WarTable.Api.Endpoints;

/// <summary>Body for capturing an operator preference. Source defaults to <c>human</c>.</summary>
public record CreatePreferenceRequest(
    string Text,
    string Rationale = "",
    string Tags = "",
    string? Source = null);

/// <summary>Body for patching a preference. Omitted members are left unchanged.</summary>
public record UpdatePreferenceRequest(
    string? Text = null,
    string? Rationale = null,
    bool? Active = null,
    string? Tags = null);

/// <summary>
/// Maps HTTP endpoints for operator preferences under /api/preferences.
/// </summary>
public static class PreferenceEndpoints
{
    public static void MapPreferenceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/preferences").WithTags("Preferences");

        group.MapGet("/", async (bool? includeInactive, WarTableService service) =>
        {
            var preferences = await service.GetPreferencesAsync(includeInactive ?? false);
            return Results.Ok(preferences);
        });

        group.MapPost("/", async (CreatePreferenceRequest request, WarTableService service) =>
        {
            try
            {
                var created = await service.CreatePreferenceAsync(
                    request.Text,
                    request.Rationale,
                    request.Tags,
                    string.IsNullOrWhiteSpace(request.Source) ? "human" : request.Source);
                return Results.Created($"/api/preferences/{created.Id}", created);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdatePreferenceRequest request, WarTableService service) =>
        {
            try
            {
                var patch = new PreferencePatch(
                    request.Text,
                    request.Rationale,
                    request.Active,
                    request.Tags);
                var updated = await service.UpdatePreferenceAsync(id, patch);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }
}
