using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WarTable.Api.Models;
using WarTable.Api.Services;
using WarTable.Api.Services.Embeddings;

namespace WarTable.Api.Mcp.Tools;

/// <summary>Bunker note tools.</summary>
[McpServerToolType]
public static class BunkerTools
{
	// ── Shared create helper ──

	private static async Task<object> CreateNoteAsync(
		WarTableService service,
		string campaign_id,
		BunkerNoteKind kind,
		string title,
		string body,
		string? tags,
		bool? pinned,
		NoteLinksInput? links,
		string? supersedes_id,
		string? actor,
		string? basis)
	{
		try
		{
			var campaignId = McpArgs.Guid(campaign_id);
			if (campaignId is null)
				return McpResults.Error("campaign_id is required");
			if (string.IsNullOrWhiteSpace(title))
				return McpResults.Error("title is required");

			IEnumerable<Guid> linkedObjectiveIds =
				McpArgs.Guids(links?.ObjectiveIds) ?? Array.Empty<Guid>();
			IEnumerable<Guid> linkedStepIds =
				McpArgs.Guids(links?.StepIds) ?? Array.Empty<Guid>();

			var note = await service.CreateNoteAsync(
				campaignId.Value,
				kind,
				title,
				body,
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

	// ── Per-kind tools ──

	[McpServerTool(Name = "add_decision")]
	[Description(
		"Record a decision: a choice between named alternatives. Only call this " +
		"when you can name the alternative you rejected and say why. If there was " +
		"no alternative, there is no decision to record. `basis` points at where " +
		"the claim can be checked (`#12`, `#12.3`, a commit sha, or a URL); it " +
		"makes the claim cheap to disprove, not true.")]
	public static async Task<object> AddDecisionAsync(
		WarTableService service,
		string campaign_id,
		string title,
		string chosen,
		string rejected,
		string reason,
		string basis,
		string? tags = null,
		bool? pinned = null,
		NoteLinksInput? links = null,
		string? supersedes_id = null,
		string? actor = null)
	{
		var body = $"**Chosen.** {chosen}\n\n**Rejected.** {rejected}\n\n**Reason.** {reason}\n\n**Basis.** {basis}";
		return await CreateNoteAsync(
			service, campaign_id, BunkerNoteKind.Decision, title, body,
			tags, pinned, links, supersedes_id, actor, basis);
	}

	[McpServerTool(Name = "add_gotcha")]
	[Description(
		"Record a trap: something that can go wrong, and how to avoid or recover " +
		"from it. Only if someone could reasonably do the wrong thing. If the code " +
		"already makes it obvious, it is not a note.")]
	public static async Task<object> AddGotchaAsync(
		WarTableService service,
		string campaign_id,
		string title,
		string trap,
		string escape,
		string? basis = null,
		string? tags = null,
		bool? pinned = null,
		NoteLinksInput? links = null,
		string? supersedes_id = null,
		string? actor = null)
	{
		var body = $"**Trap.** {trap}\n\n**Escape.** {escape}";
		return await CreateNoteAsync(
			service, campaign_id, BunkerNoteKind.Gotcha, title, body,
			tags, pinned, links, supersedes_id, actor, basis);
	}

	[McpServerTool(Name = "add_context")]
	[Description(
		"Record background that only means something for specific work. It must " +
		"link at least one objective or step, and it goes dormant 30 days after " +
		"nothing live links to it.")]
	public static async Task<object> AddContextAsync(
		WarTableService service,
		string campaign_id,
		string title,
		NoteLinksInput links,
		string? body = null,
		string? tags = null,
		bool? pinned = null,
		string? basis = null,
		string? supersedes_id = null,
		string? actor = null)
	{
		if ((links.ObjectiveIds is null || links.ObjectiveIds.Count == 0) &&
			(links.StepIds is null || links.StepIds.Count == 0))
		{
			return McpResults.Error(
				"A Context note must link at least one objective or step so the reader knows what it belongs to.");
		}

		return await CreateNoteAsync(
			service, campaign_id, BunkerNoteKind.Context, title, body ?? "",
			tags, pinned, links, supersedes_id, actor, basis);
	}

	[McpServerTool(Name = "add_directive")]
	[Description(
		"Record a hard campaign rule that applies to all work. Rare: only when " +
		"violating it is a defect, not a preference.")]
	public static async Task<object> AddDirectiveAsync(
		WarTableService service,
		string campaign_id,
		string title,
		string body,
		string? tags = null,
		bool? pinned = null,
		NoteLinksInput? links = null,
		string? supersedes_id = null,
		string? actor = null)
	{
		return await CreateNoteAsync(
			service, campaign_id, BunkerNoteKind.Directive, title, body,
			tags, pinned, links, supersedes_id, actor, basis: null);
	}

	// ── Other note tools (unchanged) ──

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
	[Description(
		"Update Bunker note fields. Only the fields you include are changed. " +
		"Requires row_version (read with get_note) for optimistic concurrency.")]
	public static async Task<object> UpdateNoteAsync(
		WarTableService service,
		string note_id,
		string row_version,
		string? title = null,
		string? body = null,
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

			var (rv, rvError) = McpArgs.RowVersion(row_version);
			if (rvError is not null)
				return McpResults.Error(rvError);

			var patch = new BunkerNotePatch(
				Title: title,
				Body: body,
				Pinned: pinned,
				Tags: tags,
				SupersedesId: McpArgs.Guid(supersedes_id));

			var updated = await service.UpdateNoteAsync(id.Value, patch, McpArgs.Actor(actor), rv);
			return updated is null
				? McpResults.Error($"Note not found: {id}")
				: updated;
		}
		catch (ConcurrencyConflictException ex)
		{
			return McpConflictResult(ex);
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "delete_note")]
	[Description(
		"Soft-delete a Bunker note. Requires row_version (read with get_note) for optimistic concurrency.")]
	public static async Task<object> DeleteNoteAsync(
		WarTableService service,
		string note_id,
		string row_version,
		string? actor = null)
	{
		try
		{
			var id = McpArgs.Guid(note_id);
			if (id is null)
				return McpResults.Error("note_id is required");

			var (rv, rvError) = McpArgs.RowVersion(row_version);
			if (rvError is not null)
				return McpResults.Error(rvError);

			var deleted = await service.DeleteNoteAsync(id.Value, McpArgs.Actor(actor), rv);
			return deleted
				? new { ok = true }
				: McpResults.Error($"Note not found: {id}");
		}
		catch (ConcurrencyConflictException ex)
		{
			return McpConflictResult(ex);
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	/// <summary>Builds the stale-rowVersion error envelope for MCP tools.</summary>
	private static CallToolResult McpConflictResult(ConcurrencyConflictException ex)
	{
		var currentJson = System.Text.Json.JsonSerializer.Serialize(ex.Current, McpJson.Options);
		return McpResults.Error(
			$$"""{"error":"conflict","message":"{{ex.Message.Replace("\"", "\\\"")}}","current":{{currentJson}}}""");
	}
}

/// <summary>Links from a Bunker note to campaign objects.</summary>
public sealed class NoteLinksInput
{
	public List<string>? ObjectiveIds { get; set; }
	public List<string>? StepIds { get; set; }
}
