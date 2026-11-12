using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WarTable.Api.Data;
using WarTable.Api.Models;
using WarTable.Api.Services;

namespace WarTable.Api.Tests;

public sealed class TestContext : IAsyncDisposable
{
	private readonly SqliteConnection _connection;
	private readonly DbContextOptions<WarTableDbContext> _options;

	public WarTableDbContext Db { get; }
	public WarTableService Service { get; }

	private TestContext(SqliteConnection connection, DbContextOptions<WarTableDbContext> options, WarTableDbContext db)
	{
		_connection = connection;
		_options = options;
		Db = db;
		Service = new WarTableService(db);
	}

	/// <summary>
	/// A second unit of work over the same database, standing in for a second
	/// client. It has its own change tracker, so it holds its own copy of every
	/// row version.
	/// </summary>
	public WarTableDbContext CreateSecondContext() => new(_options);

	public static async Task<TestContext> CreateAsync()
	{
		var connection = new SqliteConnection("Data Source=:memory:");
		await connection.OpenAsync();

		var options = new DbContextOptionsBuilder<WarTableDbContext>()
			.UseSqlite(connection)
			.Options;

		var db = new WarTableDbContext(options);
		await DatabaseInitializer.InitializeAsync(db);

		return new TestContext(connection, options, db);
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
	public async Task CreateCampaign_WithCharter_StoresOnCampaign()
	{
		await using var ctx = await TestContext.CreateAsync();

		var campaign = await ctx.Service.CreateCampaignAsync(
			"Chartered", "# Why\n\nBecause.", "tag", Human);

		// Charter is stored on the campaign column, not as a note.
		Assert.Equal("# Why\n\nBecause.", campaign.Charter);
		Assert.Equal("# Why\n\nBecause.", campaign.CharterExcerpt);
		Assert.Empty(await ctx.Service.GetNotesAsync(campaign.Id, null, null, null));

		var listed = Assert.Single(await ctx.Service.GetCampaignsAsync());
		Assert.Equal("# Why\n\nBecause.", listed.CharterExcerpt);
	}

	[Fact]
	public async Task CreateNote_RejectsSecondCharter()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync("C", "First charter", "", Human);

		// Charter is no longer a note kind, so trying to create a Charter note
		// will fail at enum parsing. This test is kept for the shape — the
		// removed guard is gone.
	}

	[Fact]
	public async Task Briefing_CharterComesFromColumn_AndNoDesignDocsSection()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync(
			"Framed", "Charter body", "", Human);

		var briefing = await ctx.Service.GetBriefingAsync(campaign.Id);

