using System.ComponentModel;
using ModelContextProtocol.Server;
using WarTable.Api.Models;
using WarTable.Api.Services;

namespace WarTable.Api.Mcp.Tools;

/// <summary>Step tools.</summary>
[McpServerToolType]
public static class StepTools
{
	[McpServerTool(Name = "create_step")]
	[Description("Add a step under an objective. Steps are the atomic unit of work.")]
	public static async Task<object> CreateStepAsync(
		WarTableService service,
		string objective_id,
		string title,
		string? context = null,
		string? assignee = null,
		string? assigner = null,
		string[]? depends_on = null,
		string? actor = null)
	{
		try
		{
			var objectiveId = McpArgs.Guid(objective_id);
			if (objectiveId is null)
				return McpResults.Error("objective_id is required");
			if (string.IsNullOrWhiteSpace(title))
				return McpResults.Error("title is required");

			var step = await service.CreateStepAsync(
				objectiveId.Value,
				title,
				context ?? "",
				assignee ?? "",
				assigner ?? "orchestrator",
				McpArgs.Guids(depends_on) ?? Array.Empty<Guid>(),
				McpArgs.Actor(actor));

			return step;
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "update_step")]
	[Description("Update step fields and/or its dependency set. Omit a field to leave it unchanged.")]
	public static async Task<object> UpdateStepAsync(
		WarTableService service,
		string step_id,
		string? title = null,
		string? context = null,
		string? assignee = null,
		string? assigner = null,
		string? status = null,
		string[]? depends_on = null,
		string? actor = null)
	{
		try
		{
			var id = McpArgs.Guid(step_id);
			if (id is null)
				return McpResults.Error("step_id is required");

			var patch = new StepPatch(
				Title: title,
				Context: context,
				Assignee: assignee,
				Assigner: assigner,
				Status: status);

			// null means "leave dependencies unchanged"; an empty array clears them.
			var dependsOn = McpArgs.Guids(depends_on);
			var updated = await service.UpdateStepAsync(
				id.Value,
				patch,
				dependsOn,
				McpArgs.Actor(actor));

			return updated is null
				? McpResults.Error($"Step not found: {id}")
				: updated;
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "update_step_status")]
	[Description("Quickly update a step's status. The parent objective's derived status is recalculated.")]
	public static async Task<object> UpdateStepStatusAsync(
		WarTableService service,
		string step_id,
		string status,
		string? actor = null)
	{
		try
		{
			var id = McpArgs.Guid(step_id);
			if (id is null)
				return McpResults.Error("step_id is required");
			if (string.IsNullOrWhiteSpace(status))
				return McpResults.Error("status is required");

			var updated = await service.UpdateStepStatusAsync(id.Value, status, McpArgs.Actor(actor));
			return updated is null
				? McpResults.Error($"Step not found: {id}")
				: updated;
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "reorder_steps")]
	[Description("Reorder steps within an objective by providing a mapping of step_id → sort_order.")]
	public static async Task<object> ReorderStepsAsync(
		WarTableService service,
		string objective_id,
		Dictionary<string, int> orders,
		string? actor = null)
	{
		try
		{
			var objectiveId = McpArgs.Guid(objective_id);
			if (objectiveId is null)
				return McpResults.Error("objective_id is required");

			var parsedOrders = McpArgs.Orders(orders);
			if (parsedOrders.Count == 0)
				return McpResults.Error(
					"orders is required (object mapping step_id to sort_order)");

			var ok = await service.ReorderStepsAsync(
				objectiveId.Value,
				parsedOrders,
				McpArgs.Actor(actor));

			return new { ok };
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "delete_step")]
	[Description("Soft-delete a step and remove dependency edges that touch it.")]
	public static async Task<object> DeleteStepAsync(
		WarTableService service,
		string step_id,
		string? actor = null)
	{
		try
		{
			var id = McpArgs.Guid(step_id);
			if (id is null)
				return McpResults.Error("step_id is required");

			var deleted = await service.DeleteStepAsync(id.Value, McpArgs.Actor(actor));
			return deleted
				? new { ok = true }
				: McpResults.Error($"Step not found: {id}");
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "add_step_update")]
	[Description("Append an update (note, blocker, result or handoff) to a step.")]
	public static async Task<object> AddStepUpdateAsync(
		WarTableService service,
		string step_id,
		string body,
		string? kind = null,
		string? actor = null)
	{
		try
		{
			var stepId = McpArgs.Guid(step_id);
			if (stepId is null)
				return McpResults.Error("step_id is required");
			if (string.IsNullOrWhiteSpace(body))
				return McpResults.Error("body is required");

			var updateKind = StepUpdateKind.Note;
			if (!string.IsNullOrWhiteSpace(kind) &&
				!Enum.TryParse(kind, ignoreCase: true, out updateKind))
			{
				return McpResults.Error(
					$"Invalid kind: {kind}. Valid values: {string.Join(", ", Enum.GetNames<StepUpdateKind>())}");
			}

			var update = await service.AddStepUpdateAsync(
				stepId.Value,
				body,
				updateKind,
				McpArgs.Actor(actor));

			return update is null
				? McpResults.Error($"Step not found: {stepId}")
				: update;
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "add_evidence")]
	[Description("Attach inspectable proof (URL, path, hash or result text) to a step.")]
	public static async Task<object> AddEvidenceAsync(
		WarTableService service,
		string step_id,
		string label,
		string value,
		string? kind = null,
		string? actor = null)
	{
		try
		{
			var stepId = McpArgs.Guid(step_id);
			if (stepId is null)
				return McpResults.Error("step_id is required");
			if (string.IsNullOrWhiteSpace(label))
				return McpResults.Error("label is required");
			if (string.IsNullOrWhiteSpace(value))
				return McpResults.Error("value is required");

			var evidenceKind = EvidenceKind.Link;
			if (!string.IsNullOrWhiteSpace(kind) &&
				!Enum.TryParse(kind, ignoreCase: true, out evidenceKind))
			{
				return McpResults.Error(
					$"Invalid kind: {kind}. Valid values: {string.Join(", ", Enum.GetNames<EvidenceKind>())}");
			}

			var evidence = await service.AddEvidenceAsync(
				stepId.Value,
				label,
				evidenceKind,
				value,
				McpArgs.Actor(actor));

			return evidence is null
				? McpResults.Error($"Step not found: {stepId}")
				: evidence;
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}
}
