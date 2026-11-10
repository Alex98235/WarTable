using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WarTable.Api.Data;
using WarTable.Api.Models;
using WarTable.Api.Options;

namespace WarTable.Api.Services.Embeddings;

public sealed record KnowledgeIndexStatus(
	bool Enabled,
	bool Configured,
	bool Ready,
	int Sources,
	int Chunks,
	DateTime? LastIndexedAt,
	string? Model,
	string? Profile,
	int Dimensions,
	string? Error);

public sealed record KnowledgeIndexResult(
	bool Changed,
	int Sources,
	int Chunks,
	string? Error);

/// <summary>
/// Reconciles Bunker notes into the sidecar index. It only embeds notes whose
/// chunk hashes changed, so the write path stays fast and the worker is cheap.
/// </summary>
public sealed class KnowledgeIndexService
{
	private readonly WarTableDbContext _db;
	private readonly IKnowledgeIndex _index;
	private readonly IEmbeddingClient _client;
	private readonly EmbeddingProfileProvider _profiles;
	private readonly EmbeddingsOptions _options;
	private readonly ILogger<KnowledgeIndexService> _logger;

	public KnowledgeIndexService(
		WarTableDbContext db,
		IKnowledgeIndex index,
		IEmbeddingClient client,
		EmbeddingProfileProvider profiles,
		IOptions<EmbeddingsOptions> options,
		ILogger<KnowledgeIndexService> logger)
	{
		_db = db;
		_index = index;
		_client = client;
		_profiles = profiles;
		_options = options.Value;
		_logger = logger;
	}

	public async Task<KnowledgeIndexResult> ReconcileAsync(CancellationToken cancellationToken = default)
	{
		if (!_options.Enabled)
			return new KnowledgeIndexResult(false, 0, 0, null);
		if (!_profiles.IsConfigured)
			return new KnowledgeIndexResult(false, 0, 0, "Embeddings:Model and Embeddings:Endpoint are required.");

		try
		{
			await _index.InitializeAsync(cancellationToken);
			var profile = _profiles.Profile;
			var state = await _index.GetStateAsync(cancellationToken);
			if (state is not null &&
				!string.Equals(state.Fingerprint, _profiles.Fingerprint, StringComparison.Ordinal))
			{
				await _index.ClearAsync(cancellationToken);
				state = null;
			}

			var notes = await _db.BunkerNotes.AsNoTracking()
				.OrderBy(n => n.Id)
				.ToListAsync(cancellationToken);
			var indexed = await _index.GetIndexedSourcesAsync(null, cancellationToken);
			var liveIds = notes.Select(n => n.Id).ToHashSet();

			foreach (var staleId in indexed.Keys.Where(id => !liveIds.Contains(id)).ToList())
			{
				await _index.DeleteSourceAsync(staleId, cancellationToken);
				indexed.Remove(staleId);
			}

			var pending = new List<PendingNote>();
			foreach (var note in notes)
			{
				var chunks = MarkdownChunker.Chunk(note.Title, note.Body, profile.MaxInputTokens);
				if (chunks.Count == 0)
				{
					if (indexed.ContainsKey(note.Id))
						await _index.DeleteSourceAsync(note.Id, cancellationToken);
					continue;
				}

				var hashes = chunks.Select(chunk => Hash(profile, note, chunk)).ToList();
				if (IsCurrent(indexed, note.Id, hashes, profile, state))
					continue;

				pending.Add(new PendingNote(
					note,
					chunks,
					hashes,
					chunks.Select(chunk => _profiles.RenderDocument(
						chunk.Text,
						note.Title,
						note.Kind.ToString(),
						note.Tags,
						chunk.HeadingPath)).ToList()));
			}

			if (pending.Count == 0)
			{
				var current = await _index.GetCountsAsync(cancellationToken);
				return new KnowledgeIndexResult(false, current.Sources, current.Chunks, null);
			}

			var flat = pending
				.SelectMany((note, noteIndex) => note.Chunks.Select((_, chunkIndex) =>
					new EmbedItem(noteIndex, chunkIndex, note.Texts[chunkIndex])))
				.ToList();

			var vectors = new float[flat.Count][];
			var batchSize = Math.Max(1, _options.BatchSize);
			for (var offset = 0; offset < flat.Count; offset += batchSize)
			{
				var batch = flat.Skip(offset).Take(batchSize).ToList();
				var embeddings = await _client.EmbedAsync(
					batch.Select(item => item.Text).ToList(),
					cancellationToken);
				if (embeddings.Count != batch.Count)
				{
					throw new EmbeddingException(
						$"Embedding service returned {embeddings.Count} vectors for {batch.Count} inputs.");
				}

				for (var i = 0; i < batch.Count; i++)
					vectors[offset + i] = embeddings[i];
			}

			var dimensions = vectors[0].Length;
			if (dimensions == 0)
				throw new EmbeddingException("Embedding service returned an empty vector.");
			if (vectors.Any(vector => vector.Length != dimensions))
				throw new EmbeddingException("Embedding service returned inconsistent vector dimensions.");
			if (_options.Dimensions > 0 && dimensions != _options.Dimensions)
			{
				throw new EmbeddingException(
					$"Embedding service returned {dimensions} dimensions, but Embeddings:Dimensions is {_options.Dimensions}.");
			}
			if (state is not null && state.Dimensions > 0 && state.Dimensions != dimensions)
			{
				await _index.ClearAsync(cancellationToken);
				throw new EmbeddingException(
					$"Embedding dimensions changed from {state.Dimensions} to {dimensions}; " +
					"the index was cleared and will rebuild on the next pass.");
			}

			var now = DateTime.UtcNow;
			var vectorIndex = 0;
			foreach (var pendingNote in pending)
			{
				var rows = pendingNote.Chunks.Select((chunk, i) => new StoredKnowledgeChunk(
					pendingNote.Note.Id,
					pendingNote.Note.CampaignId,
					chunk.Index,
					chunk.HeadingPath,
					chunk.Text,
					pendingNote.Hashes[i],
					_options.Model,
					profile.Name,
					dimensions,
					vectors[vectorIndex + i],
					now)).ToList();

				await _index.ReplaceSourceAsync(
					pendingNote.Note.Id,
					pendingNote.Note.CampaignId,
					rows,
					cancellationToken);
				vectorIndex += pendingNote.Chunks.Count;
			}

			var counts = await _index.GetCountsAsync(cancellationToken);
			await _index.SetStateAsync(
				new KnowledgeIndexState(
					_options.Model,
					profile.Name,
					dimensions,
					_profiles.Fingerprint,
					now),
				cancellationToken);

			return new KnowledgeIndexResult(true, counts.Sources, counts.Chunks, null);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Knowledge index reconciliation failed.");
			return new KnowledgeIndexResult(false, 0, 0, ex.Message);
		}
	}

