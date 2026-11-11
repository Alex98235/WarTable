using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WarTable.Api.Data;
using WarTable.Api.Models;
using WarTable.Api.Services;

namespace WarTable.Api.Tests;

public sealed class TestContext : IAsyncDisposable
{
	private readonly SqliteConnection _connection;

	public WarTableDbContext Db { get; }
	public WarTableService Service { get; }

	private TestContext(SqliteConnection connection, WarTableDbContext db)
	{
		_connection = connection;
		Db = db;
		Service = new WarTableService(db);
	}

	public static async Task<TestContext> CreateAsync()
	{
		var connection = new SqliteConnection("Data Source=:memory:");
		await connection.OpenAsync();

		var options = new DbContextOptionsBuilder<WarTableDbContext>()
			.UseSqlite(connection)
			.Options;

		var db = new WarTableDbContext(options);
		await DatabaseInitializer.InitializeAsync(db);

		return new TestContext(connection, db);
	}

	public async ValueTask DisposeAsync()
	{
		await Db.DisposeAsync();
		await _connection.DisposeAsync();
	}
}

public class WarTableServiceTests
{
	private const string Human = "human";

	private static async Task<(TestContext Ctx, Campaign Campaign, Objective Objective)> ArrangeAsync(
		PlanState planState = PlanState.Active)
	{
		var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync("Test Campaign", "", "test", Human);
		var objective = await ctx.Service.CreateObjectiveAsync(
			campaign.Id, "Test Objective", "Description", planState, Human);
		return (ctx, campaign, objective);
	}

	[Fact]
	public async Task DerivedStatus_FollowsStepPriority()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync("C", "", "", Human);
		var objective = await ctx.Service.CreateObjectiveAsync(
			campaign.Id, "O", "", PlanState.Active, Human);

		var a = await ctx.Service.CreateStepAsync(
			objective.Id, "A", "", "", "orchestrator", Array.Empty<Guid>(), Human);
		var b = await ctx.Service.CreateStepAsync(
			objective.Id, "B", "", "", "orchestrator", Array.Empty<Guid>(), Human);

		Assert.Equal(ObjectiveStatuses.Planned, (await ctx.Service.GetObjectiveAsync(objective.Id))!.Status);

		await ctx.Service.UpdateStepStatusAsync(a.Id, StepStatuses.InProgress, Human);
		Assert.Equal(ObjectiveStatuses.InProgress, (await ctx.Service.GetObjectiveAsync(objective.Id))!.Status);

		await ctx.Service.UpdateStepStatusAsync(a.Id, StepStatuses.Done, Human);
		await ctx.Service.UpdateStepStatusAsync(b.Id, StepStatuses.Done, Human);
		Assert.Equal(ObjectiveStatuses.Completed, (await ctx.Service.GetObjectiveAsync(objective.Id))!.Status);

		await ctx.Service.UpdateStepStatusAsync(b.Id, StepStatuses.Blocked, Human);
		Assert.Equal(ObjectiveStatuses.Blocked, (await ctx.Service.GetObjectiveAsync(objective.Id))!.Status);

