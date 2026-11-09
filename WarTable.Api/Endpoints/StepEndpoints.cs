using WarTable.Api.Models;
using WarTable.Api.Services;

namespace WarTable.Api.Endpoints;

/// <summary>Body for creating a step. Assigner defaults to <c>orchestrator</c>.</summary>
public record CreateStepRequest(
    Guid ObjectiveId,
    string Title,
    string Context = "",
    string Assignee = "",
    string? Assigner = null,
    List<Guid>? DependsOn = null,
    string? Actor = null);

/// <summary>
/// Body for patching a step. Omitted members are left unchanged. For
/// <c>DependsOn</c>, omitted (null) leaves dependencies unchanged while an
/// empty array clears them.
/// </summary>
public record UpdateStepRequest(
    string? Title = null,
    string? Context = null,
    string? Assignee = null,
    string? Assigner = null,
    string? Status = null,
    List<Guid>? DependsOn = null,
    string? Actor = null);

/// <summary>Body for a quick step status update.</summary>
public record StepStatusRequest(string Status, string? Actor = null);

/// <summary>Body for reordering steps within an objective.</summary>
public record StepReorderRequest(
    Guid ObjectiveId,
    Dictionary<Guid, int> Orders,
    string? Actor = null);

/// <summary>Body for appending an update to a step.</summary>
public record CreateStepUpdateRequest(
    string Body,
    StepUpdateKind Kind = StepUpdateKind.Note,
    string? Actor = null);

/// <summary>Body for attaching evidence to a step.</summary>
public record CreateEvidenceRequest(
    string Label,
    EvidenceKind Kind = EvidenceKind.Link,
    string Value = "",
    string? Actor = null);

/// <summary>
/// Maps HTTP endpoints for Steps under /api/steps. Steps belong to an objective
/// and are ordered by <c>SortOrder</c>.
/// </summary>
public static class StepEndpoints
{
    private static string Actor(string? actor)
        => string.IsNullOrWhiteSpace(actor) ? "human" : actor;

    public static void MapStepEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/steps").WithTags("Steps");

        // Reorder must be mapped before {id:guid} to avoid route ambiguity.
        group.MapPut("/reorder", async (StepReorderRequest request, WarTableService service) =>
        {
            var ok = await service.ReorderStepsAsync(
                request.ObjectiveId,
                request.Orders,
                Actor(request.Actor));
            return ok
                ? Results.Ok()
                : Results.BadRequest(new { error = "No steps to reorder" });
        });

        group.MapGet("/", async (Guid objectiveId, WarTableService service) =>
        {
            var steps = await service.GetStepsAsync(objectiveId);
            return Results.Ok(steps);
        });

        group.MapGet("/{id:guid}", async (Guid id, WarTableService service) =>
        {
            var step = await service.GetStepAsync(id);
            return step is null ? Results.NotFound() : Results.Ok(step);
        });

        group.MapPost("/", async (CreateStepRequest request, WarTableService service) =>
        {
            try
            {
                var created = await service.CreateStepAsync(
                    request.ObjectiveId,
                    request.Title,
                    request.Context,
                    request.Assignee,
                    string.IsNullOrWhiteSpace(request.Assigner) ? "orchestrator" : request.Assigner,
                    request.DependsOn ?? [],
                    Actor(request.Actor));
                return Results.Created($"/api/steps/{created.Id}", created);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdateStepRequest request, WarTableService service) =>
        {
            try
            {
                var patch = new StepPatch(
                    request.Title,
                    request.Context,
                    request.Assignee,
                    request.Assigner,
                    request.Status);
                var updated = await service.UpdateStepAsync(
                    id,
                    patch,
                    request.DependsOn,
                    Actor(request.Actor));
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPatch("/{id:guid}/status", async (Guid id, StepStatusRequest request, WarTableService service) =>
        {
            try
            {
                var updated = await service.UpdateStepStatusAsync(
                    id,
                    request.Status,
                    Actor(request.Actor));
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
                var deleted = await service.DeleteStepAsync(id, Actor(actor));
                return deleted ? Results.NoContent() : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // ── Updates ──

        group.MapGet("/{id:guid}/updates", async (Guid id, WarTableService service) =>
        {
            var updates = await service.GetStepUpdatesAsync(id);
            return Results.Ok(updates);
        });

        group.MapPost("/{id:guid}/updates", async (Guid id, CreateStepUpdateRequest request, WarTableService service) =>
        {
            try
            {
                var update = await service.AddStepUpdateAsync(
                    id,
                    request.Body,
                    request.Kind,
                    Actor(request.Actor));
                return update is null
                    ? Results.NotFound()
                    : Results.Created($"/api/steps/{id}/updates/{update.Id}", update);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // ── Evidence ──

        group.MapGet("/{id:guid}/evidence", async (Guid id, WarTableService service) =>
        {
            var evidence = await service.GetEvidenceAsync(id);
            return Results.Ok(evidence);
        });

        group.MapPost("/{id:guid}/evidence", async (Guid id, CreateEvidenceRequest request, WarTableService service) =>
        {
            try
            {
                var evidence = await service.AddEvidenceAsync(
                    id,
                    request.Label,
                    request.Kind,
                    request.Value,
                    Actor(request.Actor));
                return evidence is null
                    ? Results.NotFound()
                    : Results.Created($"/api/steps/{id}/evidence/{evidence.Id}", evidence);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }
}