	public async Task<KnowledgeIndexStatus> GetStatusAsync(CancellationToken cancellationToken = default)
	{
		if (!_options.Enabled)
		{
			return new KnowledgeIndexStatus(
				false, false, false, 0, 0, null, null, null, 0, null);
		}

		try
		{
			await _index.InitializeAsync(cancellationToken);
			var counts = await _index.GetCountsAsync(cancellationToken);
			var state = await _index.GetStateAsync(cancellationToken);
			return new KnowledgeIndexStatus(
				true,
				_profiles.IsConfigured,
				counts.Chunks > 0 &&
					state is not null &&
					string.Equals(state.Fingerprint, _profiles.Fingerprint, StringComparison.Ordinal),
				counts.Sources,
				counts.Chunks,
				state?.UpdatedAt,
				_options.Model,
				_profiles.Profile.Name,
				state?.Dimensions ?? _profiles.Profile.Dimensions,
				null);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Could not read knowledge index status.");
			return new KnowledgeIndexStatus(
				true, _profiles.IsConfigured, false, 0, 0, null, _options.Model,
				_profiles.Profile.Name, 0, ex.Message);
		}
	}

	private bool IsCurrent(
		IReadOnlyDictionary<Guid, List<IndexedChunkInfo>> indexed,
		Guid sourceId,
		IReadOnlyList<string> hashes,
		EmbeddingProfile profile,
		KnowledgeIndexState? state)
	{
		if (!indexed.TryGetValue(sourceId, out var existing) || existing.Count != hashes.Count)
			return false;

		// The first successful pass stores the dimensions the service actually
		// returned. Prefer that over the profile hint: model variants (for
		// example text-embedding-3-large) can return a different width than the
		// built-in default without any other setting changing.
		var expectedDimensions = state?.Dimensions ?? (profile.Dimensions > 0 ? profile.Dimensions : 0);
		for (var i = 0; i < existing.Count; i++)
		{
			var chunk = existing[i];
			if (!string.Equals(chunk.ContentHash, hashes[i], StringComparison.Ordinal))
				return false;
			if (!string.Equals(chunk.Model, _options.Model, StringComparison.Ordinal))
				return false;
			if (!string.Equals(chunk.Profile, profile.Name, StringComparison.Ordinal))
				return false;
			if (expectedDimensions > 0 && chunk.Dimensions != expectedDimensions)
				return false;
		}

		return true;
	}

	private string Hash(EmbeddingProfile profile, BunkerNote note, ChunkDraft chunk)
	{
		var material = string.Join('\n',
			_profiles.Fingerprint,
			note.Title,
			note.Kind.ToString(),
			note.Tags,
			chunk.HeadingPath,
			chunk.Text);
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
	}

	private sealed record PendingNote(
		BunkerNote Note,
		IReadOnlyList<ChunkDraft> Chunks,
		List<string> Hashes,
		List<string> Texts);

	private sealed record EmbedItem(int NoteIndex, int ChunkIndex, string Text);
}

/// <summary>Periodically reconciles notes; failures are logged and retried on the next poll.</summary>
public sealed class KnowledgeIndexWorker : BackgroundService
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly EmbeddingsOptions _options;
	private readonly ILogger<KnowledgeIndexWorker> _logger;

	public KnowledgeIndexWorker(
		IServiceScopeFactory scopeFactory,
		IOptions<EmbeddingsOptions> options,
		ILogger<KnowledgeIndexWorker> logger)
	{
		_scopeFactory = scopeFactory;
		_options = options.Value;
		_logger = logger;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (!_options.Enabled)
			return;

		await Task.Yield();
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				await using var scope = _scopeFactory.CreateAsyncScope();
				var service = scope.ServiceProvider.GetRequiredService<KnowledgeIndexService>();
				await service.ReconcileAsync(stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Knowledge index worker pass failed.");
			}

			try
			{
				await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _options.PollSeconds)), stoppingToken);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}
}
