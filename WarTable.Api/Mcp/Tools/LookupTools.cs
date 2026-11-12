using System.ComponentModel;
using ModelContextProtocol.Server;
using WarTable.Api.Services;
using WarTable.Api.Services.Embeddings;

namespace WarTable.Api.Mcp.Tools;

/// <summary>Shared campaign-scoped alias lookups and bounded knowledge context.</summary>
[McpServerToolType]
public static class LookupTools
{
	[McpServerTool(Name = "get_object")]
	[Description(
		"Resolve a campaign-scoped alias (#N) to the objective or Bunker note it identifies. " +
		"Soft-deleted objects still resolve, with \"deleted\": true. Objectives include a " +
		"compact summary plus a bounded `knowledge` context (linked notes, tagged notes and " +
		"semantically related gotchas/decisions/context as short excerpts). Knowledge is " +
		"null for notes and when `include_knowledge` is false. Full note bodies are only " +
		"available through `get_note`; a `knowledge_error` means context was skipped.")]
	public static async Task<object> GetObjectAsync(
		WarTableService service,
		KnowledgeContextService context,
		string campaign_id,
		int alias,
		bool? include_knowledge = null)
	{
		try
		{
			var campaignId = McpArgs.Guid(campaign_id);
			if (campaignId is null)
				return McpResults.Error("campaign_id is required");

			var resolved = await service.ResolveAliasAsync(campaignId.Value, alias);
			if (resolved is null)
				return McpResults.Error($"No object #{alias} in campaign {campaignId}");

			if (!string.Equals(resolved.Kind, "Objective", StringComparison.Ordinal))
			{
				return new AliasResolutionDetail(
					resolved.Kind,
					resolved.Id,
					resolved.Title,
					resolved.Deleted,
					resolved.CampaignId,
					resolved.Alias,
					null,
					null);
			}

			var fullObjective = await service.GetObjectiveAsync(resolved.Id);
			var objective = fullObjective is null
				? null
				: new ObjectiveSummary(
					fullObjective.Id,
					fullObjective.CampaignId,
					fullObjective.Alias,
					fullObjective.Title,
					fullObjective.Description,
					fullObjective.PlanState,
					fullObjective.Status,
					fullObjective.UpdatedAt);
			KnowledgeContext? knowledge = null;
			string? knowledgeError = null;

			if (fullObjective is not null && !resolved.Deleted && (include_knowledge ?? true))
			{
				try
				{
					knowledge = await context.GetForObjectiveAsync(campaignId.Value, fullObjective);
				}
				catch (Exception ex)
				{
					knowledgeError = ex.Message;
				}
			}

			return new AliasResolutionDetail(
				resolved.Kind,
				resolved.Id,
				resolved.Title,
				resolved.Deleted,
				resolved.CampaignId,
				resolved.Alias,
				objective,
				knowledge,
				knowledgeError);
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "get_step")]
	[Description(
		"Resolve a #N.M step reference to the step, including its updates, evidence and " +
		"dependencies, plus a bounded `knowledge` context of linked, tagged and semantically " +
		"related gotchas, decisions, context and directives as short excerpts. Set " +
		"`include_knowledge` to false to skip context. Full note bodies are only available " +
		"through `get_note`; `knowledge_error` reports a skipped context without failing the step.")]
	public static async Task<object> GetStepAsync(
		WarTableService service,
		KnowledgeContextService context,
		string campaign_id,
		int objective_alias,
		int step_number,
		bool? include_knowledge = null)
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

			if (step is null)
				return McpResults.Error(
					$"No step #{objective_alias}.{step_number} in campaign {campaignId}");

			KnowledgeContext? knowledge = null;
			string? knowledgeError = null;

			if (include_knowledge ?? true)
			{
				try
				{
					knowledge = await context.GetForStepAsync(campaignId.Value, step);
				}
				catch (Exception ex)
				{
					knowledgeError = ex.Message;
				}
			}

			return new StepLookupDetail(step, knowledge, knowledgeError);
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "get_context")]
	[Description(
		"Get bounded campaign knowledge for pre-planning or new integration work. Supply " +
		"`campaign_id` plus at least one of `objective_id`, `step_id`, `query` or `tags`. " +
		"Returns directives, decisions, gotchas, context and open questions as " +
		"short excerpts grouped by kind, with linked/tagged/semantic match reasons. Context " +
		"degrades to explicit links and directives when embeddings are unavailable " +
		"(`degraded: true` with a reason). Full note bodies are only available through `get_note`.")]
	public static async Task<object> GetContextAsync(
		KnowledgeContextService context,
		string campaign_id,
		string? objective_id = null,
		string? step_id = null,
		string? query = null,
		string? tags = null)
	{
		try
		{
			var campaignId = McpArgs.Guid(campaign_id);
			if (campaignId is null)
				return McpResults.Error("campaign_id is required");

			Guid? objectiveId = null;
			if (!string.IsNullOrWhiteSpace(objective_id))
			{
				if (!Guid.TryParse(objective_id, out var parsedObjectiveId))
					return McpResults.Error("objective_id must be a valid GUID");
				objectiveId = parsedObjectiveId;
			}

			Guid? stepId = null;
			if (!string.IsNullOrWhiteSpace(step_id))
			{
				if (!Guid.TryParse(step_id, out var parsedStepId))
					return McpResults.Error("step_id must be a valid GUID");
				stepId = parsedStepId;
			}

			if (objectiveId is null &&
				stepId is null &&
				string.IsNullOrWhiteSpace(query) &&
				string.IsNullOrWhiteSpace(tags))
			{
				return McpResults.Error(
					"Provide at least one of objective_id, step_id, query, or tags.");
			}

			return await context.GetAsync(
				campaignId.Value,
				objectiveId,
				stepId,
				query,
				tags);
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}
}
