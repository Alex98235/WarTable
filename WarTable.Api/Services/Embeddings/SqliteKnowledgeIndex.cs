using System.Globalization;
using Microsoft.Data.Sqlite;

namespace WarTable.Api.Services.Embeddings;

public sealed record IndexedChunkInfo(string ContentHash, string Model, string Profile, int Dimensions);

public sealed record StoredKnowledgeChunk(
	Guid SourceId,
	Guid CampaignId,
	int ChunkIndex,
	string HeadingPath,
	string Text,
	string ContentHash,
	string Model,
	string Profile,
	int Dimensions,
	float[] Vector,
	DateTime UpdatedAt);

public sealed record KnowledgeIndexState(
	string Model,
	string Profile,
	int Dimensions,
	string Fingerprint,
	DateTime UpdatedAt);

/// <summary>
/// Disposable, derived index. It lives beside the canonical database so the
/// main schema never depends on the embedding provider. Swapping this for a
/// vector database later should not change search or indexing code.
/// </summary>
public interface IKnowledgeIndex
{
	Task InitializeAsync(CancellationToken cancellationToken = default);
	Task<KnowledgeIndexState?> GetStateAsync(CancellationToken cancellationToken = default);
	Task SetStateAsync(KnowledgeIndexState state, CancellationToken cancellationToken = default);
	Task<Dictionary<Guid, List<IndexedChunkInfo>>> GetIndexedSourcesAsync(
		Guid? campaignId,
		CancellationToken cancellationToken = default);
	Task<List<StoredKnowledgeChunk>> GetChunksAsync(
		Guid? campaignId,
		CancellationToken cancellationToken = default);
	Task ReplaceSourceAsync(
		Guid sourceId,
		Guid campaignId,
		IReadOnlyList<StoredKnowledgeChunk> chunks,
		CancellationToken cancellationToken = default);
	Task DeleteSourceAsync(Guid sourceId, CancellationToken cancellationToken = default);
	Task ClearAsync(CancellationToken cancellationToken = default);
	Task<(int Chunks, int Sources)> GetCountsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Plain SQLite sidecar. Brute-force scoring is intentional at this scale.</summary>
public sealed class SqliteKnowledgeIndex : IKnowledgeIndex
{
	private const string SourceType = "note";
	private readonly string _connectionString;
	private readonly SemaphoreSlim _initLock = new(1, 1);
	private bool _initialized;

	public SqliteKnowledgeIndex(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
			throw new ArgumentException("A sidecar database path is required.", nameof(path));

		_connectionString = new SqliteConnectionStringBuilder
		{
			DataSource = path,
			Mode = SqliteOpenMode.ReadWriteCreate,
			Cache = SqliteCacheMode.Shared,
			DefaultTimeout = 5,
		}.ToString();
	}

	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		if (_initialized)
			return;

		await _initLock.WaitAsync(cancellationToken);
		try
		{
			if (_initialized)
				return;

			var builder = new SqliteConnectionStringBuilder(_connectionString);
			var directory = Path.GetDirectoryName(builder.DataSource);
			if (!string.IsNullOrWhiteSpace(directory))
				Directory.CreateDirectory(directory);

			await using var connection = new SqliteConnection(_connectionString);
			await connection.OpenAsync(cancellationToken);
			await using var command = connection.CreateCommand();
			command.CommandText = """
				PRAGMA journal_mode = WAL;
				PRAGMA busy_timeout = 5000;

				CREATE TABLE IF NOT EXISTS knowledge_chunks (
					id TEXT PRIMARY KEY,
					source_type TEXT NOT NULL,
					source_id TEXT NOT NULL,
					campaign_id TEXT NOT NULL,
					chunk_index INTEGER NOT NULL,
					heading_path TEXT NOT NULL,
					text TEXT NOT NULL,
					content_hash TEXT NOT NULL,
					model TEXT NOT NULL,
					profile TEXT NOT NULL,
					dimensions INTEGER NOT NULL,
					vector BLOB NOT NULL,
					updated_at TEXT NOT NULL
				);

				CREATE INDEX IF NOT EXISTS ix_knowledge_chunks_campaign
					ON knowledge_chunks (campaign_id);
				CREATE INDEX IF NOT EXISTS ix_knowledge_chunks_source
					ON knowledge_chunks (source_type, source_id);

				CREATE TABLE IF NOT EXISTS knowledge_index_state (
					id INTEGER PRIMARY KEY CHECK (id = 1),
					model TEXT NOT NULL,
					profile TEXT NOT NULL,
					dimensions INTEGER NOT NULL,
					fingerprint TEXT NOT NULL,
					updated_at TEXT NOT NULL
				);
				""";
			await command.ExecuteNonQueryAsync(cancellationToken);
			_initialized = true;
		}
		finally
		{
			_initLock.Release();
		}
	}

