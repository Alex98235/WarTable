using System.ComponentModel;
using ModelContextProtocol.Server;
using WarTable.Api.Models;
using WarTable.Api.Services;
using WarTable.Api.Services.Embeddings;

namespace WarTable.Api.Mcp.Tools;

/// <summary>Bunker note tools.</summary>
[McpServerToolType]
public static class BunkerTools
{
	[McpServerTool(Name = "add_note")]
	[Description(
		"Add a Bunker note (directive, decision, gotcha, reference, or context) " +
		"to a campaign, optionally linking objectives and steps.")]
	public static async Task<object> AddNoteAsync(
		WarTableService service,
		string campaign_id,
		string kind,
		string title,
		string? body = null,
		string? tags = null,
		bool? pinned = null,
		NoteLinksInput? links = null,
		string? supersedes_id = null,
		string? chosen = null,
		string? rejected = null,
		string? reason = null,
		string? basis = null,
		string? trap = null,
		string? escape = null,
		string? actor = null)
	{
		try
		{
			var campaignId = McpArgs.Guid(campaign_id);
			if (campaignId is null)
				return McpResults.Error("campaign_id is required");
			if (string.IsNullOrWhiteSpace(kind))
				return McpResults.Error(
					"kind is required. Valid values: Directive, Decision, Gotcha, Reference, Context");
			if (string.IsNullOrWhiteSpace(title))
				return McpResults.Error("title is required");

			if (!Enum.TryParse<BunkerNoteKind>(kind, ignoreCase: true, out var noteKind))
			{
				return McpResults.Error(
					$"Invalid kind: {kind}. Valid values: {string.Join(", ", Enum.GetNames<BunkerNoteKind>())}");
			}

			// Per-kind shape enforcement (MCP boundary only; HTTP endpoints stay free-form).
			if (noteKind == BunkerNoteKind.Decision)
			{
				if (string.IsNullOrWhiteSpace(chosen))
					return McpResults.Error(
						"A Decision needs `chosen`: the option that was selected, so a later reader knows what direction was taken.");
				if (string.IsNullOrWhiteSpace(rejected))
					return McpResults.Error(
						"A Decision needs `rejected`: what the alternative was, so a later reader can tell whether the rejection still holds.");
				if (string.IsNullOrWhiteSpace(reason))
					return McpResults.Error(
						"A Decision needs `reason`: why the chosen option won out over the alternative.");
				if (string.IsNullOrWhiteSpace(basis))
					return McpResults.Error(
						"A Decision needs `basis`: a pointer to where the claim can be checked — a #note, #objective.step, commit sha, or URL.");
				body = $"**Chosen.** {chosen}\n\n**Rejected.** {rejected}\n\n**Reason.** {reason}\n\n**Basis.** {basis}";
			}
			else if (noteKind == BunkerNoteKind.Gotcha)
			{
				if (string.IsNullOrWhiteSpace(trap))
					return McpResults.Error(
						"A Gotcha needs `trap`: the thing that can go wrong, so the reader can watch for it.");
				if (string.IsNullOrWhiteSpace(escape))
					return McpResults.Error(
						"A Gotcha needs `escape`: how to avoid or recover from the trap.");
				body = $"**Trap.** {trap}\n\n**Escape.** {escape}";
			}
			else if (noteKind == BunkerNoteKind.Context)
			{
				if (links is null ||
					(links.ObjectiveIds is null || links.ObjectiveIds.Count == 0) &&
					(links.StepIds is null || links.StepIds.Count == 0))
				{
					return McpResults.Error(
						"A Context note must link at least one objective or step so the reader knows what it belongs to.");
				}
			}
			else if (noteKind == BunkerNoteKind.Directive || noteKind == BunkerNoteKind.Reference)
			{
				if (string.IsNullOrWhiteSpace(body))
					return McpResults.Error(
						noteKind == BunkerNoteKind.Directive
							? "A Directive needs `body`: the hard rule to enforce."
							: "A Reference needs `body`: the information or pointer to document.");
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
				McpArgs.Guid(supersedes_id),
				basis: basis);

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
				noteStatus,
				includeDormant: true);
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "search_knowledge")]
	[Description(
		"Search campaign knowledge semantically and by keyword. Returns ranked Bunker notes " +
		"with snippets. Falls back to keyword-only results when embeddings are unavailable.")]
	public static async Task<object> SearchKnowledgeAsync(
		KnowledgeSearchService service,
		string query,
		string? campaign_id = null,
		string? mode = null,
		int? limit = null)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(query))
				return McpResults.Error("query is required");

			var campaignId = McpArgs.Guid(campaign_id);
			if (!string.IsNullOrWhiteSpace(campaign_id) && campaignId is null)
				return McpResults.Error("campaign_id must be a GUID");

			var searchMode = KnowledgeSearchMode.Hybrid;
			if (!string.IsNullOrWhiteSpace(mode) &&
				!Enum.TryParse(mode, ignoreCase: true, out searchMode))
			{
				return McpResults.Error(
					$"Invalid mode: {mode}. Valid values: {string.Join(", ", Enum.GetNames<KnowledgeSearchMode>())}");
			}

			return await service.SearchAsync(campaignId, query, searchMode, limit);
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
