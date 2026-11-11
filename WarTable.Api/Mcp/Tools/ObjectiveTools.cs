using System.ComponentModel;
using ModelContextProtocol.Server;
using WarTable.Api.Models;
using WarTable.Api.Services;

namespace WarTable.Api.Mcp.Tools;

/// <summary>Objective tools.</summary>
[McpServerToolType]
public static class ObjectiveTools
{
	[McpServerTool(Name = "create_objective")]
	[Description("Create a new objective under a campaign. plan_state is Seed (unfinished thought), Draft (default), or Active. Objective aliases (#N) are permanent and never reused.")]
	public static async Task<object> CreateObjectiveAsync(
		WarTableService service,
		string campaign_id,
		string title,
		string? description = null,
		string? plan_state = null,
		string? actor = null)
	{
		try
		{
			var campaignId = McpArgs.Guid(campaign_id);
			if (campaignId is null)
				return McpResults.Error("campaign_id is required");
			if (string.IsNullOrWhiteSpace(title))
				return McpResults.Error("title is required");

			var planState = PlanState.Draft;
			if (!string.IsNullOrWhiteSpace(plan_state) &&
				!Enum.TryParse(plan_state, ignoreCase: true, out planState))
			{
				return McpResults.Error(
					$"Invalid plan_state: {plan_state}. Valid values: Seed, Draft, Active");
			}

			var objective = await service.CreateObjectiveAsync(
				campaignId.Value,
				title,
				description ?? "",
				planState,
				McpArgs.Actor(actor));

			return objective;
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "update_objective")]
	[Description("Update objective fields. Only the fields you include are changed. plan_state is Seed (unfinished thought), Draft, or Active.")]
	public static async Task<object> UpdateObjectiveAsync(
		WarTableService service,
		string objective_id,
		string? title = null,
		string? description = null,
		string? plan_state = null,
		string? actor = null)
	{
		try
		{
			var id = McpArgs.Guid(objective_id);
			if (id is null)
				return McpResults.Error("objective_id is required");

			PlanState? planState = null;
			if (!string.IsNullOrWhiteSpace(plan_state))
			{
				if (!Enum.TryParse<PlanState>(plan_state, ignoreCase: true, out var parsed))
					return McpResults.Error(
						$"Invalid plan_state: {plan_state}. Valid values: Seed, Draft, Active");
				planState = parsed;
			}

			var patch = new ObjectivePatch(
				Title: title,
				Description: description,
				PlanState: planState);

			var updated = await service.UpdateObjectiveAsync(id.Value, patch, McpArgs.Actor(actor));
			return updated is null
				? McpResults.Error($"Objective not found: {id}")
				: updated;
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "reorder_objectives")]
	[Description("Reorder objectives within a campaign by providing a mapping of objective ID (GUID) → sort_order. orders keys are IDs, not #N aliases.")]
	public static async Task<object> ReorderObjectivesAsync(
		WarTableService service,
		string campaign_id,
		Dictionary<string, int> orders,
		string? actor = null)
	{
		try
		{
			var campaignId = McpArgs.Guid(campaign_id);
			if (campaignId is null)
				return McpResults.Error("campaign_id is required");

			var invalidKeys = McpArgs.InvalidOrderKeys(orders);
			if (invalidKeys.Length > 0)
				return McpResults.Error(
					$"orders keys must be objective IDs (GUIDs), not aliases. Invalid key(s): {string.Join(", ", invalidKeys)}");

			var parsedOrders = McpArgs.Orders(orders);
			if (parsedOrders.Count == 0)
				return McpResults.Error(
					"orders is required (object mapping objective_id to sort_order)");

			var ok = await service.ReorderObjectivesAsync(
				campaignId.Value,
				parsedOrders,
				McpArgs.Actor(actor));

			return new { ok };
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "delete_objective")]
	[Description("Soft-delete an objective and its steps. Old #N aliases remain resolvable and are never reused.")]
	public static async Task<object> DeleteObjectiveAsync(
		WarTableService service,
		string objective_id,
		string? actor = null)
	{
		try
		{
			var id = McpArgs.Guid(objective_id);
			if (id is null)
				return McpResults.Error("objective_id is required");

			var deleted = await service.DeleteObjectiveAsync(id.Value, McpArgs.Actor(actor));
			return deleted
				? new { ok = true }
				: McpResults.Error($"Objective not found: {id}");
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}
}
