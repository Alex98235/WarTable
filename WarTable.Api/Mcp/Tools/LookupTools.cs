using System.ComponentModel;
using ModelContextProtocol.Server;
using WarTable.Api.Services;

namespace WarTable.Api.Mcp.Tools;

/// <summary>Shared campaign-scoped alias lookups.</summary>
[McpServerToolType]
public static class LookupTools
{
	[McpServerTool(Name = "get_object")]
	[Description(
		"Resolve a campaign-scoped alias (#N) to the objective or Bunker note it identifies. " +
		"Soft-deleted objects still resolve, with \"deleted\": true.")]
	public static async Task<object> GetObjectAsync(
		WarTableService service,
		string campaign_id,
		int alias)
	{
		try
		{
			var campaignId = McpArgs.Guid(campaign_id);
			if (campaignId is null)
				return McpResults.Error("campaign_id is required");

			var resolved = await service.ResolveAliasAsync(campaignId.Value, alias);
			return resolved is null
				? McpResults.Error($"No object #{alias} in campaign {campaignId}")
				: resolved;
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "get_step")]
	[Description("Resolve a #N.M step reference to the step, including its updates, evidence and dependencies.")]
	public static async Task<object> GetStepAsync(
		WarTableService service,
		string campaign_id,
		int objective_alias,
		int step_number)
	{
		try
		{
			var campaignId = McpArgs.Guid(campaign_id);
			if (campaignId is null)
				return McpResults.Error("campaign_id is required");

			var step = await service.GetStepByRefAsync(
				campaignId.Value,
				objective_alias,
				step_number);

			return step is null
				? McpResults.Error(
					$"No step #{objective_alias}.{step_number} in campaign {campaignId}")
				: step;
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}
}
