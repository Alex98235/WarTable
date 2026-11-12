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
			// Simulate v1.0.1: EnsureCreated shape, stamped version, no history.
			await using (var legacy = Open(path))
			{
				await legacy.Database.EnsureCreatedAsync();
				legacy.Campaigns.Add(new Campaign { Title = "Survives" });
				await legacy.SaveChangesAsync();
				await legacy.Database.ExecuteSqlRawAsync("PRAGMA user_version = 2;");
			}

			await using (var upgraded = Open(path))
			{
				await DatabaseInitializer.InitializeAsync(upgraded);

				Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
				Assert.Equal("Survives", (await upgraded.Campaigns.SingleAsync()).Title);
			}

			// Adoption records the migration; it does not rewrite the file.
			Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, $"{Path.GetFileName(path)}.bak-*"));
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
			rows.Add($"{reader.GetString(0)}|{reader.GetString(1)}|{Regex.Replace(reader.GetString(2), @"\s+", " ").Trim()}");

		return string.Join("\n", rows);
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
