using WarTable.Api.Models;
using WarTable.Api.Services;

namespace WarTable.Api.Tests;

/// <summary>
/// Integration tests for derived note dormancy against a real in-memory database:
/// the live-link id sets are computed by the service from the actual state of
/// objectives and steps.
/// </summary>
public class NoteDormancyServiceTests
{
	private const string Human = "human";

	private static async Task AgeNoteAsync(TestContext ctx, BunkerNote note, int days)
	{
		note.UpdatedAt = DateTime.UtcNow.AddDays(-days);
		await ctx.Db.SaveChangesAsync();
	}

	// Mirrors GET /api/notes: default (includeDormant=false) excludes dormant,
	// includeDormant=true includes them marked with Dormant and DormantDays.
	[Fact]
	public async Task GetNotes_ExcludesDormantByDefault_AndIncludesMarkedWithIncludeDormant()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync("C", "", "", Human);
		var objective = await ctx.Service.CreateObjectiveAsync(
			campaign.Id, "O", "", PlanState.Active, Human);

		var live = await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Gotcha, "Live gotcha", "Body", "", false, Human,
			new[] { objective.Id }, Array.Empty<Guid>(), null);
		var dormant = await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Context, "Dormant context", "Stale body", "", false, Human,
			Array.Empty<Guid>(), Array.Empty<Guid>(), null);
		await AgeNoteAsync(ctx, dormant, 40);

		var excluded = await ctx.Service.GetNotesAsync(
			campaign.Id, null, null, null, includeDormant: false);
		Assert.Contains(excluded, n => n.Id == live.Id);
		Assert.DoesNotContain(excluded, n => n.Id == dormant.Id);

		var included = await ctx.Service.GetNotesAsync(
			campaign.Id, null, null, null, includeDormant: true);
		var marked = Assert.Single(included, n => n.Id == dormant.Id);
		Assert.True(marked.Dormant);
		Assert.True(marked.DormantDays >= 30);
		Assert.False(Assert.Single(included, n => n.Id == live.Id).Dormant);
	}

	[Fact]
	public async Task OldNoteWithLiveObjectiveLink_StaysLive()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync("C", "", "", Human);
		var objective = await ctx.Service.CreateObjectiveAsync(
			campaign.Id, "O", "", PlanState.Active, Human);

		var note = await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Gotcha, "Old but linked", "Body", "", false, Human,
			new[] { objective.Id }, Array.Empty<Guid>(), null);
		await AgeNoteAsync(ctx, note, 40);

		var included = await ctx.Service.GetNotesAsync(
			campaign.Id, null, null, null, includeDormant: true);
		Assert.False(Assert.Single(included, n => n.Id == note.Id).Dormant);
	}

	[Fact]
	public async Task LinkToCompletedObjective_IsNotLive()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync("C", "", "", Human);
		var objective = await ctx.Service.CreateObjectiveAsync(
			campaign.Id, "O", "", PlanState.Active, Human);
		var step = await ctx.Service.CreateStepAsync(
			objective.Id, "S", "", "", "orchestrator", Array.Empty<Guid>(), Human);
		await ctx.Service.UpdateStepStatusAsync(step.Id, StepStatuses.Done, Human);
		Assert.Equal(
			ObjectiveStatuses.Completed,
			(await ctx.Service.GetObjectiveAsync(objective.Id))!.Status);

		var note = await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Gotcha, "Old linked gotcha", "Body", "", false, Human,
			new[] { objective.Id }, Array.Empty<Guid>(), null);
		await AgeNoteAsync(ctx, note, 40);

		var included = await ctx.Service.GetNotesAsync(
			campaign.Id, null, null, null, includeDormant: true);
		Assert.True(Assert.Single(included, n => n.Id == note.Id).Dormant);
	}

	[Fact]
	public async Task LinkToDoneStep_IsNotLive()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync("C", "", "", Human);
		var objective = await ctx.Service.CreateObjectiveAsync(
			campaign.Id, "O", "", PlanState.Active, Human);
		var step = await ctx.Service.CreateStepAsync(
			objective.Id, "S", "", "", "orchestrator", Array.Empty<Guid>(), Human);
		await ctx.Service.UpdateStepStatusAsync(step.Id, StepStatuses.Done, Human);

		var note = await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Gotcha, "Old linked gotcha", "Body", "", false, Human,
			Array.Empty<Guid>(), new[] { step.Id }, null);
		await AgeNoteAsync(ctx, note, 40);

		var included = await ctx.Service.GetNotesAsync(
			campaign.Id, null, null, null, includeDormant: true);
		Assert.True(Assert.Single(included, n => n.Id == note.Id).Dormant);
	}

	[Fact]
	public async Task LinkToSoftDeletedObjective_IsNotLive()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync("C", "", "", Human);
		var objective = await ctx.Service.CreateObjectiveAsync(
			campaign.Id, "O", "", PlanState.Active, Human);

		var note = await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Reference, "Old reference", "Body", "", false, Human,
			new[] { objective.Id }, Array.Empty<Guid>(), null);
		await AgeNoteAsync(ctx, note, 40);

		await ctx.Service.DeleteObjectiveAsync(objective.Id, Human);

		var included = await ctx.Service.GetNotesAsync(
			campaign.Id, null, null, null, includeDormant: true);
		Assert.True(Assert.Single(included, n => n.Id == note.Id).Dormant);
	}

	[Fact]
	public async Task GetNoteByAlias_And_GetNote_ReturnDormantNotes_Unchanged()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync("C", "", "", Human);
		var dormant = await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Context, "Dormant", "Body", "", false, Human,
			Array.Empty<Guid>(), Array.Empty<Guid>(), null);
		await AgeNoteAsync(ctx, dormant, 40);

		// by-alias and by-id must not filter dormant notes; they are returned
		// unchanged (no dormancy marking computed on this path).
		var byAlias = await ctx.Service.GetNoteByAliasAsync(campaign.Id, dormant.Alias);
		Assert.NotNull(byAlias);
		Assert.Equal(dormant.Id, byAlias!.Id);
		Assert.False(byAlias.Dormant);

		var byId = await ctx.Service.GetNoteAsync(dormant.Id);
		Assert.NotNull(byId);
		Assert.Equal(dormant.Id, byId!.Id);
	}
}
