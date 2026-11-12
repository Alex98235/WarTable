using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WarTable.Api.Data;
using WarTable.Api.Models;
using WarTable.Api.Services;

namespace WarTable.Api.Tests;

/// <summary>
/// File-backed integration tests for the event-driven activity feed. Each test gets
/// its own temporary SQLite file so the file watcher and WAL sidecars are exercised
/// exactly as they are in production.
/// </summary>
public sealed class ActivityBroadcasterTests : IAsyncLifetime
{
	private const string Human = "human";

	private string _directory = "";
	private string _databasePath = "";
	private ServiceProvider _provider = null!;
	private ActivityBroadcaster _broadcaster = null!;

	public async Task InitializeAsync()
	{
		_directory = Path.Combine(
			Path.GetTempPath(), "wartable-broadcast-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_directory);
		_databasePath = Path.Combine(_directory, "wartable.db");

		var services = new ServiceCollection();
		services.AddLogging();
		services.AddDbContext<WarTableDbContext>(options =>
			options.UseSqlite($"Data Source={_databasePath}"));
		services.AddScoped<WarTableService>();
		_provider = services.BuildServiceProvider();

		using (var scope = _provider.CreateScope())
		{
			await DatabaseInitializer.InitializeAsync(
				scope.ServiceProvider.GetRequiredService<WarTableDbContext>());
		}

		_broadcaster = new ActivityBroadcaster(
			_provider.GetRequiredService<IServiceScopeFactory>(), _databasePath);
		await _broadcaster.StartAsync(CancellationToken.None);
	}

	public async Task DisposeAsync()
	{
		await _broadcaster.StopAsync(CancellationToken.None);
		_broadcaster.Dispose();
		await _provider.DisposeAsync();
		SqliteConnection.ClearAllPools();

		try
		{
			Directory.Delete(_directory, recursive: true);
		}
		catch (IOException)
		{
			// Best effort on Windows: the watcher may still be releasing handles.
		}
	}

	private async Task<T> WithServiceAsync<T>(Func<WarTableService, Task<T>> action)
	{
		using var scope = _provider.CreateScope();
		var service = scope.ServiceProvider.GetRequiredService<WarTableService>();
		return await action(service);
	}

	private static async Task<ActivityEvent?> ReadAsync(
		ChannelReader<ActivityEvent> reader,
		TimeSpan timeout)
	{
		using var cts = new CancellationTokenSource(timeout);
		try
		{
			if (await reader.WaitToReadAsync(cts.Token))
			{
				reader.TryRead(out var activity);
				return activity;
			}
		}
		catch (OperationCanceledException)
		{
			// Timed out.
		}

		return null;
	}

	[Fact]
	public async Task FileBackedDatabaseUsesWal()
	{
		using var scope = _provider.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<WarTableDbContext>();

		var connection = db.Database.GetDbConnection();
		await connection.OpenAsync();

		await using var command = connection.CreateCommand();
		command.CommandText = "PRAGMA journal_mode;";
		var mode = Convert.ToString(await command.ExecuteScalarAsync());

		Assert.Equal("wal", mode?.ToLowerInvariant());
	}

	[Fact]
	public async Task FileWriteAfterSubscribeIsBroadcast()
	{
		using var subscription = await _broadcaster.SubscribeAsync();

		var campaign = await WithServiceAsync(
			service => service.CreateCampaignAsync("Broadcast Campaign", "", "", Human));

		// Explicit signal keeps the test independent of FileSystemWatcher timing;
		// the watcher may also have signaled already.
		_broadcaster.Signal();

		var delivered = await ReadAsync(subscription.Reader, TimeSpan.FromSeconds(5));

		Assert.NotNull(delivered);
		Assert.Equal("Created", delivered!.Action);
		Assert.Equal(campaign.Id, delivered.CampaignId);
		Assert.Equal(ActivityEntityType.Campaign, delivered.EntityType);
	}

	[Fact]
	public async Task SignalBeforeCommitIsDeliveredAfterDebounce()
	{
		// Point the watcher at a directory that does not exist so it is disabled.
		// Signal() is then the only trigger, making the pre-commit race the one
		// thing under test.
		//
		// The signal has to arrive before the activity row is committed, and the
		// drain has to run after it, so the commit must finish inside the debounce
		// window. That is the whole timing assumption, and it used to be only
		// 450ms wide with a cold EF model, which a loaded CI runner can miss — and
		// when it misses, nothing ever arrives because the disabled watcher cannot
		// signal again. Warm the model first, shorten the pre-write pause, and keep
		// the window generous so the test measures the debounce, not the runner.
		await WithServiceAsync(
			service => service.CreateCampaignAsync("Warm Up Campaign", "", "", Human));

		var unwatchedDatabase = Path.Combine(_directory, "no-watch", "wartable.db");
		var debounced = new ActivityBroadcaster(
			_provider.GetRequiredService<IServiceScopeFactory>(),
			unwatchedDatabase,
			signalDebounce: TimeSpan.FromSeconds(2));

		await debounced.StartAsync(CancellationToken.None);
		try
		{
			using var subscription = await debounced.SubscribeAsync();

			// Signal before the activity row exists. Without the debounce the drain
			// runs immediately, finds nothing, and no second signal arrives.
			debounced.Signal();
			await Task.Delay(100);

			var campaign = await WithServiceAsync(
				service => service.CreateCampaignAsync("Debounced Campaign", "", "", Human));

			var delivered = await ReadAsync(subscription.Reader, TimeSpan.FromSeconds(10));

			Assert.NotNull(delivered);
			Assert.Equal("Created", delivered!.Action);
			Assert.Equal(campaign.Id, delivered.CampaignId);
		}
		finally
		{
			await debounced.StopAsync(CancellationToken.None);
			debounced.Dispose();
		}
	}

	[Fact]
	public async Task IdleBroadcasterDoesNotDrainOrAdvanceCursor()
	{
		var before = _broadcaster.LastSequence;

		await WithServiceAsync(
			service => service.CreateCampaignAsync("Idle Campaign", "", "", Human));

		_broadcaster.Signal();
		await Task.Delay(300);

		// No subscribers: no query, no cursor movement.
		Assert.Equal(before, _broadcaster.LastSequence);
	}

	[Fact]
	public async Task FullSubscriberBufferIsCompleted()
	{
		using var subscription = await _broadcaster.SubscribeAsync(capacity: 1);

		for (var i = 0; i < 5; i++)
		{
			var index = i;
			await WithServiceAsync(
				service => service.CreateCampaignAsync($"Overflow {index}", "", "", Human));
		}

		_broadcaster.Signal();

		// A completed channel ends the reader; a live one would keep waiting.
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		var completed = false;
		try
		{
			while (await subscription.Reader.WaitToReadAsync(cts.Token))
			{
				while (subscription.Reader.TryRead(out _))
				{
				}
			}

			completed = true;
		}
		catch (OperationCanceledException)
		{
			// Never completed: the slow subscriber was not dropped.
		}

		Assert.True(completed, "A full subscriber buffer should complete the subscription.");
	}
}