	public async Task<KnowledgeIndexState?> GetStateAsync(CancellationToken cancellationToken = default)
	{
		await InitializeAsync(cancellationToken);
		await using var connection = await OpenAsync(cancellationToken);
		await using var command = connection.CreateCommand();
		command.CommandText = """
			SELECT model, profile, dimensions, fingerprint, updated_at
			FROM knowledge_index_state
			WHERE id = 1
			""";

		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		if (!await reader.ReadAsync(cancellationToken))
			return null;

		return new KnowledgeIndexState(
			reader.GetString(0),
			reader.GetString(1),
			reader.GetInt32(2),
			reader.GetString(3),
			ParseDate(reader.GetString(4)));
	}

	public async Task SetStateAsync(
		KnowledgeIndexState state,
		CancellationToken cancellationToken = default)
	{
		await InitializeAsync(cancellationToken);
		await using var connection = await OpenAsync(cancellationToken);
		await using var command = connection.CreateCommand();
		command.CommandText = """
			INSERT INTO knowledge_index_state
				(id, model, profile, dimensions, fingerprint, updated_at)
			VALUES
				(1, $model, $profile, $dimensions, $fingerprint, $updatedAt)
			ON CONFLICT(id) DO UPDATE SET
				model = excluded.model,
				profile = excluded.profile,
				dimensions = excluded.dimensions,
				fingerprint = excluded.fingerprint,
				updated_at = excluded.updated_at
			""";
		command.Parameters.AddWithValue("$model", state.Model);
		command.Parameters.AddWithValue("$profile", state.Profile);
		command.Parameters.AddWithValue("$dimensions", state.Dimensions);
		command.Parameters.AddWithValue("$fingerprint", state.Fingerprint);
		command.Parameters.AddWithValue("$updatedAt", FormatDate(state.UpdatedAt));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<Dictionary<Guid, List<IndexedChunkInfo>>> GetIndexedSourcesAsync(
		Guid? campaignId,
		CancellationToken cancellationToken = default)
	{
		await InitializeAsync(cancellationToken);
		await using var connection = await OpenAsync(cancellationToken);
		await using var command = connection.CreateCommand();
		command.CommandText = campaignId.HasValue
			? """
				SELECT source_id, content_hash, model, profile, dimensions
				FROM knowledge_chunks
				WHERE source_type = $sourceType AND campaign_id = $campaignId
				ORDER BY source_id, chunk_index
				"""
			: """
				SELECT source_id, content_hash, model, profile, dimensions
				FROM knowledge_chunks
				WHERE source_type = $sourceType
				ORDER BY source_id, chunk_index
				""";
		command.Parameters.AddWithValue("$sourceType", SourceType);
		if (campaignId.HasValue)
			command.Parameters.AddWithValue("$campaignId", campaignId.Value.ToString("D"));

		var result = new Dictionary<Guid, List<IndexedChunkInfo>>();
		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
		{
			var sourceId = Guid.Parse(reader.GetString(0));
			if (!result.TryGetValue(sourceId, out var chunks))
			{
				chunks = new List<IndexedChunkInfo>();
				result[sourceId] = chunks;
			}

			chunks.Add(new IndexedChunkInfo(
				reader.GetString(1),
				reader.GetString(2),
				reader.GetString(3),
				reader.GetInt32(4)));
		}

		return result;
	}

	public async Task<List<StoredKnowledgeChunk>> GetChunksAsync(
		Guid? campaignId,
		CancellationToken cancellationToken = default)
	{
		await InitializeAsync(cancellationToken);
		await using var connection = await OpenAsync(cancellationToken);
		await using var command = connection.CreateCommand();
		command.CommandText = campaignId.HasValue
			? """
				SELECT source_id, campaign_id, chunk_index, heading_path, text,
					content_hash, model, profile, dimensions, vector, updated_at
				FROM knowledge_chunks
				WHERE source_type = $sourceType AND campaign_id = $campaignId
				ORDER BY source_id, chunk_index
				"""
			: """
				SELECT source_id, campaign_id, chunk_index, heading_path, text,
					content_hash, model, profile, dimensions, vector, updated_at
				FROM knowledge_chunks
				WHERE source_type = $sourceType
				ORDER BY source_id, chunk_index
				""";
		command.Parameters.AddWithValue("$sourceType", SourceType);
		if (campaignId.HasValue)
			command.Parameters.AddWithValue("$campaignId", campaignId.Value.ToString("D"));

		var result = new List<StoredKnowledgeChunk>();
		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
		{
			result.Add(new StoredKnowledgeChunk(
				Guid.Parse(reader.GetString(0)),
				Guid.Parse(reader.GetString(1)),
				reader.GetInt32(2),
				reader.GetString(3),
				reader.GetString(4),
				reader.GetString(5),
				reader.GetString(6),
				reader.GetString(7),
				reader.GetInt32(8),
				FromBlob((byte[])reader[9]),
				ParseDate(reader.GetString(10))));
		}

		return result;
	}

	public async Task ReplaceSourceAsync(
		Guid sourceId,
		Guid campaignId,
		IReadOnlyList<StoredKnowledgeChunk> chunks,
		CancellationToken cancellationToken = default)
	{
		await InitializeAsync(cancellationToken);
		await using var connection = await OpenAsync(cancellationToken);
		await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

		await using (var delete = connection.CreateCommand())
		{
			delete.Transaction = (SqliteTransaction)transaction;
			delete.CommandText =
				"DELETE FROM knowledge_chunks WHERE source_type = $sourceType AND source_id = $sourceId";
			delete.Parameters.AddWithValue("$sourceType", SourceType);
			delete.Parameters.AddWithValue("$sourceId", sourceId.ToString("D"));
			await delete.ExecuteNonQueryAsync(cancellationToken);
		}

		await using (var insert = connection.CreateCommand())
		{
			insert.Transaction = (SqliteTransaction)transaction;
			insert.CommandText = """
				INSERT INTO knowledge_chunks
					(id, source_type, source_id, campaign_id, chunk_index, heading_path, text,
					 content_hash, model, profile, dimensions, vector, updated_at)
				VALUES
					($id, $sourceType, $sourceId, $campaignId, $chunkIndex, $headingPath, $text,
					 $contentHash, $model, $profile, $dimensions, $vector, $updatedAt)
				""";

			var id = insert.Parameters.Add("$id", SqliteType.Text);
			var sourceType = insert.Parameters.Add("$sourceType", SqliteType.Text);
			var source = insert.Parameters.Add("$sourceId", SqliteType.Text);
			var campaign = insert.Parameters.Add("$campaignId", SqliteType.Text);
			var chunkIndex = insert.Parameters.Add("$chunkIndex", SqliteType.Integer);
			var headingPath = insert.Parameters.Add("$headingPath", SqliteType.Text);
			var text = insert.Parameters.Add("$text", SqliteType.Text);
			var contentHash = insert.Parameters.Add("$contentHash", SqliteType.Text);
			var model = insert.Parameters.Add("$model", SqliteType.Text);
			var profile = insert.Parameters.Add("$profile", SqliteType.Text);
			var dimensions = insert.Parameters.Add("$dimensions", SqliteType.Integer);
			var vector = insert.Parameters.Add("$vector", SqliteType.Blob);
			var updatedAt = insert.Parameters.Add("$updatedAt", SqliteType.Text);

			sourceType.Value = SourceType;
			source.Value = sourceId.ToString("D");
			campaign.Value = campaignId.ToString("D");

			foreach (var chunk in chunks)
			{
				id.Value = $"{sourceId:D}:{chunk.ChunkIndex}";
				chunkIndex.Value = chunk.ChunkIndex;
				headingPath.Value = chunk.HeadingPath;
				text.Value = chunk.Text;
				contentHash.Value = chunk.ContentHash;
				model.Value = chunk.Model;
				profile.Value = chunk.Profile;
				dimensions.Value = chunk.Dimensions;
				vector.Value = ToBlob(chunk.Vector);
				updatedAt.Value = FormatDate(chunk.UpdatedAt);
				await insert.ExecuteNonQueryAsync(cancellationToken);
			}
		}

		await transaction.CommitAsync(cancellationToken);
	}

	public async Task DeleteSourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
	{
		await InitializeAsync(cancellationToken);
		await using var connection = await OpenAsync(cancellationToken);
		await using var command = connection.CreateCommand();
		command.CommandText =
			"DELETE FROM knowledge_chunks WHERE source_type = $sourceType AND source_id = $sourceId";
		command.Parameters.AddWithValue("$sourceType", SourceType);
		command.Parameters.AddWithValue("$sourceId", sourceId.ToString("D"));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task ClearAsync(CancellationToken cancellationToken = default)
	{
		await InitializeAsync(cancellationToken);
		await using var connection = await OpenAsync(cancellationToken);
		await using var command = connection.CreateCommand();
		command.CommandText = """
			DELETE FROM knowledge_chunks;
			DELETE FROM knowledge_index_state;
			""";
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<(int Chunks, int Sources)> GetCountsAsync(
		CancellationToken cancellationToken = default)
	{
		await InitializeAsync(cancellationToken);
		await using var connection = await OpenAsync(cancellationToken);
		await using var command = connection.CreateCommand();
		command.CommandText = """
			SELECT COUNT(*), COUNT(DISTINCT source_id)
			FROM knowledge_chunks
			WHERE source_type = $sourceType
			""";
		command.Parameters.AddWithValue("$sourceType", SourceType);

		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		if (!await reader.ReadAsync(cancellationToken))
			return (0, 0);
		return (reader.GetInt32(0), reader.GetInt32(1));
	}

	private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
	{
		var connection = new SqliteConnection(_connectionString);
		await connection.OpenAsync(cancellationToken);
		return connection;
	}

	private static byte[] ToBlob(float[] vector)
	{
		var bytes = new byte[vector.Length * sizeof(float)];
		Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
		return bytes;
	}

	private static float[] FromBlob(byte[] bytes)
	{
		var vector = new float[bytes.Length / sizeof(float)];
		Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
		return vector;
	}

	private static string FormatDate(DateTime value)
		=> value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

	private static DateTime ParseDate(string value)
		=> DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
