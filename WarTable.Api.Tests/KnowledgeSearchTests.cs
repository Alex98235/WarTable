using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WarTable.Api.Models;
using WarTable.Api.Options;
using WarTable.Api.Services.Embeddings;

namespace WarTable.Api.Tests;

public class EmbeddingProfileTests
{
	[Fact]
	public void ModelMatching_UsesBuiltInTemplates()
	{
		var options = new EmbeddingsOptions
		{
			Enabled = true,
			Model = "embeddinggemma:300m",
			Endpoint = "http://localhost/v1",
		};
		var provider = NewProvider(options);

		Assert.Equal("embeddinggemma", provider.Profile.Name);
		Assert.Equal("task: search result | query: hello", provider.RenderQuery("hello"));
		Assert.Equal(
			"title: My note | text: body",
			provider.RenderDocument("body", "My note"));
	}

	[Fact]
	public void ExplicitProfile_AndOverrides_Win()
	{
		var options = new EmbeddingsOptions
		{
			Enabled = true,
			Model = "custom-model",
			Endpoint = "http://localhost/v1",
			Profile = "house",
			Dimensions = 3,
			Profiles =
			[
				new EmbeddingProfileOptions
				{
					Name = "house",
					Match = "custom",
					Dimensions = 2,
					MaxInputTokens = 256,
					QueryTemplate = "Q: {text}",
					DocumentTemplate = "D: {title}\n{text}",
				},
			],
		};
		var provider = NewProvider(options);

		Assert.Equal("house", provider.Profile.Name);
		Assert.Equal(3, provider.Profile.Dimensions);
		Assert.Equal("Q: find it", provider.RenderQuery("find it"));
		Assert.Equal("D: Title\nbody", provider.RenderDocument("body", "Title"));
	}

	[Fact]
	public void UnknownModel_FallsBackToRaw()
	{
		var options = new EmbeddingsOptions
		{
			Enabled = true,
			Model = "some-new-model",
			Endpoint = "http://localhost/v1",
		};
		var provider = NewProvider(options);

		Assert.True(provider.UsedFallback);
		Assert.Equal("raw", provider.Profile.Name);
		Assert.Equal("hello", provider.RenderQuery("hello"));
	}

	private static EmbeddingProfileProvider NewProvider(EmbeddingsOptions options)
		=> new(
			Microsoft.Extensions.Options.Options.Create(options),
			NullLogger<EmbeddingProfileProvider>.Instance);
}

public class MarkdownChunkerTests
{
	[Fact]
	public void Chunk_PreservesHeadingPath()
	{
		var chunks = MarkdownChunker.Chunk(
			"Note",
			"# Alpha\nalpha body\n\n## Beta\nbeta body",
			200);

		Assert.Equal(2, chunks.Count);
		Assert.Equal("Alpha", chunks[0].HeadingPath);
		Assert.Equal("Alpha > Beta", chunks[1].HeadingPath);
		Assert.Contains("alpha body", chunks[0].Text);
		Assert.Contains("beta body", chunks[1].Text);
	}

	[Fact]
	public void Chunk_SplitsLongSections()
	{
		var body = string.Join(' ', Enumerable.Repeat("word", 400));
		var chunks = MarkdownChunker.Chunk("Note", body, 200);

		Assert.True(chunks.Count > 1);
		Assert.All(chunks, chunk => Assert.True(chunk.Text.Length <= 800));
	}

	[Fact]
	public void EmptyBody_UsesTitle()
	{
		var chunks = MarkdownChunker.Chunk("Only a title", "", 200);

		Assert.Single(chunks);
		Assert.Equal("Only a title", chunks[0].Text);
	}
}

public class KnowledgeSearchTests
{
	[Fact]
	public async Task Reconcile_IndexesNotes_AndSearchReturnsEvidence()
	{
		var path = TempPath();
		try
		{
			await using var ctx = await TestContext.CreateAsync();
			var campaign = await ctx.Service.CreateCampaignAsync("Campaign", "", "", "human");
			var alpha = await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Context, "Alpha launch",
				"Alpha is the launch codename.", "", false, "human",
				Array.Empty<Guid>(), Array.Empty<Guid>(), null);
			await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Decision, "Beta decision",
				"Beta is the fallback codename.", "", false, "human",
				Array.Empty<Guid>(), Array.Empty<Guid>(), null);

			var options = TestOptions(path);
			var profiles = new EmbeddingProfileProvider(
				Microsoft.Extensions.Options.Options.Create(options), NullLogger<EmbeddingProfileProvider>.Instance);
			var client = new FakeEmbeddingClient();
			var index = new SqliteKnowledgeIndex(path);
			var indexer = new KnowledgeIndexService(
				ctx.Db, index, client, profiles, Microsoft.Extensions.Options.Options.Create(options),
				NullLogger<KnowledgeIndexService>.Instance);

			var first = await indexer.ReconcileAsync();
			Assert.Null(first.Error);
			Assert.True(first.Chunks > 0);

			var second = await indexer.ReconcileAsync();
			Assert.Null(second.Error);
			Assert.False(second.Changed);

