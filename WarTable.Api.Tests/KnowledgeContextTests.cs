using Microsoft.Extensions.Logging.Abstractions;
using WarTable.Api.Models;
using WarTable.Api.Options;
using WarTable.Api.Services;
using WarTable.Api.Services.Embeddings;

namespace WarTable.Api.Tests;

public class KnowledgeContextTests
{
	private const string Human = "human";

	[Fact]
	public async Task StepContext_IncludesLinkedTaggedAndSemanticNotes_AndFiltersSuperseded()
	{
		var path = TempPath();
		try
		{
			await using var ctx = await TestContext.CreateAsync();
			var campaign = await ctx.Service.CreateCampaignAsync("Campaign", "", "release", Human);
			var objective = await ctx.Service.CreateObjectiveAsync(
				campaign.Id, "Ship the release", "Coordinate the launch steps.", PlanState.Ready, Human);
			var step = await ctx.Service.CreateStepAsync(
				objective.Id, "Alpha deployment", "Wire the alpha deployment pipeline.",
				"", "orchestrator", Array.Empty<Guid>(), Human);

			var gotcha = await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Gotcha, "Alpha queue limit",
				"Alpha deploys can stall when the queue depth exceeds the worker budget. " +
				"This body is deliberately long so the returned excerpt must be bounded and not " +
				"a full body. " + new string('x', 600),
				"deploy", false, Human,
				Array.Empty<Guid>(), new[] { step.Id }, null);

			var decision = await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Decision, "Chosen rollout",
				"Alpha uses the blue-green rollout chosen during planning.",
				"", false, Human,
				new[] { objective.Id }, Array.Empty<Guid>(), null);

