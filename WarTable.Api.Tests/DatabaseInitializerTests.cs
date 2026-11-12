using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WarTable.Api.Data;
using WarTable.Api.Models;

namespace WarTable.Api.Tests;

public sealed class DatabaseInitializerTests
{
	private static string TempDbPath() =>
		Path.Combine(Path.GetTempPath(), $"wartable-test-{Guid.NewGuid():N}.db");

	private static WarTableDbContext Open(string path) =>
		new(new DbContextOptionsBuilder<WarTableDbContext>()
			.UseSqlite($"Data Source={path}")
			.Options);

	/// <summary>
	/// The initial migration is what adopts every database written before
	/// migrations existed, so its shape must be identical to what EnsureCreated
	/// produced. A difference here silently breaks the baseline.
	/// </summary>
	[Fact]
	public async Task Migrated_schema_matches_the_model()
	{
		var migratedPath = TempDbPath();
		var modelPath = TempDbPath();

		try
		{
			await using (var migrated = Open(migratedPath))
				await DatabaseInitializer.InitializeAsync(migrated);

			await using (var model = Open(modelPath))
				await model.Database.EnsureCreatedAsync();

			Assert.Equal(await ReadShapeAsync(modelPath), await ReadShapeAsync(migratedPath));
		}
		finally
		{
			DeleteDatabase(migratedPath);
			DeleteDatabase(modelPath);
		}
	}

