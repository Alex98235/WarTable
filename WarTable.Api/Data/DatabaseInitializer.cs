using System.Data;
using Microsoft.EntityFrameworkCore;

namespace WarTable.Api.Data;

/// <summary>
/// Creates and version-stamps the SQLite database. EnsureCreated does not migrate,
/// so a database written by an older schema is backed up and recreated rather than
/// silently used with the wrong shape.
/// </summary>
public static class DatabaseInitializer
{
	/// <summary>Bump when the EF model changes shape.</summary>
	public const int SchemaVersion = 2;

	public static void Initialize(WarTableDbContext db)
		=> InitializeAsync(db).GetAwaiter().GetResult();

	public static async Task InitializeAsync(WarTableDbContext db)
	{
		var path = GetDatabasePath(db);
		var exists = path is not null && File.Exists(path);

		if (exists)
		{
			var version = await ReadSchemaVersionAsync(db);
			if (version == SchemaVersion)
			{
				// Already current. EnsureCreated is a no-op when tables exist and
				// creates them when the file was pre-created but empty.
				await db.Database.EnsureCreatedAsync();
				await EnableWalAsync(db);
				return;
			}

			var backupPath = $"{path}.bak-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}";
			File.Copy(path!, backupPath, overwrite: true);
			Console.Error.WriteLine(
				$"[WarTable] Database schema version {version} does not match required {SchemaVersion}. " +
				$"Backed up to {backupPath} and recreating the database.");

			await db.Database.EnsureDeletedAsync();
		}

		await db.Database.EnsureCreatedAsync();
		await EnableWalAsync(db);
		await WriteSchemaVersionAsync(db);

		if (!exists)
			Console.Error.WriteLine($"[WarTable] Created database at {path ?? "(in-memory)"} (schema version {SchemaVersion}).");
	}

	/// <summary>Path of the SQLite file behind the context, or null for in-memory.</summary>
	private static string? GetDatabasePath(WarTableDbContext db)
	{
		var dataSource = db.Database.GetDbConnection().DataSource;
		if (string.IsNullOrWhiteSpace(dataSource) ||
			dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		return Path.GetFullPath(dataSource);
	}

	/// <summary>
	/// Switches file-backed databases to WAL. In WAL mode writes land in the
	/// <c>-wal</c> sidecar, which gives the activity broadcaster a reliable write
	/// signal (it ignores the constantly changing <c>-shm</c> file). In-memory
	/// databases are skipped. Best effort: a failure only costs us live updates,
	/// so the database is still usable.
	/// </summary>
	private static async Task EnableWalAsync(WarTableDbContext db)
	{
		if (GetDatabasePath(db) is null)
			return;

		try
		{
			await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine(
				$"[WarTable] Could not enable WAL journaling: {ex.Message}");
		}
	}

	private static async Task<int> ReadSchemaVersionAsync(WarTableDbContext db)
	{
		var connection = db.Database.GetDbConnection();
		var openedHere = connection.State != ConnectionState.Open;
		if (openedHere)
			await connection.OpenAsync();

		try
		{
			await using var command = connection.CreateCommand();
			command.CommandText = "PRAGMA user_version;";
			var result = await command.ExecuteScalarAsync();
			return Convert.ToInt32(result);
		}
		finally
		{
			if (openedHere)
				await connection.CloseAsync();
		}
	}

	private static async Task WriteSchemaVersionAsync(WarTableDbContext db)
	{
		var connection = db.Database.GetDbConnection();
		var openedHere = connection.State != ConnectionState.Open;
		if (openedHere)
			await connection.OpenAsync();

		try
		{
			await using var command = connection.CreateCommand();
			command.CommandText = $"PRAGMA user_version = {SchemaVersion};";
			await command.ExecuteNonQueryAsync();
		}
		finally
		{
			if (openedHere)
				await connection.CloseAsync();
		}
	}
}
