using Microsoft.EntityFrameworkCore;
using WarTable.Api.Models;

namespace WarTable.Api.Services;

/// <summary>Campaign metadata supplied when import_plan creates a new campaign.</summary>
public record ImportPlanCampaign(
	string? Title = null,
	string? Intent = null,
	string? Tags = null);

/// <summary>One step in an import. <c>Key</c> is a client-side handle for dependencies.</summary>
public record ImportPlanStep(
	string? Key = null,
	string Title = "",
	string? Context = null,
	string? Assignee = null,
	string? Assigner = null,
	IReadOnlyList<string>? DependsOn = null);

/// <summary>One objective in an import. <c>Key</c> is a client-side handle.</summary>
public record ImportPlanObjective(
	string? Key = null,
	string Title = "",
	string? Description = null,
	PlanState? PlanState = null,
	IReadOnlyList<ImportPlanStep>? Steps = null);

/// <summary>Atomic bulk plan import. Supply either an existing campaign id or campaign metadata.</summary>
public record ImportPlanRequest(
	Guid? CampaignId = null,
	ImportPlanCampaign? Campaign = null,
	IReadOnlyList<ImportPlanObjective>? Objectives = null);

public record ImportPlanStepResult(
	string? Key,
	Guid Id,
	int StepNumber,
	string Ref);

public record ImportPlanObjectiveResult(
	string? Key,
	Guid Id,
	int Alias,
	List<ImportPlanStepResult> Steps);

public record ImportPlanResult(
	Guid CampaignId,
	List<ImportPlanObjectiveResult> Objectives);

public partial class WarTableService
{
	/// <summary>
	/// Imports a campaign/objective/step tree in one transaction. Objectives default
	/// to <see cref="PlanState.Draft"/>; step dependencies are resolved from the
	/// client-side keys only after all steps exist, so forward references work.
	/// </summary>
	public async Task<ImportPlanResult> ImportPlanAsync(ImportPlanRequest request, string actor)
	{
		if (request.Objectives is null || request.Objectives.Count == 0)
			throw new InvalidOperationException("objectives is required and must not be empty.");

		if (request.CampaignId is null && request.Campaign is null)
			throw new InvalidOperationException("Either campaign_id or campaign metadata is required.");

		if (request.Campaign is not null && string.IsNullOrWhiteSpace(request.Campaign.Title))
			throw new InvalidOperationException("campaign.title is required when creating a campaign.");

		await using var transaction = await _db.Database.BeginTransactionAsync();
		try
		{
			Campaign campaign;
			if (request.CampaignId.HasValue)
			{
				campaign = await _db.Campaigns.FirstOrDefaultAsync(c => c.Id == request.CampaignId.Value)
					?? throw new InvalidOperationException($"Campaign not found: {request.CampaignId}");
			}
			else
			{
				campaign = await CreateCampaignAsync(
					request.Campaign!.Title!,
					request.Campaign.Intent ?? "",
					request.Campaign.Tags ?? "",
					actor);
			}

			var objectivesByKey = new Dictionary<string, Objective>(StringComparer.Ordinal);
			var stepsByKey = new Dictionary<string, Step>(StringComparer.Ordinal);
			var objectiveResults = new List<ImportPlanObjectiveResult>();

			foreach (var importedObjective in request.Objectives)
			{
				if (string.IsNullOrWhiteSpace(importedObjective.Title))
					throw new InvalidOperationException("Every objective requires a title.");

				var objective = await CreateObjectiveAsync(
					campaign.Id,
					importedObjective.Title,
					importedObjective.Description ?? "",
					importedObjective.PlanState ?? PlanState.Draft,
					actor);

				if (!string.IsNullOrWhiteSpace(importedObjective.Key))
				{
					if (!objectivesByKey.TryAdd(importedObjective.Key, objective))
						throw new InvalidOperationException(
							$"Duplicate objective key in import: {importedObjective.Key}");
				}

				var stepResults = new List<ImportPlanStepResult>();
				foreach (var importedStep in importedObjective.Steps ?? [])
				{
					if (string.IsNullOrWhiteSpace(importedStep.Title))
						throw new InvalidOperationException(
							$"Every step in objective \"{importedObjective.Title}\" requires a title.");

					var step = await CreateStepAsync(
						objective.Id,
						importedStep.Title,
						importedStep.Context ?? "",
						importedStep.Assignee ?? "",
						importedStep.Assigner ?? "orchestrator",
						Array.Empty<Guid>(),
						actor);

					if (!string.IsNullOrWhiteSpace(importedStep.Key))
					{
						if (!stepsByKey.TryAdd(importedStep.Key, step))
							throw new InvalidOperationException(
								$"Duplicate step key in import: {importedStep.Key}");
					}

					stepResults.Add(new ImportPlanStepResult(
						importedStep.Key,
						step.Id,
						step.StepNumber,
						$"#{objective.Alias}.{step.StepNumber}"));
				}

				objectiveResults.Add(new ImportPlanObjectiveResult(
					importedObjective.Key,
					objective.Id,
					objective.Alias,
					stepResults));
			}

			// Resolve dependencies after all steps exist so forward references work.
			foreach (var importedObjective in request.Objectives)
			{
				foreach (var importedStep in importedObjective.Steps ?? [])
				{
					if (importedStep.DependsOn is null || importedStep.DependsOn.Count == 0)
						continue;

					if (string.IsNullOrWhiteSpace(importedStep.Key) ||
						!stepsByKey.TryGetValue(importedStep.Key, out var step))
					{
						throw new InvalidOperationException(
							$"Step \"{importedStep.Title}\" has dependencies but no import key.");
					}

					var dependencyIds = new List<Guid>();
					foreach (var dependencyKey in importedStep.DependsOn)
					{
						if (!stepsByKey.TryGetValue(dependencyKey, out var dependency))
							throw new InvalidOperationException(
								$"Unknown dependency key \"{dependencyKey}\" on step \"{importedStep.Title}\".");

						dependencyIds.Add(dependency.Id);
					}

					await UpdateStepAsync(
						step.Id,
						new StepPatch(),
						dependencyIds,
						actor);
				}
			}

			await RecordEventAsync(
				campaign.Id,
				actor,
				ActivityEntityType.Campaign,
				campaign.Id,
				"ImportedPlan",
				$"Imported plan with {objectiveResults.Count} objective(s)",
				new
				{
					campaign_id = campaign.Id,
					objectives = objectiveResults.Count,
					steps = objectiveResults.Sum(o => o.Steps.Count),
				});

			await transaction.CommitAsync();
			return new ImportPlanResult(campaign.Id, objectiveResults);
		}
		catch
		{
			await transaction.RollbackAsync();
			throw;
		}
	}
}
