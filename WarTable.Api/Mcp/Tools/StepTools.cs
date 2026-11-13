using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WarTable.Api.Models;
using WarTable.Api.Services;

namespace WarTable.Api.Mcp.Tools;

/// <summary>Step tools.</summary>
[McpServerToolType]
public static class StepTools
{
	[McpServerTool(Name = "create_step")]
	[Description("Add a step under an objective. Steps are the atomic unit of work; step numbers are permanent, never reused.")]
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
	[Description(
		"Update step fields and/or its dependency set. Omit a field to leave it unchanged. " +
		"Requires row_version (read with get_step) for optimistic concurrency.")]
	public static async Task<object> UpdateStepAsync(
		WarTableService service,
		string step_id,
		string row_version,
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

			var (rv, rvError) = McpArgs.RowVersion(row_version);
			if (rvError is not null)
				return McpResults.Error(rvError);

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
				McpArgs.Actor(actor),
				rv);

			return updated is null
				? McpResults.Error($"Step not found: {id}")
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

	[McpServerTool(Name = "update_step_status")]
	[Description(
		"Quickly update a step's status. The parent objective's derived status is recalculated. " +
		"Requires row_version (read with get_step) for optimistic concurrency.")]
	public static async Task<object> UpdateStepStatusAsync(
		WarTableService service,
		string step_id,
		string status,
		string row_version,
		string? actor = null)
	{
		try
		{
			var id = McpArgs.Guid(step_id);
			if (id is null)
				return McpResults.Error("step_id is required");
			if (string.IsNullOrWhiteSpace(status))
				return McpResults.Error("status is required");

			var (rv, rvError) = McpArgs.RowVersion(row_version);
			if (rvError is not null)
				return McpResults.Error(rvError);

			var updated = await service.UpdateStepStatusAsync(id.Value, status, McpArgs.Actor(actor), rv);
			return updated is null
				? McpResults.Error($"Step not found: {id}")
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

	[McpServerTool(Name = "reorder_steps")]
	[Description("Reorder steps within an objective by providing a mapping of step ID (GUID) → sort_order. This changes display order only; StepNumber does not change. orders keys must be step IDs (GUIDs), not #N.M numbers.")]
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

			var invalidKeys = McpArgs.InvalidOrderKeys(orders);
			if (invalidKeys.Length > 0)
				return McpResults.Error(
					$"orders keys must be step IDs (GUIDs), not step numbers. Invalid key(s): {string.Join(", ", invalidKeys)}");

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
	[Description(
		"Soft-delete a step. Fails if other steps depend on it unless force=true; force also removes dependency edges. Returns removed_dependencies. " +
		"Requires row_version (read with get_step) for optimistic concurrency.")]
	public static async Task<object> DeleteStepAsync(
		WarTableService service,
		string step_id,
		string row_version,
		bool force = false,
		string? actor = null)
	{
		try
		{
			var id = McpArgs.Guid(step_id);
			if (id is null)
				return McpResults.Error("step_id is required");

			var (rv, rvError) = McpArgs.RowVersion(row_version);
			if (rvError is not null)
				return McpResults.Error(rvError);

			var result = await service.DeleteStepAsync(id.Value, force, McpArgs.Actor(actor), rv);
			return result.Deleted
				? new { ok = true, removed_dependencies = result.RemovedDependencies }
				: McpResults.Error($"Step not found: {id}");
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
	[Description("Attach proof that lives outside the repository - a CI run, a deployed instance, a third-party result. Value is a URL (for Link) or a commit sha (for Commit).")]
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

			// Validate the value matches the kind.
			if (evidenceKind == EvidenceKind.Commit)
			{
				// 7-40 hex characters, no other shape.
				if (value.Length < 7 || value.Length > 40 ||
					!value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
				{
					return McpResults.Error(
						"A Commit needs a sha, not a description: 7-40 hex characters.");
				}
			}
			else // Link
			{
				if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
					(uri.Scheme != "http" && uri.Scheme != "https"))
				{
					return McpResults.Error(
						"A Link needs an absolute http or https URL.");
				}
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

	[McpServerTool(Name = "delete_evidence")]
	[Description("Remove a citation that is wrong or no longer true; a citation nobody can retract is a claim, not proof. Deletes the evidence row permanently; there is no soft delete.")]
	public static async Task<object> DeleteEvidenceAsync(
		WarTableService service,
		string step_id,
		string evidence_id,
		string? actor = null)
	{
		try
		{
			var stepId = McpArgs.Guid(step_id);
			if (stepId is null)
				return McpResults.Error("step_id is required");

			var evidenceId = McpArgs.Guid(evidence_id);
			if (evidenceId is null)
				return McpResults.Error("evidence_id is required");

			var removed = await service.RemoveEvidenceAsync(stepId.Value, evidenceId.Value, McpArgs.Actor(actor));

			return removed is null
				? McpResults.Error($"Evidence not found: {evidenceId}")
				: new { ok = true, step_id = stepId, evidence_id = evidenceId, label = removed.Label };
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}
}