		await ctx.Service.UpdateStepStatusAsync(b.Id, StepStatuses.Flagged, Human);
		Assert.Equal(ObjectiveStatuses.NeedsReview, (await ctx.Service.GetObjectiveAsync(objective.Id))!.Status);
	}

	[Fact]
	public async Task Dependencies_RejectCyclesAndSelfDependencies()
	{
		var (ctx, _, objective) = await ArrangeAsync();
		await using var _ctx = ctx;

		var a = await ctx.Service.CreateStepAsync(
			objective.Id, "A", "", "", "orchestrator", Array.Empty<Guid>(), Human);
		var b = await ctx.Service.CreateStepAsync(
			objective.Id, "B", "", "", "orchestrator", Array.Empty<Guid>(), Human);

		await ctx.Service.UpdateStepAsync(a.Id, new StepPatch(), new[] { b.Id }, Human);

		var cycle = await Assert.ThrowsAsync<InvalidOperationException>(
			() => ctx.Service.UpdateStepAsync(b.Id, new StepPatch(), new[] { a.Id }, Human));
		Assert.Contains("cycle", cycle.Message, StringComparison.OrdinalIgnoreCase);

		var self = await Assert.ThrowsAsync<InvalidOperationException>(
			() => ctx.Service.UpdateStepAsync(a.Id, new StepPatch(), new[] { a.Id }, Human));
		Assert.Contains("itself", self.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task ReadySet_ExcludesDraftsAndWaitsForDependencies()
	{
		var (ctx, campaign, objective) = await ArrangeAsync(PlanState.Draft);
		await using var _ctx = ctx;

		var a = await ctx.Service.CreateStepAsync(
			objective.Id, "A", "", "", "orchestrator", Array.Empty<Guid>(), Human);

		var briefing = await ctx.Service.GetBriefingAsync(campaign.Id);
		Assert.Empty(briefing.ReadySteps);

		await ctx.Service.UpdateObjectiveAsync(
			objective.Id, new ObjectivePatch(PlanState: PlanState.Active), Human);

		briefing = await ctx.Service.GetBriefingAsync(campaign.Id);
		Assert.Contains(briefing.ReadySteps, s => s.Id == a.Id);
		Assert.Equal("#1.1", briefing.ReadySteps.Single(s => s.Id == a.Id).Ref);

		var b = await ctx.Service.CreateStepAsync(
			objective.Id, "B", "", "", "orchestrator", Array.Empty<Guid>(), Human);

		await ctx.Service.UpdateStepAsync(a.Id, new StepPatch(), new[] { b.Id }, Human);

		briefing = await ctx.Service.GetBriefingAsync(campaign.Id);
		Assert.DoesNotContain(briefing.ReadySteps, s => s.Id == a.Id);
		Assert.Contains(briefing.ReadySteps, s => s.Id == b.Id);

		await ctx.Service.UpdateStepStatusAsync(b.Id, StepStatuses.Done, Human);

		briefing = await ctx.Service.GetBriefingAsync(campaign.Id);
		Assert.Contains(briefing.ReadySteps, s => s.Id == a.Id);
		Assert.DoesNotContain(briefing.ReadySteps, s => s.Id == b.Id);
	}

	[Fact]
	public async Task Aliases_AreSharedAndSoftDeletedObjectivesResolve()
	{
		var (ctx, campaign, objective) = await ArrangeAsync();
		await using var _ctx = ctx;

		var note = await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Decision, "Note", "Body", "", false, Human,
			Array.Empty<Guid>(), Array.Empty<Guid>(), null);

		Assert.Equal(1, objective.Alias);
		Assert.Equal(2, note.Alias);

		var objectiveResolution = await ctx.Service.ResolveAliasAsync(campaign.Id, 1);
		Assert.NotNull(objectiveResolution);
		Assert.Equal("Objective", objectiveResolution!.Kind);
		Assert.False(objectiveResolution.Deleted);

		var noteResolution = await ctx.Service.ResolveAliasAsync(campaign.Id, 2);
		Assert.NotNull(noteResolution);
		Assert.Equal("Note", noteResolution!.Kind);

		await ctx.Service.DeleteObjectiveAsync(objective.Id, Human);

		var deletedResolution = await ctx.Service.ResolveAliasAsync(campaign.Id, 1);
		Assert.NotNull(deletedResolution);
		Assert.True(deletedResolution!.Deleted);

		var next = await ctx.Service.CreateObjectiveAsync(
			campaign.Id, "Next", "", PlanState.Draft, Human);
		Assert.Equal(3, next.Alias);
	}

	[Fact]
	public async Task CreateCampaign_WithCharter_CreatesCharterNote()
	{
		await using var ctx = await TestContext.CreateAsync();

		var campaign = await ctx.Service.CreateCampaignAsync(
			"Chartered", "# Why\n\nBecause.", "tag", Human);

		var charter = Assert.Single(await ctx.Service.GetNotesAsync(
			campaign.Id, null, BunkerNoteKind.Charter, null));
		Assert.Equal("Charter", charter.Title);
		Assert.Equal("# Why\n\nBecause.", charter.Body);
		Assert.Equal(1, charter.Alias);
		Assert.Equal(1, campaign.CharterAlias);
		Assert.Equal("# Why\n\nBecause.", campaign.CharterExcerpt);

		var listed = Assert.Single(await ctx.Service.GetCampaignsAsync());
		Assert.Equal("# Why\n\nBecause.", listed.CharterExcerpt);
		Assert.Equal(1, listed.CharterAlias);
	}

	[Fact]
	public async Task CreateNote_RejectsSecondCharter()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync("C", "First charter", "", Human);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			ctx.Service.CreateNoteAsync(
				campaign.Id,
				BunkerNoteKind.Charter,
				"Another charter",
				"Second",
				"",
				false,
				Human,
				Array.Empty<Guid>(),
				Array.Empty<Guid>(),
				null));

		Assert.Contains("already has a charter", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task Briefing_IncludesCharterAndDesignDocs()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync(
			"Framed", "Charter body", "", Human);

		await ctx.Service.CreateNoteAsync(
			campaign.Id,
			BunkerNoteKind.DesignDoc,
			"Index lifecycle",
			"Design body",
			"search, index",
			false,
			Human,
			Array.Empty<Guid>(),
			Array.Empty<Guid>(),
			null);

		var briefing = await ctx.Service.GetBriefingAsync(campaign.Id);

		Assert.NotNull(briefing.Charter);
		Assert.Equal("Charter", briefing.Charter!.Title);
		Assert.Equal("Charter body", briefing.Charter.Excerpt);
		Assert.Equal(1, briefing.Campaign.CharterAlias);
		Assert.Equal("Charter body", briefing.Campaign.CharterExcerpt);
		var designDoc = Assert.Single(briefing.DesignDocs);
		Assert.Equal("Index lifecycle", designDoc.Title);
		Assert.Equal(BunkerNoteKind.DesignDoc, designDoc.Kind);
	}

	[Fact]
	public async Task ImportPlan_IsAtomicOnUnknownDependency()
	{
		await using var ctx = await TestContext.CreateAsync();

		var request = new ImportPlanRequest(
			Campaign: new ImportPlanCampaign("Atomic", "", "tag"),
			Objectives:
			[
				new ImportPlanObjective(
					Key: "objective",
					Title: "Objective",
					Steps:
					[
						new ImportPlanStep(
							Key: "step",
							Title: "Step",
							DependsOn: ["missing"]),
					]),
			]);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => ctx.Service.ImportPlanAsync(request, Human));

		Assert.Contains("Unknown dependency", exception.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Empty(await ctx.Service.GetCampaignsAsync());
		Assert.Empty(await ctx.Service.GetObjectivesAsync());
	}

	[Fact]
	public async Task ImportPlan_ResolvesForwardReferencesAndReturnsRefs()
	{
		await using var ctx = await TestContext.CreateAsync();

		var request = new ImportPlanRequest(
			Campaign: new ImportPlanCampaign("Imported", "", "tag"),
			Objectives:
			[
				new ImportPlanObjective(
					Key: "objective",
					Title: "Objective",
					PlanState: PlanState.Active,
					Steps:
					[
						new ImportPlanStep(Key: "second", Title: "Second", DependsOn: ["first"]),
						new ImportPlanStep(Key: "first", Title: "First"),
					]),
			]);

		var result = await ctx.Service.ImportPlanAsync(request, Human);

		Assert.Single(result.Objectives);
		var objective = result.Objectives[0];
		Assert.Equal("objective", objective.Key);
		Assert.Equal(1, objective.Alias);
		Assert.Equal(2, objective.Steps.Count);
		Assert.Equal("#1.1", objective.Steps[0].Ref);
		Assert.Equal("#1.2", objective.Steps[1].Ref);

		var plan = await ctx.Service.GetPlanAsync(result.CampaignId);
		var steps = plan.Single().Steps;
		var second = steps.Single(s => s.StepNumber == 1);
		var first = steps.Single(s => s.StepNumber == 2);
		Assert.Contains(second.DependsOn, d => d.StepId == first.Id);
	}

	[Fact]
	public async Task Briefing_CapsLargeCollections()
	{
		var (ctx, campaign, objective) = await ArrangeAsync();
		await using var _ctx = ctx;

		for (var i = 0; i < 25; i++)
		{
			await ctx.Service.CreateStepAsync(
				objective.Id, $"Step {i}", "", "", "orchestrator", Array.Empty<Guid>(), Human);
		}

		for (var i = 0; i < 15; i++)
		{
			await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Decision, $"Decision {i}", "Body", "", false, Human,
				Array.Empty<Guid>(), Array.Empty<Guid>(), null);
		}

		for (var i = 0; i < 25; i++)
			await ctx.Service.CreatePreferenceAsync($"Preference {i}", "", "", Human);

		var briefing = await ctx.Service.GetBriefingAsync(campaign.Id);

		Assert.Equal(20, briefing.ReadySteps.Count);
		Assert.Equal(10, briefing.RecentDecisions.Count);
		Assert.Equal(20, briefing.Preferences.Count);
		Assert.Equal(20, briefing.RecentActivity.Count);
	}

	[Fact]
	public async Task StepNumbers_AreStableAcrossReorderAndDeletion()
	{
		var (ctx, campaign, objective) = await ArrangeAsync();
		await using var _ctx = ctx;

		var a = await ctx.Service.CreateStepAsync(
			objective.Id, "A", "", "", "orchestrator", Array.Empty<Guid>(), Human);
		var b = await ctx.Service.CreateStepAsync(
			objective.Id, "B", "", "", "orchestrator", Array.Empty<Guid>(), Human);
		var c = await ctx.Service.CreateStepAsync(
			objective.Id, "C", "", "", "orchestrator", Array.Empty<Guid>(), Human);

		Assert.Equal(1, a.StepNumber);
		Assert.Equal(2, b.StepNumber);
		Assert.Equal(3, c.StepNumber);

		await ctx.Service.ReorderStepsAsync(
			objective.Id,
			new Dictionary<Guid, int> { [a.Id] = 2, [b.Id] = 0, [c.Id] = 1 },
			Human);

		Assert.Equal(1, (await ctx.Service.GetStepAsync(a.Id))!.StepNumber);
		Assert.Equal(2, (await ctx.Service.GetStepAsync(b.Id))!.StepNumber);

		var deleted = await ctx.Service.DeleteStepAsync(b.Id, false, Human);
		Assert.True(deleted.Deleted);
		Assert.Equal(0, deleted.RemovedDependencies);

		var d = await ctx.Service.CreateStepAsync(
			objective.Id, "D", "", "", "orchestrator", Array.Empty<Guid>(), Human);

		Assert.Equal(4, d.StepNumber);
		var byRef = await ctx.Service.GetStepByRefAsync(campaign.Id, objective.Alias, 1);
		Assert.NotNull(byRef);
		Assert.Equal(a.Id, byRef!.Id);
	}

	[Fact]
	public async Task DeleteStep_RefusesToBreakDependentsUnlessForced()
	{
		var (ctx, _, objective) = await ArrangeAsync();
		await using var _ctx = ctx;

		var a = await ctx.Service.CreateStepAsync(
			objective.Id, "A", "", "", "orchestrator", Array.Empty<Guid>(), Human);
		var b = await ctx.Service.CreateStepAsync(
			objective.Id, "B", "", "", "orchestrator", Array.Empty<Guid>(), Human);
		await ctx.Service.UpdateStepAsync(b.Id, new StepPatch(), new[] { a.Id }, Human);

		var error = await Assert.ThrowsAsync<InvalidOperationException>(
			() => ctx.Service.DeleteStepAsync(a.Id, false, Human));
		Assert.Contains($"#{objective.Alias}.{a.StepNumber}", error.Message);
		Assert.Contains($"#{objective.Alias}.{b.StepNumber}", error.Message);
		Assert.Contains("force", error.Message, StringComparison.OrdinalIgnoreCase);
		Assert.NotNull(await ctx.Service.GetStepAsync(a.Id));

		var result = await ctx.Service.DeleteStepAsync(a.Id, true, Human);
		Assert.True(result.Deleted);
		Assert.Equal(1, result.RemovedDependencies);
		Assert.Null(await ctx.Service.GetStepAsync(a.Id));

		var remaining = await ctx.Service.GetStepAsync(b.Id);
		Assert.NotNull(remaining);
		Assert.Empty(remaining!.DependsOn);
	}

	[Fact]
	public async Task Seed_IsVisibleButNotActionable_UntilDrafted()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync("C", "", "", Human);
		var seed = await ctx.Service.CreateObjectiveAsync(
			campaign.Id, "Seed Thought", "Rough idea", PlanState.Seed, Human);
		await ctx.Service.CreateStepAsync(
			seed.Id, "Open step", "", "", "orchestrator", Array.Empty<Guid>(), Human);

		var briefing = await ctx.Service.GetBriefingAsync(campaign.Id);
		Assert.Empty(briefing.ReadySteps);
		var briefingSeed = Assert.Single(briefing.Seeds);
		Assert.Equal(seed.Alias, briefingSeed.Alias);
		Assert.Equal("Seed Thought", briefingSeed.Title);

		var attention = await ctx.Service.GetAttentionAsync(campaign.Id);
		var attentionSeed = Assert.Single(attention.SeedObjectives);
		Assert.Equal("SeedObjective", attentionSeed.Kind);
		Assert.Equal(seed.Id, attentionSeed.EntityId);
		Assert.Empty(attention.DraftObjectives);

		await ctx.Service.UpdateObjectiveAsync(
			seed.Id, new ObjectivePatch(PlanState: PlanState.Draft), Human);

		briefing = await ctx.Service.GetBriefingAsync(campaign.Id);
		attention = await ctx.Service.GetAttentionAsync(campaign.Id);
		Assert.Empty(briefing.Seeds);
		Assert.Empty(briefing.ReadySteps);
		var draftItem = Assert.Single(attention.DraftObjectives);
		Assert.Equal(seed.Id, draftItem.EntityId);
		Assert.Empty(attention.SeedObjectives);
	}
}
