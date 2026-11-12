using WarTable.Api.Models;
using WarTable.Api.Services;

namespace WarTable.Api.Endpoints;

/// <summary>Body for creating a Bunker note.</summary>
public record CreateNoteRequest(
	Guid CampaignId,
	BunkerNoteKind Kind = BunkerNoteKind.Context,
	string Title = "",
	string Body = "",
	string Tags = "",
	bool Pinned = false,
	List<Guid>? ObjectiveIds = null,
	List<Guid>? StepIds = null,
	Guid? SupersedesId = null,
	string? Actor = null);

/// <summary>Body for patching a Bunker note. Omitted members are left unchanged.</summary>
public record UpdateNoteRequest(
	string? Title = null,
	string? Body = null,
	bool? Pinned = null,
	string? Tags = null,
	Guid? SupersedesId = null,
	string? Basis = null,
	DateTime? ApprovedAt = null,
	string? Actor = null);

/// <summary>
/// Maps HTTP endpoints for Bunker notes under /api/notes.
/// </summary>
public static class NoteEndpoints
{
	private static string Actor(string? actor)
		=> string.IsNullOrWhiteSpace(actor) ? "human" : actor;

	public static void MapNoteEndpoints(this WebApplication app)
	{
		var group = app.MapGroup("/api/notes").WithTags("Notes");

		// Alias lookup must be mapped before {id:guid}.
		group.MapGet("/by-alias", async (Guid campaignId, int alias, WarTableService service) =>
		{
			if (campaignId == Guid.Empty)
				return Results.BadRequest(new { error = "campaignId is required" });

			var note = await service.GetNoteByAliasAsync(campaignId, alias);
			return note is null ? Results.NotFound() : Results.Ok(note);
		});

		group.MapGet("/", async (
			Guid? campaignId,
			string? search,
			BunkerNoteKind? kind,
			BunkerNoteStatus? status,
			bool? includeDeleted,
			bool? includeDormant,
			WarTableService service) =>
		{
			var notes = await service.GetNotesAsync(
				campaignId,
				search,
				kind,
				status,
				includeDeleted ?? false,
				includeDormant ?? false);
			return Results.Ok(notes);
		});

		group.MapGet("/{id:guid}", async (Guid id, WarTableService service) =>
		{
			var note = await service.GetNoteAsync(id);
			return note is null ? Results.NotFound() : Results.Ok(note);
		});

		group.MapPost("/", async (CreateNoteRequest request, WarTableService service) =>
		{
			try
			{
				var created = await service.CreateNoteAsync(
					request.CampaignId,
					request.Kind,
					request.Title,
					request.Body,
					request.Tags,
					request.Pinned,
					Actor(request.Actor),
					request.ObjectiveIds ?? [],
					request.StepIds ?? [],
					request.SupersedesId);
				return Results.Created($"/api/notes/{created.Id}", created);
			}
			catch (InvalidOperationException ex)
			{
				return Results.BadRequest(new { error = ex.Message });
			}
		});

		group.MapPut("/{id:guid}", async (Guid id, UpdateNoteRequest request, WarTableService service) =>
		{
			try
			{
				var patch = new BunkerNotePatch(
					Title: request.Title,
					Body: request.Body,
					Pinned: request.Pinned,
					Tags: request.Tags,
					SupersedesId: request.SupersedesId,
					Basis: request.Basis,
					ApprovedAt: request.ApprovedAt);
				var updated = await service.UpdateNoteAsync(id, patch, Actor(request.Actor));
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
				var deleted = await service.DeleteNoteAsync(id, Actor(actor));
				return deleted ? Results.NoContent() : Results.NotFound();
			}
			catch (InvalidOperationException ex)
			{
				return Results.BadRequest(new { error = ex.Message });
			}
		});
	}
}
