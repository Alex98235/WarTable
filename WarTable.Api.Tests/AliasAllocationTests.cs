using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WarTable.Api.Data;
using WarTable.Api.Models;
using WarTable.Api.Services;

namespace WarTable.Api.Tests;

/// <summary>
/// File-backed concurrency tests for campaign alias allocation. The shared
/// in-memory fixture cannot reproduce the read-modify-write race because it uses
/// one connection; these tests open independent contexts against a real SQLite
/// file, which is what the API and the stdio MCP process do in production.
/// </summary>
public sealed class AliasAllocationTests : IAsyncLifetime
{
    private const string Human = "human";

    private string _directory = "";
    private string _databasePath = "";

    public Task InitializeAsync()
    {
        _directory = Path.Combine(
            Path.GetTempPath(), "wartable-alias-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "wartable.db");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort on Windows: pooled connections may still hold handles.
        }

        return Task.CompletedTask;
    }

    private (WarTableDbContext Db, WarTableService Service) CreateContext()
    {
        var options = new DbContextOptionsBuilder<WarTableDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;

        var db = new WarTableDbContext(options);
        return (db, new WarTableService(db));
    }

    private async Task<Guid> InitializeCampaignAsync()
    {
        var (initDb, _) = CreateContext();
        await using (initDb)
            await DatabaseInitializer.InitializeAsync(initDb);

        var (setupDb, setupService) = CreateContext();
        await using (setupDb)
        {
            var campaign = await setupService.CreateCampaignAsync("Alias Race", "", "", Human);
            return campaign.Id;
        }
    }

    [Fact]
    public async Task ConcurrentObjectiveAndNoteCreates_AllocateUniqueAliases()
    {
        var campaignId = await InitializeCampaignAsync();

        const int objectiveCount = 4;
        const int noteCount = 4;

        // Release every worker at once so their campaign reads overlap, which is
        // the window the original implementation lost.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workers = new List<Task<int>>();

        for (var i = 0; i < objectiveCount; i++)
        {
            var index = i;
            workers.Add(Task.Run(async () =>
            {
                var (db, service) = CreateContext();
                await using (db)
                {
                    await gate.Task;

                    var objective = await service.CreateObjectiveAsync(
                        campaignId, $"Objective {index}", "", PlanState.Active, Human);
                    return objective.Alias;
                }
            }));
        }

        for (var i = 0; i < noteCount; i++)
        {
            var index = i;
            workers.Add(Task.Run(async () =>
            {
                var (db, service) = CreateContext();
                await using (db)
                {
                    await gate.Task;

                    var note = await service.CreateNoteAsync(
                        campaignId, BunkerNoteKind.Context, $"Note {index}", "Body", "", false,
                        Human, Array.Empty<Guid>(), Array.Empty<Guid>(), null);
                    return note.Alias;
                }
            }));
        }

        gate.SetResult();

        var aliases = await Task.WhenAll(workers);

        Assert.Equal(objectiveCount + noteCount, aliases.Length);
        Assert.Equal(aliases.Length, aliases.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, aliases.Length), aliases.OrderBy(a => a));

        var (verifyDb, verifyService) = CreateContext();
        await using (verifyDb)
        {
            Assert.Equal(objectiveCount, (await verifyService.GetObjectivesAsync(campaignId)).Count);
            Assert.Equal(noteCount, (await verifyService.GetNotesAsync(campaignId, null, null, null)).Count);
        }
    }

    [Fact]
    public async Task ConcurrentCreates_KeepAliasesMonotonicAndNeverReuseSoftDeleted()
    {
        var campaignId = await InitializeCampaignAsync();

        const int concurrentCount = 4;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var workers = Enumerable.Range(0, concurrentCount)
            .Select(index => Task.Run(async () =>
            {
                var (db, service) = CreateContext();
                await using (db)
                {
                    await gate.Task;

                    var note = await service.CreateNoteAsync(
                        campaignId, BunkerNoteKind.Gotcha, $"Note {index}", "", "", false,
                        Human, Array.Empty<Guid>(), Array.Empty<Guid>(), null);
                    return note.Alias;
                }
            }))
            .ToList();

        gate.SetResult();
        var concurrentAliases = await Task.WhenAll(workers);

        Assert.Equal(Enumerable.Range(1, concurrentCount), concurrentAliases.OrderBy(a => a));

        // A sequential create after the burst continues the sequence.
        var (sequentialDb, sequentialService) = CreateContext();
        await using (sequentialDb)
        {
            var next = await sequentialService.CreateObjectiveAsync(
                campaignId, "After Burst", "", PlanState.Draft, Human);
            Assert.Equal(concurrentCount + 1, next.Alias);
        }

        // Soft delete does not release the alias or rewind the counter.
        var (deletedDb, deletedService) = CreateContext();
        await using (deletedDb)
        {
            var notes = await deletedService.GetNotesAsync(campaignId, null, null, null);
            var removed = notes.Single(n => n.Alias == 1);
            await deletedService.DeleteNoteAsync(removed.Id, Human);
        }

        var (afterDeleteDb, afterDeleteService) = CreateContext();
        await using (afterDeleteDb)
        {
            var resolution = await afterDeleteService.ResolveAliasAsync(campaignId, 1);
            Assert.NotNull(resolution);
            Assert.True(resolution!.Deleted);

            var next = await afterDeleteService.CreateObjectiveAsync(
                campaignId, "After Delete", "", PlanState.Draft, Human);
            Assert.Equal(concurrentCount + 2, next.Alias);
        }
    }
}
