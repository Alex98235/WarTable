using System.ComponentModel;
using ModelContextProtocol.Server;
using WarTable.Api.Models;
using WarTable.Api.Services;

namespace WarTable.Api.Mcp.Tools;

/// <summary>Bunker note tools.</summary>
[McpServerToolType]
public static class BunkerTools
{
	[McpServerTool(Name = "add_note")]
	[Description(
		"Add a Bunker note (directive, decision, gotcha, reference, context or question) " +
		"to a campaign, optionally linking objectives and steps.")]
	public static async Task<object> AddNoteAsync(
		WarTableService service,
		string campaign_id,
		string title,
		string? kind = null,
		string? body = null,
		string? tags = null,
		bool? pinned = null,
		NoteLinksInput? links = null,
		string? supersedes_id = null,
		string? actor = null)
	{
		try
		{
			var campaignId = McpArgs.Guid(campaign_id);
			if (campaignId is null)
				return McpResults.Error("campaign_id is required");
			if (string.IsNullOrWhiteSpace(title))
				return McpResults.Error("title is required");

			var noteKind = BunkerNoteKind.Context;
			if (!string.IsNullOrWhiteSpace(kind) &&
				!Enum.TryParse(kind, ignoreCase: true, out noteKind))
			{
				return McpResults.Error(
					$"Invalid kind: {kind}. Valid values: {string.Join(", ", Enum.GetNames<BunkerNoteKind>())}");
			}

			IEnumerable<Guid> linkedObjectiveIds =
				McpArgs.Guids(links?.ObjectiveIds) ?? Array.Empty<Guid>();
			IEnumerable<Guid> linkedStepIds =
				McpArgs.Guids(links?.StepIds) ?? Array.Empty<Guid>();

			var note = await service.CreateNoteAsync(
				campaignId.Value,
				noteKind,
				title,
				body ?? "",
				tags ?? "",
				pinned ?? false,
				McpArgs.Actor(actor),
				linkedObjectiveIds,
				linkedStepIds,
				McpArgs.Guid(supersedes_id));

			return note;
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "get_note")]
	[Description("Get a Bunker note by its campaign-scoped alias (#N). Soft-deleted notes still resolve.")]
	public static async Task<object> GetNoteAsync(
		WarTableService service,
		string campaign_id,
		int alias)
	{
		try
		{
			var campaignId = McpArgs.Guid(campaign_id);
			if (campaignId is null)
				return McpResults.Error("campaign_id is required");

			var note = await service.GetNoteByAliasAsync(campaignId.Value, alias);
			return note is null
				? McpResults.Error($"No note #{alias} in campaign {campaignId}")
				: note;
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "search_notes")]
	[Description("Search Bunker notes by text, kind, status and/or campaign.")]
	public static async Task<object> SearchNotesAsync(
		WarTableService service,
		string? campaign_id = null,
		string? search = null,
		string? kind = null,
		string? status = null)
	{
		try
		{
			BunkerNoteKind? noteKind = null;
			if (!string.IsNullOrWhiteSpace(kind))
			{
				if (!Enum.TryParse<BunkerNoteKind>(kind, ignoreCase: true, out var parsed))
					return McpResults.Error(
						$"Invalid kind: {kind}. Valid values: {string.Join(", ", Enum.GetNames<BunkerNoteKind>())}");
				noteKind = parsed;
			}

			BunkerNoteStatus? noteStatus = null;
			if (!string.IsNullOrWhiteSpace(status))
			{
				if (!Enum.TryParse<BunkerNoteStatus>(status, ignoreCase: true, out var parsed))
					return McpResults.Error(
						$"Invalid status: {status}. Valid values: {string.Join(", ", Enum.GetNames<BunkerNoteStatus>())}");
				noteStatus = parsed;
			}

			return await service.GetNotesAsync(
				McpArgs.Guid(campaign_id),
				search,
				noteKind,
				noteStatus);
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "update_note")]
	[Description("Update Bunker note fields. Only the fields you include are changed.")]
	public static async Task<object> UpdateNoteAsync(
		WarTableService service,
		string note_id,
		string? title = null,
		string? body = null,
		string? status = null,
		bool? pinned = null,
		string? tags = null,
		string? supersedes_id = null,
		string? actor = null)
	{
		try
		{
			var id = McpArgs.Guid(note_id);
			if (id is null)
				return McpResults.Error("note_id is required");

			BunkerNoteStatus? noteStatus = null;
			if (!string.IsNullOrWhiteSpace(status))
			{
				if (!Enum.TryParse<BunkerNoteStatus>(status, ignoreCase: true, out var parsed))
					return McpResults.Error(
						$"Invalid status: {status}. Valid values: {string.Join(", ", Enum.GetNames<BunkerNoteStatus>())}");
				noteStatus = parsed;
			}

			var patch = new BunkerNotePatch(
				Title: title,
				Body: body,
				Status: noteStatus,
				Pinned: pinned,
				Tags: tags,
				SupersedesId: McpArgs.Guid(supersedes_id));

			var updated = await service.UpdateNoteAsync(id.Value, patch, McpArgs.Actor(actor));
			return updated is null
				? McpResults.Error($"Note not found: {id}")
				: updated;
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}
}

/// <summary>Optional links from a Bunker note to campaign objects.</summary>
public sealed class NoteLinksInput
{
	public List<string>? ObjectiveIds { get; set; }
	public List<string>? StepIds { get; set; }
}
