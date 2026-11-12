using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace WarTable.Api.Data;

/// <summary>
/// Applies EF migrations to the SQLite database and stamps the schema version.
///
/// Databases written before migrations existed carry no migration history. A
/// database whose stamped version is covered by <c>InitialCreate</c> is adopted:
/// the initial migration is recorded as already applied and later migrations run
/// normally. A database older than that baseline has no path forward and is
/// backed up and recreated rather than used with the wrong shape.
/// </summary>
public static class DatabaseInitializer
{
	/// <summary>
	/// Schema generation stamped into <c>PRAGMA user_version</c>. Bump when the
	/// EF model changes so release notes can flag it; migrations move the data,
	/// this number only describes the shape.
	/// </summary>
	public const int SchemaVersion = 2;

	/// <summary>
	/// Schema generation reproduced by the initial migration — the last shape
	/// <c>EnsureCreated</c> produced. A history-less database at or above this
	/// version can be adopted instead of recreated.
	/// </summary>
	public const int BaselineSchemaVersion = 2;

	public static void Initialize(WarTableDbContext db)
		=> InitializeAsync(db).GetAwaiter().GetResult();

	public static async Task InitializeAsync(WarTableDbContext db, bool reset = false)
	{
		var path = GetDatabasePath(db);
		var exists = path is not null && File.Exists(path);

		if (exists && reset && path is not null)
		{
			var backup = await BackupDatabaseAsync(db, path);
			Console.Error.WriteLine(
				$"[WarTable] --reset-db: backed up {path} to {backup} and deleting the database.");
			await db.Database.EnsureDeletedAsync();
			exists = false;
		}

		// A database created by EnsureCreated has tables but no migration
		// history. Adopt it when the initial migration covers its shape.
		if (exists && path is not null &&
			await HasUserTablesAsync(db) &&
			!await HasMigrationHistoryAsync(db))
		{
			var version = await ReadSchemaVersionAsync(db);
			if (version >= BaselineSchemaVersion)
			{
				Console.Error.WriteLine(
					$"[WarTable] Adopting database at schema version {version}: no migration history " +
					"found, recording the initial migration as applied.");
				await BaselineAtInitialMigrationAsync(db);
			}
			else
			{
				var backup = await BackupDatabaseAsync(db, path);
				Console.Error.WriteLine(
					$"[WarTable] Database schema version {version} predates migrations " +
					$"(baseline {BaselineSchemaVersion}). Backed up to {backup} and recreating the database.");
				await db.Database.EnsureDeletedAsync();
				exists = false;
			}
		}

		string? preMigrationBackup = null;
		if (exists && path is not null)
		{
			var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
			if (pending.Count > 0)
			{
				preMigrationBackup = await BackupDatabaseAsync(db, path);
				Console.Error.WriteLine(
					$"[WarTable] Applying {pending.Count} migration(s): {string.Join(", ", pending)}. " +
					$"Backup: {preMigrationBackup}");
			}
		}

		try
		{
			await db.Database.MigrateAsync();
		}
		catch (Exception ex)
		{
			if (preMigrationBackup is not null && path is not null)
			{
				await RestoreDatabaseAsync(db, path, preMigrationBackup);
				Console.Error.WriteLine(
					$"[WarTable] Migration failed ({ex.Message}). " +
					$"Restored the pre-migration database from {preMigrationBackup}.");
			}

			throw;
		}

		await EnableWalAsync(db);
		await WriteSchemaVersionAsync(db);

		if (!exists)
			Console.Error.WriteLine(
				$"[WarTable] Created database at {path ?? "(in-memory)"} (schema version {SchemaVersion}).");
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
	/// Records the initial migration as applied without running it, so later
	/// migrations start from a shape that already exists on disk.
	/// </summary>
	private static async Task BaselineAtInitialMigrationAsync(WarTableDbContext db)
	{
		var initial = db.Database.GetMigrations().First();
		await db.Database.ExecuteSqlRawAsync(
			"""
			CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
			    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
			    "ProductVersion" TEXT NOT NULL
			);
			""");

		await db.Database.ExecuteSqlRawAsync(
			"""INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ({0}, {1});""",
			initial,
			ProductInfo.GetVersion());
	}

	private static async Task<bool> HasMigrationHistoryAsync(WarTableDbContext db)
	{
		var count = await ScalarAsync(db,
			"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory';");
		return count > 0;
	}

	/// <summary>
	/// Whether the file holds any table other than SQLite's own bookkeeping.
	/// A pre-created but empty file is treated as new, not as legacy.
	/// </summary>
	private static async Task<bool> HasUserTablesAsync(WarTableDbContext db)
	{
		var count = await ScalarAsync(db,
			"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' " +
			"AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory';");
		return count > 0;
	}

	private static async Task<int> ScalarAsync(WarTableDbContext db, string sql)
	{
		var connection = db.Database.GetDbConnection();
		var openedHere = connection.State != ConnectionState.Open;
		if (openedHere)
			await connection.OpenAsync();

		try
		{
			await using var command = connection.CreateCommand();
			command.CommandText = sql;
			var result = await command.ExecuteScalarAsync();
			return Convert.ToInt32(result);
		}
		finally
		{
			if (openedHere)
				await connection.CloseAsync();
		}
	}

	/// <summary>
	/// Copies the database file before a migration run. The WAL is checkpointed
	/// first so the copy contains committed rows that still live in the -wal file.
	/// </summary>
	private static async Task<string> BackupDatabaseAsync(WarTableDbContext db, string path)
	{
		try
		{
			await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);");
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[WarTable] Could not checkpoint before backup: {ex.Message}");
		}

		await db.Database.CloseConnectionAsync();

		var backupPath = $"{path}.bak-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}";
		File.Copy(path, backupPath, overwrite: true);
		return backupPath;
	}

	/// <summary>
	/// Puts the pre-migration file back after a failed migration. The -wal and
	/// -shm sidecars are removed first so the restored file is authoritative.
	/// </summary>
	private static async Task RestoreDatabaseAsync(WarTableDbContext db, string path, string backupPath)
	{
		await db.Database.CloseConnectionAsync();

		// Pooled connections keep the file handle open; releasing them lets the
		// restore replace the file on every platform.
		SqliteConnection.ClearAllPools();

		foreach (var sidecar in new[] { path, $"{path}-wal", $"{path}-shm" })
		{
			if (!File.Exists(sidecar))
				continue;

			File.Delete(sidecar);
		}

		File.Copy(backupPath, path, overwrite: true);
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
		=> await ScalarAsync(db, "PRAGMA user_version;");

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
