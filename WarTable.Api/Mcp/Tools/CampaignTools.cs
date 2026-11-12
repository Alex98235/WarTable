using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WarTable.Api.Services;

namespace WarTable.Api.Mcp.Tools;

/// <summary>Campaign tools, ported from the hand-rolled MCP protocol.</summary>
[McpServerToolType]
public static class CampaignTools
{
	[McpServerTool(Name = "create_campaign")]
	[Description(
		"Create a new campaign in the planning workspace. A non-empty charter is " +
		"stored on the campaign record (why it exists, scope, architecture).")]
	public static async Task<object> CreateCampaignAsync(
		WarTableService service,
		string title,
		string? charter = null,
		string? tags = null,
		string? actor = null)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(title))
				return McpResults.Error("title is required");

			var campaign = await service.CreateCampaignAsync(
				title,
				charter ?? "",
				tags ?? "",
				McpArgs.Actor(actor));

			return campaign;
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "get_campaign")]
	[Description(
		"Get a single campaign by id, including its objectives and Bunker notes. " +
		"Objectives and notes carry an `alias` integer — the campaign-scoped #N identifier.")]
	public static async Task<object> GetCampaignAsync(
		WarTableService service,
		string campaign_id)
	{
		try
		{
			var id = McpArgs.Guid(campaign_id);
			if (id is null)
				return McpResults.Error("campaign_id is required");

			var campaign = await service.GetCampaignAsync(id.Value);
			return campaign is null
				? McpResults.Error($"Campaign not found: {id}")
				: campaign;
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "list_campaigns")]
	[Description("List campaigns, optionally filtered by search text. Archived campaigns are hidden by default.")]
	public static async Task<object> ListCampaignsAsync(
		WarTableService service,
		string? search = null,
		bool? include_archived = null)
	{
		try
		{
			return await service.GetCampaignsAsync(search, include_archived ?? false);
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "update_campaign")]
	[Description(
		"Update campaign metadata (title, tags, charter, archived_at). " +
		"Requires row_version (read with get_campaign) for optimistic concurrency.")]
	public static async Task<object> UpdateCampaignAsync(
		WarTableService service,
		string campaign_id,
		string row_version,
		string? title = null,
		string? tags = null,
		string? charter = null,
		DateTime? archived_at = null,
		string? actor = null)
	{
		try
		{
			var id = McpArgs.Guid(campaign_id);
			if (id is null)
				return McpResults.Error("campaign_id is required");

			var (rv, rvError) = McpArgs.RowVersion(row_version);
			if (rvError is not null)
				return McpResults.Error(rvError);

			var patch = new CampaignPatch(
				Title: title,
				Tags: tags,
				ArchivedAt: archived_at,
				Charter: charter);

			var updated = await service.UpdateCampaignAsync(id.Value, patch, McpArgs.Actor(actor), rv);
			return updated is null
				? McpResults.Error($"Campaign not found: {id}")
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

	[McpServerTool(Name = "delete_campaign")]
	[Description(
		"Delete a campaign and its objectives, steps and notes. " +
		"Requires row_version (read with get_campaign) for optimistic concurrency.")]
	public static async Task<object> DeleteCampaignAsync(
		WarTableService service,
		string campaign_id,
		string row_version,
		string? actor = null)
	{
		try
		{
			var id = McpArgs.Guid(campaign_id);
			if (id is null)
				return McpResults.Error("campaign_id is required");

			var (rv, rvError) = McpArgs.RowVersion(row_version);
			if (rvError is not null)
				return McpResults.Error(rvError);

			var deleted = await service.DeleteCampaignAsync(id.Value, McpArgs.Actor(actor), rv);
			return deleted
				? new { ok = true }
				: McpResults.Error($"Campaign not found: {id}");
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