	[Fact]
	public async Task Adopts_a_database_written_before_migrations()
	{
		var path = TempDbPath();

		try
		{
			// Simulate v1.0.1: EnsureCreated shape (pre-note-shapes), stamped version, no history.
			await using (var legacy = Open(path))
			{
				await legacy.Database.EnsureCreatedAsync();
				// The current EnsureCreated (which lacks a Status column because it
				// is now [NotMapped]) did not create it. Prior schema versions had it,
				// so add it back to simulate a real legacy database.
				await legacy.Database.ExecuteSqlRawAsync(
					"ALTER TABLE \"BunkerNotes\" ADD COLUMN \"Status\" TEXT NOT NULL DEFAULT 'Active';");
				// EnsureCreated creates the new index (without Status); replace it with
				// the legacy index so the migration's DropIndex finds it.
				await legacy.Database.ExecuteSqlRawAsync(
					"DROP INDEX IF EXISTS \"IX_BunkerNotes_CampaignId_Kind\";");
				await legacy.Database.ExecuteSqlRawAsync(
					"CREATE INDEX \"IX_BunkerNotes_CampaignId_Kind_Status\" ON \"BunkerNotes\" (\"CampaignId\", \"Kind\", \"Status\");");
				await legacy.Database.ExecuteSqlRawAsync(
					"ALTER TABLE \"BunkerNotes\" DROP COLUMN \"ApprovedAt\";");
				await legacy.Database.ExecuteSqlRawAsync(
					"ALTER TABLE \"BunkerNotes\" DROP COLUMN \"Basis\";");
				await legacy.Database.ExecuteSqlRawAsync(
					"ALTER TABLE \"Campaigns\" DROP COLUMN \"Charter\";");
				// Use raw SQL because the Campaign entity expects the Charter column.
				await legacy.Database.ExecuteSqlRawAsync(
					"INSERT INTO \"Campaigns\" (\"Id\", \"Title\", \"Intent\", \"Tags\", \"CreatedAt\", \"UpdatedAt\", \"NextAliasNumber\") " +
					"VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6);",
					[Guid.NewGuid(), "Survives", "", "", DateTime.UtcNow, DateTime.UtcNow, 1]);
				await legacy.Database.ExecuteSqlRawAsync("PRAGMA user_version = 2;");
			}

			await using (var upgraded = Open(path))
			{
				await DatabaseInitializer.InitializeAsync(upgraded);

				Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
				Assert.Equal("Survives", (await upgraded.Campaigns.SingleAsync()).Title);
			}

			// Adoption records the migration and creates a backup for the upgrade.
			Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, $"{Path.GetFileName(path)}.bak-*"));
			Assert.Equal(DatabaseInitializer.SchemaVersion, await ReadUserVersionAsync(path));
		}
		finally
		{
			DeleteDatabase(path);
		}
	}

	[Fact]
	public async Task Recreates_a_database_older_than_the_baseline()
	{
		var path = TempDbPath();

		try
		{
			await using (var ancient = Open(path))
			{
				await ancient.Database.EnsureCreatedAsync();
				ancient.Campaigns.Add(new Campaign { Title = "Doomed" });
				await ancient.SaveChangesAsync();
				await ancient.Database.ExecuteSqlRawAsync("PRAGMA user_version = 1;");
			}

			await using (var recreated = Open(path))
			{
				await DatabaseInitializer.InitializeAsync(recreated);

				Assert.Empty(await recreated.Campaigns.ToListAsync());
				Assert.Empty(await recreated.Database.GetPendingMigrationsAsync());
			}

			Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, $"{Path.GetFileName(path)}.bak-*"));
			Assert.Equal(DatabaseInitializer.SchemaVersion, await ReadUserVersionAsync(path));
		}
		finally
		{
			DeleteDatabase(path);
		}
	}

	[Fact]
	public async Task Reset_backs_up_before_deleting()
	{
		var path = TempDbPath();

		try
		{
			await using (var db = Open(path))
			{
				await DatabaseInitializer.InitializeAsync(db);
				db.Campaigns.Add(new Campaign { Title = "Discarded" });
				await db.SaveChangesAsync();
			}

			await using (var db = Open(path))
			{
				await DatabaseInitializer.InitializeAsync(db, reset: true);

				Assert.Empty(await db.Campaigns.ToListAsync());
				Assert.Empty(await db.Database.GetPendingMigrationsAsync());
			}

			Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, $"{Path.GetFileName(path)}.bak-*"));
		}
		finally
		{
			DeleteDatabase(path);
		}
	}

	/// <summary>
	/// Table and index definitions for everything the application owns, in a
	/// stable order. Minor whitespace differences between the two DDL emitters
	/// are collapsed so only shape differences show up.
	/// </summary>
	private static async Task<string> ReadShapeAsync(string path)
	{
		await using var connection = new SqliteConnection($"Data Source={path}");
		await connection.OpenAsync();

		await using var command = connection.CreateCommand();
		command.CommandText =
			"""
			SELECT type, name, sql FROM sqlite_master
			WHERE sql IS NOT NULL
			  AND name NOT LIKE 'sqlite_%'
			  AND name NOT LIKE '%EFMigrations%'
			ORDER BY type, name;
			""";

		var rows = new List<string>();
		await using var reader = await command.ExecuteReaderAsync();
		while (await reader.ReadAsync())
		{
			var sql = reader.GetString(2);
			// Normalise column order within CREATE TABLE so migration-added columns
			// (which appear at the end) compare equal to model-order columns.
			sql = NormaliseColumnOrder(sql);
			rows.Add($"{reader.GetString(0)}|{reader.GetString(1)}|{Regex.Replace(sql, @"\s+", " ").Trim()}");
		}

		return string.Join("\n", rows);
	}

	/// <summary>
	/// Sorts column definitions alphabetically within a CREATE TABLE so that
	/// column order differences between migrations-add and EnsureCreated do
	/// not cause a false mismatch.
	/// </summary>
	private static string NormaliseColumnOrder(string sql)
	{
		if (!sql.StartsWith("CREATE TABLE ", StringComparison.OrdinalIgnoreCase))
			return sql;

		// Find the column-list parenthesised block.
		var parenStart = sql.IndexOf('(');
		if (parenStart < 0)
			return sql;

		var depth = 0;
		var parenEnd = -1;
		for (var i = parenStart; i < sql.Length; i++)
		{
			if (sql[i] == '(') depth++;
			else if (sql[i] == ')') { depth--; if (depth == 0) { parenEnd = i; break; } }
		}
		if (parenEnd < 0)
			return sql;

		var prefix = sql[..parenStart];
		var body = sql[(parenStart + 1)..parenEnd];
		var suffix = sql[(parenEnd + 1)..];

		// Split on top-level commas (not inside nested parens).
		var parts = SplitTopLevelCommas(body);
		if (parts.Length < 3)
			return sql; // too simple to bother

		// Keep constraints (PRIMARY KEY, FOREIGN KEY, UNIQUE) in original order;
		// sort only column definitions (lines with a data type).
		var constraints = parts.Where(p => !p.Contains(' ', StringComparison.Ordinal)).ToList();
		var columnDefs = parts.Where(p => p.Contains(' ', StringComparison.Ordinal)).ToList();

		// Sort column names ignoring case, but keep id columns first for determinism.
		columnDefs.Sort((a, b) =>
		{
			var aName = a.Split(' ', 2, StringSplitOptions.TrimEntries)[0].Trim('"');
			var bName = b.Split(' ', 2, StringSplitOptions.TrimEntries)[0].Trim('"');
			return string.Compare(aName, bName, StringComparison.OrdinalIgnoreCase);
		});

		var reordered = string.Join(", ", columnDefs.Concat(constraints));
		return $"{prefix}({reordered}){suffix}";
	}

	private static string[] SplitTopLevelCommas(string text)
	{
		var parts = new List<string>();
		var depth = 0;
		var start = 0;
		for (var i = 0; i < text.Length; i++)
		{
			if (text[i] == '(') depth++;
			else if (text[i] == ')') depth--;
			else if (text[i] == ',' && depth == 0)
			{
				parts.Add(text[start..i].Trim());
				start = i + 1;
			}
		}
		parts.Add(text[start..].Trim());
		return [.. parts];
	}

	private static async Task<int> ReadUserVersionAsync(string path)
	{
		await using var connection = new SqliteConnection($"Data Source={path}");
		await connection.OpenAsync();

		await using var command = connection.CreateCommand();
		command.CommandText = "PRAGMA user_version;";
		return Convert.ToInt32(await command.ExecuteScalarAsync());
	}

	private static void DeleteDatabase(string path)
	{
		SqliteConnection.ClearAllPools();

		foreach (var file in Directory.GetFiles(Path.GetDirectoryName(path)!, $"{Path.GetFileName(path)}*"))
			File.Delete(file);
	}
}