			var reference = await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Context, "Alpha deployment runbook",
				"The alpha deployment runbook documents the release checklist and rollback.",
				"", false, Human,
				Array.Empty<Guid>(), Array.Empty<Guid>(), null);

			var superseded = await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Decision, "Old alpha plan",
				"Alpha was previously deployed by hand. This note is superseded.",
				"", false, Human,
				new[] { objective.Id }, Array.Empty<Guid>(), null);
			// Create a successor note that supersedes the old one — status is derived.
			await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Decision, "Superseding note",
				"This note replaces the old alpha plan.", "", false, Human,
				new[] { objective.Id }, Array.Empty<Guid>(), superseded.Id);

			var options = TestOptions(path);
			var client = new FakeEmbeddingClient();
			var context = await BuildContextAsync(ctx, options, client, reconcile: true);

			var result = await context.GetForStepAsync(campaign.Id, step);

			Assert.Equal(campaign.Id, result.CampaignId);
			Assert.Equal(objective.Id, result.ObjectiveId);
			Assert.Equal(step.Id, result.StepId);
			Assert.True(result.SemanticUsed);
			Assert.False(result.Degraded);

			var notes = result.Groups.SelectMany(group => group.Notes).ToList();

			var gotchaNote = notes.Single(note => note.NoteId == gotcha.Id);
			Assert.Contains("linked-step", gotchaNote.MatchedBy);

			var decisionNote = notes.Single(note => note.NoteId == decision.Id);
			Assert.Contains("linked-objective", decisionNote.MatchedBy);

			var referenceNote = notes.Single(note => note.NoteId == reference.Id);
			Assert.Contains("semantic", referenceNote.MatchedBy);

			Assert.DoesNotContain(notes, note => note.NoteId == superseded.Id);
			Assert.All(notes, note =>
			{
				Assert.False(string.IsNullOrWhiteSpace(note.Excerpt));
				Assert.True(note.Excerpt.Length <= 240);
			});

			// Full bodies are intentionally absent from the context read model.
			Assert.Null(typeof(KnowledgeContextNote).GetProperty("Body"));
		}
		finally
		{
			DeleteSidecar(path);
		}
	}

	[Fact]
	public async Task StepContext_AlwaysIncludesTheCampaignCharter()
	{
		var path = TempPath();
		try
		{
			await using var ctx = await TestContext.CreateAsync();
			var campaign = await ctx.Service.CreateCampaignAsync(
				"Campaign", "The durable why and scope.", "", Human);
			var objective = await ctx.Service.CreateObjectiveAsync(
				campaign.Id, "Objective", "", PlanState.Ready, Human);
			var step = await ctx.Service.CreateStepAsync(
				objective.Id, "Step", "", "", "orchestrator", Array.Empty<Guid>(), Human);

			// Charter is now on the campaign column, not a note. The context
			// no longer carries a charter group; the briefing does.
			var options = TestOptions(path);
			options.Enabled = false;
			var context = await BuildContextAsync(ctx, options, new ThrowingEmbeddingClient(), reconcile: false);

			var result = await context.GetForStepAsync(campaign.Id, step);

			// No charter group exists — it was removed from GroupOrder.
			Assert.DoesNotContain(result.Groups, group => group.Kind == BunkerNoteKind.Context);
		}
		finally
		{
			DeleteSidecar(path);
		}
	}

	[Fact]
	public async Task PerKindQuota_ReportsTotalAndOmitted()
	{
		var path = TempPath();
		try
		{
			await using var ctx = await TestContext.CreateAsync();
			var campaign = await ctx.Service.CreateCampaignAsync("Campaign", "", "", Human);
			var objective = await ctx.Service.CreateObjectiveAsync(
				campaign.Id, "Objective", "", PlanState.Ready, Human);
			var step = await ctx.Service.CreateStepAsync(
				objective.Id, "Quota probe", "", "", "orchestrator", Array.Empty<Guid>(), Human);

			for (var i = 0; i < 7; i++)
			{
				await ctx.Service.CreateNoteAsync(
					campaign.Id, BunkerNoteKind.Gotcha, $"Gotcha {i}",
					$"Body {i}.", "", false, Human,
					Array.Empty<Guid>(), new[] { step.Id }, null);
			}

			var options = TestOptions(path);
			options.Enabled = false;
			var context = await BuildContextAsync(ctx, options, new ThrowingEmbeddingClient(), reconcile: false);

			var result = await context.GetForStepAsync(campaign.Id, step);

			var group = result.Groups.Single(item => item.Kind == BunkerNoteKind.Gotcha);
			Assert.Equal(7, group.Total);
			Assert.Equal(5, group.Notes.Count);
			Assert.Equal(2, group.Omitted);
		}
		finally
		{
			DeleteSidecar(path);
		}
	}

	[Fact]
	public async Task StepContext_ExcludesDormantNote_AndReportsItInGroupTotals()
	{
		var path = TempPath();
		try
		{
			await using var ctx = await TestContext.CreateAsync();
			var campaign = await ctx.Service.CreateCampaignAsync("Campaign", "", "", Human);
			var objective = await ctx.Service.CreateObjectiveAsync(
				campaign.Id, "Objective", "", PlanState.Ready, Human);
			var step = await ctx.Service.CreateStepAsync(
				objective.Id, "Alpha step", "", "", "orchestrator", Array.Empty<Guid>(), Human);

			// A gotcha linked to the step, aged past the dormancy threshold; then
			// the step is done, so its link is no longer live and the note is dormant.
			var gotcha = await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Gotcha, "Alpha trap",
				"Unrelated body that does not match the step title needle.",
				"", false, Human,
				Array.Empty<Guid>(), new[] { step.Id }, null);
			gotcha.UpdatedAt = DateTime.UtcNow.AddDays(-40);
			await ctx.Db.SaveChangesAsync();
			await ctx.Service.UpdateStepStatusAsync(step.Id, StepStatuses.Done, Human);

			var options = TestOptions(path);
			options.Enabled = false;
			var context = await BuildContextAsync(ctx, options, new ThrowingEmbeddingClient(), reconcile: false);

			var result = await context.GetForStepAsync(campaign.Id, step);

			// The dormant note is excluded from the excerpt list but its omission is
			// reported in the group totals.
			var gotchaGroup = result.Groups.Single(group => group.Kind == BunkerNoteKind.Gotcha);
			Assert.Equal(1, gotchaGroup.Total);
			Assert.Equal(1, gotchaGroup.Omitted);
			Assert.Empty(gotchaGroup.Notes);

			Assert.DoesNotContain(
				result.Groups.SelectMany(group => group.Notes),
				note => note.NoteId == gotcha.Id);
		}
		finally
		{
			DeleteSidecar(path);
		}
	}

	[Fact]
	public async Task DisabledEmbeddings_DegradeToLinkedAndDirectiveNotes()
	{
		var path = TempPath();
		try
		{
			await using var ctx = await TestContext.CreateAsync();
			var campaign = await ctx.Service.CreateCampaignAsync("Campaign", "", "", Human);
			var objective = await ctx.Service.CreateObjectiveAsync(
				campaign.Id, "Objective", "", PlanState.Ready, Human);
			var step = await ctx.Service.CreateStepAsync(
				objective.Id, "Probe", "", "", "orchestrator", Array.Empty<Guid>(), Human);

			var directive = await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Directive, "Always test",
				"Run the suite before reporting.", "", false, Human,
				Array.Empty<Guid>(), Array.Empty<Guid>(), null);

			var gotcha = await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Gotcha, "Linked gotcha",
				"Known trap.", "", false, Human,
				Array.Empty<Guid>(), new[] { step.Id }, null);

			// Matches the step-title keyword needle even with embeddings disabled.
			var keywordNote = await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Context, "Probe checklist",
				"Probe run notes.", "", false, Human,
				Array.Empty<Guid>(), Array.Empty<Guid>(), null);

			var options = TestOptions(path);
			options.Enabled = false;
			var context = await BuildContextAsync(ctx, options, new ThrowingEmbeddingClient(), reconcile: false);

			var result = await context.GetForStepAsync(campaign.Id, step);

			Assert.True(result.Degraded);
			Assert.False(result.SemanticUsed);
			Assert.False(string.IsNullOrWhiteSpace(result.DegradationReason));

			var notes = result.Groups.SelectMany(group => group.Notes).ToList();
			Assert.Contains(notes, note => note.NoteId == directive.Id &&
				note.MatchedBy.Contains("directive"));
			Assert.Contains(notes, note => note.NoteId == gotcha.Id &&
				note.MatchedBy.Contains("linked-step"));
			Assert.Contains(notes, note => note.NoteId == keywordNote.Id &&
				note.MatchedBy.Contains("keyword"));
		}
		finally
		{
			DeleteSidecar(path);
		}
	}

	[Fact]
	public async Task CountsForObjectives_UsesOnlyExplicitLinks_AndReturnsZeroForUnlinked()
	{
		var path = TempPath();
		try
		{
			await using var ctx = await TestContext.CreateAsync();
			var campaign = await ctx.Service.CreateCampaignAsync("Campaign", "", "", Human);
			var linked = await ctx.Service.CreateObjectiveAsync(
				campaign.Id, "Linked", "", PlanState.Ready, Human);
			var unlinked = await ctx.Service.CreateObjectiveAsync(
				campaign.Id, "Unlinked", "", PlanState.Ready, Human);
			var linkedStep = await ctx.Service.CreateStepAsync(
				linked.Id, "Linked step", "", "", "orchestrator", Array.Empty<Guid>(), Human);

			await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Decision, "D1", "", "", false, Human,
				new[] { linked.Id }, Array.Empty<Guid>(), null);
			await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Gotcha, "G1", "", "", false, Human,
				new[] { linked.Id }, Array.Empty<Guid>(), null);
			await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Gotcha, "G2 on step", "", "", false, Human,
				Array.Empty<Guid>(), new[] { linkedStep.Id }, null);
			await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Context, "R1", "", "", false, Human,
				new[] { linked.Id }, Array.Empty<Guid>(), null);
			await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Context, "C1", "", "", false, Human,
				new[] { linked.Id }, Array.Empty<Guid>(), null);
			await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Context, "Q1", "", "", false, Human,
				new[] { linked.Id }, Array.Empty<Guid>(), null);
			await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Gotcha, "Unlinked gotcha", "", "", false, Human,
				Array.Empty<Guid>(), Array.Empty<Guid>(), null);

			var options = TestOptions(path);
			options.Enabled = false;
			var context = await BuildContextAsync(ctx, options, new ThrowingEmbeddingClient(), reconcile: false);

			var counts = await context.GetCountsForObjectivesAsync(
				campaign.Id, new[] { linked, unlinked });

			var linkedCounts = counts[linked.Id];
			Assert.Equal(1, linkedCounts.Decisions);
			Assert.Equal(2, linkedCounts.Gotchas);
			Assert.Equal(3, linkedCounts.Context);
			Assert.Equal(6, linkedCounts.Total);

			var unlinkedCounts = counts[unlinked.Id];
			Assert.Equal(0, unlinkedCounts.Total);
			Assert.Equal(new KnowledgeCounts(0, 0, 0), unlinkedCounts);
		}
		finally
		{
			DeleteSidecar(path);
		}
	}

	[Fact]
	public async Task GetContext_SupportsQueryOnlyAndTagOnly_AndRejectsEmptySelector()
	{
		var path = TempPath();
		try
		{
			await using var ctx = await TestContext.CreateAsync();
			var campaign = await ctx.Service.CreateCampaignAsync("Campaign", "", "", Human);
			var otherCampaign = await ctx.Service.CreateCampaignAsync("Other", "", "", Human);
			var otherObjective = await ctx.Service.CreateObjectiveAsync(
				otherCampaign.Id, "Other objective", "", PlanState.Ready, Human);

			var alpha = await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Context, "Alpha note",
				"Alpha integration details.", "", false, Human,
				Array.Empty<Guid>(), Array.Empty<Guid>(), null);
			var tagged = await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Context, "Release checklist",
				"Checks for every release.", "release,ops", false, Human,
				Array.Empty<Guid>(), Array.Empty<Guid>(), null);

			var options = TestOptions(path);
			options.Enabled = false;
			var context = await BuildContextAsync(ctx, options, new ThrowingEmbeddingClient(), reconcile: false);

			var byQuery = await context.GetAsync(campaign.Id, null, null, "alpha", null);
			Assert.Null(byQuery.ObjectiveId);
			Assert.Null(byQuery.StepId);
			Assert.Contains("alpha", byQuery.Query, StringComparison.OrdinalIgnoreCase);
			Assert.Contains(
				byQuery.Groups.SelectMany(group => group.Notes),
				note => note.NoteId == alpha.Id && note.MatchedBy.Contains("keyword"));

			var byTag = await context.GetAsync(campaign.Id, null, null, null, "release");
			Assert.Contains(
				byTag.Groups.SelectMany(group => group.Notes),
				note => note.NoteId == tagged.Id && note.MatchedBy.Contains("tag"));

			await Assert.ThrowsAsync<ArgumentException>(
				() => context.GetAsync(campaign.Id, null, null, null, null));

			await Assert.ThrowsAsync<InvalidOperationException>(
				() => context.GetAsync(campaign.Id, otherObjective.Id, null, null, null));
		}
		finally
		{
			DeleteSidecar(path);
		}
	}

	private static async Task<KnowledgeContextService> BuildContextAsync(
		TestContext ctx,
		EmbeddingsOptions options,
		IEmbeddingClient client,
		bool reconcile)
	{
		var profiles = new EmbeddingProfileProvider(
			Microsoft.Extensions.Options.Options.Create(options),
			NullLogger<EmbeddingProfileProvider>.Instance);
		var index = new SqliteKnowledgeIndex(options.SidecarPath!);

		if (reconcile)
		{
			var indexer = new KnowledgeIndexService(
				ctx.Db,
				index,
				client,
				profiles,
				Microsoft.Extensions.Options.Options.Create(options),
				NullLogger<KnowledgeIndexService>.Instance);
			var result = await indexer.ReconcileAsync();
			Assert.Null(result.Error);
		}

		var search = new KnowledgeSearchService(
			ctx.Service,
			new SqliteKnowledgeIndex(options.SidecarPath!),
			client,
			profiles,
			Microsoft.Extensions.Options.Options.Create(options),
			NullLogger<KnowledgeSearchService>.Instance);

		return new KnowledgeContextService(
			ctx.Service, search, NullLogger<KnowledgeContextService>.Instance);
	}

	private static EmbeddingsOptions TestOptions(string path) => new()
	{
		Enabled = true,
		Endpoint = "http://localhost/v1",
		Model = "fake",
		Dimensions = 2,
		MaxInputTokens = 100,
		BatchSize = 2,
		TopK = 8,
		MinScore = 0.2,
		SidecarPath = path,
	};

	private static string TempPath()
		=> Path.Combine(Path.GetTempPath(), $"wartable-context-{Guid.NewGuid():N}.db");

	private static void DeleteSidecar(string path)
	{
		foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
		{
			try
			{
				if (File.Exists(candidate))
					File.Delete(candidate);
			}
			catch (IOException)
			{
				// Best-effort cleanup; the temp directory is disposable.
			}
		}
	}

	private sealed class FakeEmbeddingClient : IEmbeddingClient
	{
		public Task<IReadOnlyList<float[]>> EmbedAsync(
			IReadOnlyList<string> inputs,
			CancellationToken cancellationToken = default)
		{
			IReadOnlyList<float[]> vectors = inputs.Select(text =>
			{
				var vector = text.Contains("alpha", StringComparison.OrdinalIgnoreCase)
					? new[] { 1f, 0f }
					: new[] { 0.5f, 0.5f };
				VectorMath.NormalizeInPlace(vector);
				return vector;
			}).ToList();
			return Task.FromResult(vectors);
		}
	}

	private sealed class ThrowingEmbeddingClient : IEmbeddingClient
	{
		public Task<IReadOnlyList<float[]>> EmbedAsync(
			IReadOnlyList<string> inputs,
			CancellationToken cancellationToken = default)
			=> throw new EmbeddingException("service unavailable");
	}
}