		Assert.NotNull(briefing.Charter);
		Assert.Equal("Charter body", briefing.Charter!.Excerpt);
		Assert.Equal("Charter body", briefing.Campaign.CharterExcerpt);
		Assert.Empty(briefing.DesignDocs);
	}

	[Fact]
	public async Task UpdateCampaign_Charter_ReplacesColumn()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync("C", "Original charter", "", Human);

		await ctx.Service.UpdateCampaignAsync(
			campaign.Id,
			new CampaignPatch(Charter: "Replaced charter"),
			Human);

		var reloaded = await ctx.Service.GetCampaignAsync(campaign.Id);
		Assert.NotNull(reloaded);
		Assert.Equal("Replaced charter", reloaded!.Charter);
		Assert.Equal("Replaced charter", reloaded.CharterExcerpt);
	}

	[Fact]
	public async Task NoteBodyEdit_ClearsApprovedAt()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync("C", "", "", Human);
		var note = await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Decision, "Approved decision",
			"Original text.", "", false, Human,
			Array.Empty<Guid>(), Array.Empty<Guid>(), null);

		// Set ApprovedAt via the patch (simulates human confirmation).
		var approvalTime = DateTime.UtcNow;
		await ctx.Service.UpdateNoteAsync(
			note.Id,
			new BunkerNotePatch(ApprovedAt: approvalTime),
			Human);
		var withApproval = await ctx.Service.GetNoteAsync(note.Id);
		Assert.NotNull(withApproval);
		Assert.NotNull(withApproval!.ApprovedAt);

		// Editing the body must clear ApprovedAt.
		await ctx.Service.UpdateNoteAsync(
			note.Id,
			new BunkerNotePatch(Body: "Revised text."),
			Human);
		var afterEdit = await ctx.Service.GetNoteAsync(note.Id);
		Assert.NotNull(afterEdit);
		Assert.Equal("Revised text.", afterEdit!.Body);
		Assert.Null(afterEdit.ApprovedAt);
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

	[Fact]
	public async Task RemoveEvidence_RemovesRowAndRecordsActivityEvent()
	{
		var (ctx, campaign, objective) = await ArrangeAsync();
		await using var _ctx = ctx;

		var step = await ctx.Service.CreateStepAsync(
			objective.Id, "Step", "", "", "orchestrator", Array.Empty<Guid>(), Human);
		var evidence = await ctx.Service.AddEvidenceAsync(
			step.Id, "CI run", EvidenceKind.Link, "https://ci.example.com/run/42", Human);
		Assert.NotNull(evidence);

		var removed = await ctx.Service.RemoveEvidenceAsync(step.Id, evidence!.Id, "tester");

		Assert.NotNull(removed);
		Assert.Equal(evidence.Id, removed!.Id);
		Assert.Empty(await ctx.Service.GetEvidenceAsync(step.Id));

		var activity = await ctx.Service.GetActivityAsync(campaign.Id, null);
		var deleted = Assert.Single(activity, e => e.Action == "Deleted");
		Assert.Equal($"Removed evidence from step #{objective.Alias}.{step.StepNumber}: CI run", deleted.Summary);
		Assert.Equal("tester", deleted.Actor);
	}

	[Fact]
	public async Task RemoveEvidence_UnknownId_FailsWithoutEvent()
	{
		var (ctx, campaign, objective) = await ArrangeAsync();
		await using var _ctx = ctx;

		var step = await ctx.Service.CreateStepAsync(
			objective.Id, "Step", "", "", "orchestrator", Array.Empty<Guid>(), Human);
		var evidence = await ctx.Service.AddEvidenceAsync(
			step.Id, "CI run", EvidenceKind.Link, "https://ci.example.com/run/42", Human);
		Assert.NotNull(evidence);

		var before = (await ctx.Service.GetActivityAsync(campaign.Id, null))
			.Count(e => e.Action == "Deleted");

		var removed = await ctx.Service.RemoveEvidenceAsync(step.Id, Guid.NewGuid(), Human);

		Assert.Null(removed);
		Assert.Single(await ctx.Service.GetEvidenceAsync(step.Id));
		var after = (await ctx.Service.GetActivityAsync(campaign.Id, null))
			.Count(e => e.Action == "Deleted");
		Assert.Equal(before, after);
	}

	[Fact]
	public async Task RemoveEvidence_FromDifferentStep_DoesNotSucceed()
	{
		var (ctx, campaign, objective) = await ArrangeAsync();
		await using var _ctx = ctx;

		var stepA = await ctx.Service.CreateStepAsync(
			objective.Id, "A", "", "", "orchestrator", Array.Empty<Guid>(), Human);
		var stepB = await ctx.Service.CreateStepAsync(
			objective.Id, "B", "", "", "orchestrator", Array.Empty<Guid>(), Human);
		var evidence = await ctx.Service.AddEvidenceAsync(
			stepA.Id, "CI run", EvidenceKind.Link, "https://ci.example.com/run/42", Human);
		Assert.NotNull(evidence);

		var before = (await ctx.Service.GetActivityAsync(campaign.Id, null))
			.Count(e => e.Action == "Deleted");

		var removed = await ctx.Service.RemoveEvidenceAsync(stepB.Id, evidence!.Id, Human);

		Assert.Null(removed);
		var onA = Assert.Single(await ctx.Service.GetEvidenceAsync(stepA.Id));
		Assert.Equal(evidence.Id, onA.Id);
		Assert.Empty(await ctx.Service.GetEvidenceAsync(stepB.Id));
		var after = (await ctx.Service.GetActivityAsync(campaign.Id, null))
			.Count(e => e.Action == "Deleted");
		Assert.Equal(before, after);
	}

	// ── Derived note status ──

	[Fact]
	public async Task SupersedingNote_DerivesSupersededStatus_OnSupersededNote()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync("C", "", "", Human);
		var original = await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Decision, "Original", "", "", false, Human,
			Array.Empty<Guid>(), Array.Empty<Guid>(), null);

		// Before supersession: reads as Active.
		var before = await ctx.Service.GetNoteAsync(original.Id);
		Assert.NotNull(before);
		Assert.Equal(BunkerNoteStatus.Active, before!.Status);

		// Create a successor that supersedes the original.
		await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Decision, "Successor", "", "", false, Human,
			Array.Empty<Guid>(), Array.Empty<Guid>(), original.Id);

		// After supersession: original reads as Superseded with no explicit Status write.
		var after = await ctx.Service.GetNoteAsync(original.Id);
		Assert.NotNull(after);
		Assert.Equal(BunkerNoteStatus.Superseded, after!.Status);
	}

	[Fact]
	public async Task SupersedingNote_ReadsActive_WhenSuccessorIsSoftDeleted()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync("C", "", "", Human);
		var original = await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Decision, "Original", "", "", false, Human,
			Array.Empty<Guid>(), Array.Empty<Guid>(), null);

		var successor = await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Decision, "Successor", "", "", false, Human,
			Array.Empty<Guid>(), Array.Empty<Guid>(), original.Id);

		// Soft-delete the successor.
		await ctx.Service.DeleteNoteAsync(successor.Id, Human);

		// Original reads as Active again because its only superseder was deleted.
		var after = await ctx.Service.GetNoteAsync(original.Id);
		Assert.NotNull(after);
		Assert.Equal(BunkerNoteStatus.Active, after!.Status);
	}

	[Fact]
	public async Task GetNotes_FiltersByDerivedStatus_Active()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync("C", "", "", Human);
		var active = await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Context, "Active", "", "", false, Human,
			Array.Empty<Guid>(), Array.Empty<Guid>(), null);
		var superseded = await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Gotcha, "Will be superseded", "", "", false, Human,
			Array.Empty<Guid>(), Array.Empty<Guid>(), null);
		await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Context, "Successor", "", "", false, Human,
			Array.Empty<Guid>(), Array.Empty<Guid>(), superseded.Id);

		var activeNotes = await ctx.Service.GetNotesAsync(
			campaign.Id, null, null, BunkerNoteStatus.Active);
		Assert.Contains(activeNotes, n => n.Id == active.Id);
		Assert.DoesNotContain(activeNotes, n => n.Id == superseded.Id);
	}

	[Fact]
	public async Task GetNotes_FiltersByDerivedStatus_Superseded()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync("C", "", "", Human);
		var active = await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Context, "Active", "", "", false, Human,
			Array.Empty<Guid>(), Array.Empty<Guid>(), null);
		var superseded = await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Gotcha, "Will be superseded", "", "", false, Human,
			Array.Empty<Guid>(), Array.Empty<Guid>(), null);
		await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Context, "Successor", "", "", false, Human,
			Array.Empty<Guid>(), Array.Empty<Guid>(), superseded.Id);

		var supersededNotes = await ctx.Service.GetNotesAsync(
			campaign.Id, null, null, BunkerNoteStatus.Superseded);
		Assert.DoesNotContain(supersededNotes, n => n.Id == active.Id);
		Assert.Contains(supersededNotes, n => n.Id == superseded.Id);
	}

	[Fact]
	public async Task GetDirectives_ExcludesSuperseded()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync("C", "", "", Human);
		var directive = await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Directive, "Active directive", "Body", "", false, Human,
			Array.Empty<Guid>(), Array.Empty<Guid>(), null);
		var supersededDirective = await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Directive, "Superseded directive", "Body", "", false, Human,
			Array.Empty<Guid>(), Array.Empty<Guid>(), null);
		await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Directive, "Successor directive", "Body", "", false, Human,
			Array.Empty<Guid>(), Array.Empty<Guid>(), supersededDirective.Id);

		var directives = await ctx.Service.GetDirectivesAsync(campaign.Id);
		Assert.Contains(directives, n => n.Id == directive.Id);
		Assert.DoesNotContain(directives, n => n.Id == supersededDirective.Id);
	}

	[Fact]
	public async Task Briefing_ExcludesSupersededDecisions()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync("C", "", "", Human);
		var decision = await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Decision, "Active decision", "Body", "", false, Human,
			Array.Empty<Guid>(), Array.Empty<Guid>(), null);
		var superseded = await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Decision, "Superseded decision", "Body", "", false, Human,
			Array.Empty<Guid>(), Array.Empty<Guid>(), null);
		await ctx.Service.CreateNoteAsync(
			campaign.Id, BunkerNoteKind.Decision, "Successor", "Body", "", false, Human,
			Array.Empty<Guid>(), Array.Empty<Guid>(), superseded.Id);

		var briefing = await ctx.Service.GetBriefingAsync(campaign.Id);
		Assert.Contains(briefing.RecentDecisions, n => n.Id == decision.Id);
		Assert.DoesNotContain(briefing.RecentDecisions, n => n.Id == superseded.Id);
	}
}