			var search = new KnowledgeSearchService(
				ctx.Service, index, client, profiles, Microsoft.Extensions.Options.Options.Create(options),
				NullLogger<KnowledgeSearchService>.Instance);
			var response = await search.SearchAsync(
				campaign.Id, "alpha", KnowledgeSearchMode.Hybrid);

			Assert.False(response.Degraded);
			var result = response.Results.Single(r => r.NoteId == alpha.Id);
			Assert.Contains("launch", result.Snippet, StringComparison.OrdinalIgnoreCase);
			Assert.True(result.MatchedBy is "both" or "semantic" or "keyword");
		}
		finally
		{
			DeleteSidecar(path);
		}
	}

	[Fact]
	public async Task Search_FallsBackToKeyword_WhenEmbeddingFails()
	{
		var path = TempPath();
		try
		{
			await using var ctx = await TestContext.CreateAsync();
			var campaign = await ctx.Service.CreateCampaignAsync("Campaign", "", "", "human");
			await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Gotcha, "Rate limit",
				"The API returns 429 when the token bucket is empty.", "", false, "human",
				Array.Empty<Guid>(), Array.Empty<Guid>(), null);

			var options = TestOptions(path);
			var profiles = new EmbeddingProfileProvider(
				Microsoft.Extensions.Options.Options.Create(options), NullLogger<EmbeddingProfileProvider>.Instance);
			var search = new KnowledgeSearchService(
				ctx.Service,
				new SqliteKnowledgeIndex(path),
				new ThrowingEmbeddingClient(),
				profiles,
				Microsoft.Extensions.Options.Options.Create(options),
				NullLogger<KnowledgeSearchService>.Instance);

			var response = await search.SearchAsync(
				campaign.Id, "token bucket", KnowledgeSearchMode.Semantic);

			Assert.True(response.Degraded);
			Assert.Contains(response.Results, r => r.Title == "Rate limit");
		}
		finally
		{
			DeleteSidecar(path);
		}
	}

	[Fact]
	public async Task Search_IncludesDormantNote_MarkedDormant()
	{
		var path = TempPath();
		try
		{
			await using var ctx = await TestContext.CreateAsync();
			var campaign = await ctx.Service.CreateCampaignAsync("Campaign", "", "", "human");
			var dormant = await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Context, "Stale launch note",
				"The alpha launch checklist was archived and is no longer linked.",
				"", false, "human",
				Array.Empty<Guid>(), Array.Empty<Guid>(), null);
			dormant.UpdatedAt = DateTime.UtcNow.AddDays(-40);
			await ctx.Db.SaveChangesAsync();

			var options = TestOptions(path);
			var profiles = new EmbeddingProfileProvider(
				Microsoft.Extensions.Options.Options.Create(options), NullLogger<EmbeddingProfileProvider>.Instance);
			var search = new KnowledgeSearchService(
				ctx.Service,
				new SqliteKnowledgeIndex(path),
				new ThrowingEmbeddingClient(),
				profiles,
				Microsoft.Extensions.Options.Options.Create(options),
				NullLogger<KnowledgeSearchService>.Instance);

			var response = await search.SearchAsync(
				campaign.Id, "alpha launch", KnowledgeSearchMode.Keyword);

			var result = Assert.Single(response.Results, r => r.NoteId == dormant.Id);
			Assert.True(result.Dormant);
			Assert.True(result.DormantDays >= 30);
		}
		finally
		{
			DeleteSidecar(path);
		}
	}

	[Fact]
	public async Task Reconcile_UsesActualVectorDimensions_WhenProfileHintDiffers()
	{
		var path = TempPath();
		try
		{
			await using var ctx = await TestContext.CreateAsync();
			var campaign = await ctx.Service.CreateCampaignAsync("Campaign", "", "", "human");
			await ctx.Service.CreateNoteAsync(
				campaign.Id, BunkerNoteKind.Context, "Alpha note",
				"Alpha body.", "", false, "human",
				Array.Empty<Guid>(), Array.Empty<Guid>(), null);

			var options = TestOptions(path);
			// EmbeddingGemma's built-in hint is 768, but the fake service returns
			// two dimensions. The second pass must trust the stored width.
			options.Model = "embeddinggemma:300m";
			options.Dimensions = 0;

			var profiles = new EmbeddingProfileProvider(
				Microsoft.Extensions.Options.Options.Create(options), NullLogger<EmbeddingProfileProvider>.Instance);
			var index = new SqliteKnowledgeIndex(path);
			var indexer = new KnowledgeIndexService(
				ctx.Db, index, new FakeEmbeddingClient(), profiles,
				Microsoft.Extensions.Options.Options.Create(options),
				NullLogger<KnowledgeIndexService>.Instance);

			var first = await indexer.ReconcileAsync();
			Assert.Null(first.Error);
			Assert.True(first.Changed);

			var second = await indexer.ReconcileAsync();
			Assert.Null(second.Error);
			Assert.False(second.Changed);
		}
		finally
		{
			DeleteSidecar(path);
		}
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
		MinScore = 0,
		SidecarPath = path,
	};

	private static string TempPath()
		=> Path.Combine(Path.GetTempPath(), $"wartable-embeddings-{Guid.NewGuid():N}.db");

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
					: text.Contains("beta", StringComparison.OrdinalIgnoreCase)
						? new[] { 0f, 1f }
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
