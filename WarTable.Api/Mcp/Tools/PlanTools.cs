using System.ComponentModel;
using ModelContextProtocol.Server;
using WarTable.Api.Models;
using WarTable.Api.Services;
using WarTable.Api.Services.Embeddings;

namespace WarTable.Api.Mcp.Tools;

/// <summary>Plan, briefing, attention and activity tools.</summary>
[McpServerToolType]
public static class PlanTools
{
	[McpServerTool(Name = "import_plan")]
	[Description(
		"Atomically import a campaign/objective/step tree. Supply either an existing " +
		"`campaign_id` or `campaign` metadata to create a new campaign. Step `depends_on` " +
		"values reference the client-side `key` of other steps in the same import.")]
	public static async Task<object> ImportPlanAsync(
		WarTableService service,
		List<ImportPlanObjectiveInput> objectives,
		string? campaign_id = null,
		ImportPlanCampaignInput? campaign = null,
		string? actor = null)
	{
		try
		{
			if (objectives is null || objectives.Count == 0)
				return McpResults.Error("objectives is required and must be a non-empty array");

			var importedObjectives = new List<ImportPlanObjective>(objectives.Count);
			foreach (var objective in objectives)
			{
				if (objective is null)
					return McpResults.Error("Each objective must be an object");

				PlanState? planState = null;
				var rawPlanState = objective.PlanState;
				if (!string.IsNullOrWhiteSpace(rawPlanState))
				{
					if (!Enum.TryParse<PlanState>(rawPlanState, ignoreCase: true, out var parsed))
						return McpResults.Error(
							$"Invalid plan_state: {rawPlanState}. Valid values: Seed, Backlog, Ready");
					planState = parsed;
				}

				var steps = new List<ImportPlanStep>();
				if (objective.Steps is not null)
				{
					foreach (var step in objective.Steps)
					{
						if (step is null)
							return McpResults.Error("Each step must be an object");

						steps.Add(new ImportPlanStep(
							Key: step.Key,
							Title: step.Title ?? "",
							Context: step.Context,
							Assignee: step.Assignee,
							Assigner: step.Assigner,
							DependsOn: step.DependsOn));
					}
				}

				importedObjectives.Add(new ImportPlanObjective(
					Key: objective.Key,
					Title: objective.Title ?? "",
					Description: objective.Description,
					PlanState: planState,
					Steps: steps));
			}

			ImportPlanCampaign? importedCampaign = null;
			if (campaign is not null)
			{
				importedCampaign = new ImportPlanCampaign(
					Title: campaign.Title,
					Charter: campaign.Charter,
					Tags: campaign.Tags);
			}

			var request = new ImportPlanRequest(
				CampaignId: McpArgs.Guid(campaign_id),
				Campaign: importedCampaign,
				Objectives: importedObjectives);

			return await service.ImportPlanAsync(request, McpArgs.Actor(actor));
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "get_plan")]
	[Description(
		"Get a campaign's full plan: objectives in order, each with its steps, updates and " +
		"evidence, plus a `knowledge` summary of explicit note-link counts by kind " +
		"(decisions, gotchas, context, questions). Zero counts mean no explicit " +
		"links; use `get_context` or `search_knowledge` for semantic discovery and `get_note` " +
		"for full bodies.")]
	public static async Task<object> GetPlanAsync(
		WarTableService service,
		KnowledgeContextService context,
		string campaign_id,
		bool? include_not_ready = null)
	{
		try
		{
			var id = McpArgs.Guid(campaign_id);
			if (id is null)
				return McpResults.Error("campaign_id is required");

			var objectives = await service.GetPlanAsync(id.Value, include_not_ready ?? true);
			try
			{
				var counts = await context.GetCountsForObjectivesAsync(id.Value, objectives);
				foreach (var objective in objectives)
				{
					if (counts.TryGetValue(objective.Id, out var knowledge))
						objective.Knowledge = knowledge;
				}
			}
			catch
			{
				// Counts are an enhancement; a failure must not hide the plan.
			}

			return objectives;
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "get_briefing")]
	[Description(
		"Get bounded, structured context for starting work: campaign, ready/blocked/flagged " +
		"steps, open questions, directives, preferences, recent decisions and activity.")]
	public static async Task<object> GetBriefingAsync(
		WarTableService service,
		string campaign_id,
		long? since = null)
	{
		try
		{
			var id = McpArgs.Guid(campaign_id);
			if (id is null)
				return McpResults.Error("campaign_id is required");

			return await service.GetBriefingAsync(id.Value, since);
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "get_attention")]
	[Description("Get the cross-campaign attention inbox: flagged steps, blocked objectives, open questions, stale steps and drafts.")]
	public static async Task<object> GetAttentionAsync(
		WarTableService service,
		string? campaign_id = null)
	{
		try
		{
			return await service.GetAttentionAsync(McpArgs.Guid(campaign_id));
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}

	[McpServerTool(Name = "get_activity")]
	[Description("Get the append-only activity feed, optionally scoped to a campaign or resumed from a cursor.")]
	public static async Task<object> GetActivityAsync(
		WarTableService service,
		string? campaign_id = null,
		long? since = null,
		int? limit = null)
	{
		try
		{
			return await service.GetActivityAsync(
				McpArgs.Guid(campaign_id),
				since,
				limit ?? 50);
		}
		catch (Exception ex)
		{
			return McpResults.Error(ex.Message);
		}
	}
}

/// <summary>Campaign metadata supplied when import_plan creates a new campaign.</summary>
public sealed class ImportPlanCampaignInput
{
	public string Title { get; set; } = "";
	public string? Charter { get; set; }
	public string? Tags { get; set; }
}

/// <summary>One objective in an import. <see cref="Key"/> is a client-side handle.</summary>
public sealed class ImportPlanObjectiveInput
{
	public string? Key { get; set; }
	public string Title { get; set; } = "";
	public string? Description { get; set; }

	/// <summary>Seed, Backlog, or Ready, parsed case-insensitively.</summary>
	public string? PlanState { get; set; }

	public List<ImportPlanStepInput>? Steps { get; set; }
}

/// <summary>One step in an import. <see cref="Key"/> is a client-side handle for dependencies.</summary>
public sealed class ImportPlanStepInput
{
	public string? Key { get; set; }
	public string Title { get; set; } = "";
	public string? Context { get; set; }
	public string? Assignee { get; set; }
	public string? Assigner { get; set; }
	public List<string>? DependsOn { get; set; }
}
