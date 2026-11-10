using System.ComponentModel;
using ModelContextProtocol.Server;
using WarTable.Api.Services;

namespace WarTable.Api.Mcp.Tools;

/// <summary>Campaign tools, ported from the hand-rolled MCP protocol.</summary>
[McpServerToolType]
public static class CampaignTools
{
	[McpServerTool(Name = "create_campaign")]
	[Description("Create a new campaign in the planning workspace.")]
	public static async Task<object> CreateCampaignAsync(
		WarTableService service,
		string title,
		string? intent = null,
		string? tags = null,
		string? actor = null)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(title))
				return McpResults.Error("title is required");

			var campaign = await service.CreateCampaignAsync(
				title,
				intent ?? "",
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
	[Description("Update campaign metadata. Only the fields you include are changed.")]
	public static async Task<object> UpdateCampaignAsync(
		WarTableService service,
		string campaign_id,
		string? title = null,
		string? intent = null,
		string? tags = null,
		string? actor = null)
	{
		try
		{
			var id = McpArgs.Guid(campaign_id);
			if (id is null)
				return McpResults.Error("campaign_id is required");

			var patch = new CampaignPatch(
				Title: title,
				Intent: intent,
				Tags: tags);

			var updated = await service.UpdateCampaignAsync(id.Value, patch, McpArgs.Actor(actor));
			return updated is null
				? McpResults.Error($"Campaign not found: {id}")
				: updated;
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "delete_campaign")]
	[Description("Delete a campaign and its objectives, steps and notes.")]
	public static async Task<object> DeleteCampaignAsync(
		WarTableService service,
		string campaign_id,
		string? actor = null)
	{
		try
		{
			var id = McpArgs.Guid(campaign_id);
			if (id is null)
				return McpResults.Error("campaign_id is required");

			var deleted = await service.DeleteCampaignAsync(id.Value, McpArgs.Actor(actor));
			return deleted
				? new { ok = true }
				: McpResults.Error($"Campaign not found: {id}");
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}
}
