using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WarTable.Api.Data;
using WarTable.Api.Models;

namespace WarTable.Api.Services;

/// <summary>
/// Pushes activity events to connected SSE clients when the SQLite database changes.
/// <para>
/// File-system events are the only write trigger. There is deliberately no
/// timer-based poll: the feed is idle (and issues no queries) until the database
/// file, its <c>-wal</c>, or its <c>-journal</c> changes. Keepalive comments on the
/// SSE connection are network-only and never touch the database.
/// </para>
/// <para>
/// This assumes the database lives on a local filesystem. On a network share the
/// watcher may not receive change notifications; writes made by another process
/// would then need an explicit notification path (for example the MCP server
/// calling <see cref="Signal"/> in-process).
/// </para>
/// </summary>
public sealed class ActivityBroadcaster : BackgroundService
{
	private const int DefaultSubscriberCapacity = 256;
	private const int DrainBatchSize = 100;

	/// <summary>
	/// SQLite can emit a WAL write notification before the transaction's
	/// <c>ActivityEvents</c> row is visible to a reader. A short debounce lets the
	/// commit land before the drain queries, so a single mutation is delivered
	/// without waiting for the next write. This is not a poll: the delay only runs
	/// after a file signal, and an idle feed still issues no queries.
	/// </summary>
	private static readonly TimeSpan DefaultSignalDebounce = TimeSpan.FromMilliseconds(200);

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger<ActivityBroadcaster> _logger;

	private readonly string? _databasePath;
	private readonly string? _databaseFileName;
	private readonly string? _watchDirectory;
	private readonly TimeSpan _signalDebounce;

	/// <summary>
	/// Bounded to a single byte: a burst of file events collapses into one pending
	/// signal. DropWrite keeps writers (watcher thread-pool callbacks) non-blocking.
	/// </summary>
	private readonly Channel<byte> _signals = Channel.CreateBounded<byte>(
		new BoundedChannelOptions(1)
		{
			SingleReader = true,
			SingleWriter = false,
			FullMode = BoundedChannelFullMode.DropWrite,
		});

	/// <summary>Serializes DB drains and cursor updates.</summary>
	private readonly SemaphoreSlim _drainLock = new(1, 1);

	private readonly object _subscribersGate = new();
	private readonly HashSet<ActivitySubscription> _subscribers = new();

	private long _lastSequence;
	private FileSystemWatcher? _watcher;

	public ActivityBroadcaster(
		IServiceScopeFactory scopeFactory,
		string? databasePath,
		ILogger<ActivityBroadcaster>? logger = null,
		TimeSpan? signalDebounce = null)
	{
		_scopeFactory = scopeFactory;
		_logger = logger ?? NullLogger<ActivityBroadcaster>.Instance;

		// A negative delay is meaningless; treat it as "drain immediately".
		var debounce = signalDebounce ?? DefaultSignalDebounce;
		_signalDebounce = debounce < TimeSpan.Zero ? TimeSpan.Zero : debounce;

		if (!string.IsNullOrWhiteSpace(databasePath) &&
			!databasePath.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
		{
			var fullPath = Path.GetFullPath(databasePath);
			_databasePath = fullPath;
			_databaseFileName = Path.GetFileName(fullPath);
			_watchDirectory = Path.GetDirectoryName(fullPath);
		}
	}

	/// <summary>
	/// High-water mark for live delivery. Primarily exposed so tests/diagnostics can
	/// verify that an idle feed does not drain the database.
	/// </summary>
	public long LastSequence => Interlocked.Read(ref _lastSequence);

	public override async Task StartAsync(CancellationToken cancellationToken)
	{
		// Establish the head before any request can subscribe so a freshly started
		// server does not replay the entire history to the first client.
		await _drainLock.WaitAsync(cancellationToken);
		try
		{
			try
			{
				Interlocked.Exchange(ref _lastSequence, await GetMaxSequenceAsync(cancellationToken));
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.LogWarning(ex, "Could not read the initial activity sequence; starting at 0.");
			}
		}
		finally
		{
			_drainLock.Release();
		}

		StartWatcher();
		await base.StartAsync(cancellationToken);
	}

	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		StopWatcher();
		await base.StopAsync(cancellationToken);
		CompleteAllSubscribers();
		_signals.Writer.TryComplete();
	}

	/// <summary>
	/// Subscribes to live activity. When the first subscriber arrives after an idle
	/// period the cursor is advanced to the current head so only genuinely new
	/// events are broadcast; the endpoint itself replays the backlog it needs.
	/// </summary>
	public async Task<ActivitySubscription> SubscribeAsync(
		int capacity = DefaultSubscriberCapacity,
		CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

		await _drainLock.WaitAsync(cancellationToken);
		try
		{
			bool idle;
			lock (_subscribersGate)
				idle = _subscribers.Count == 0;

			if (idle)
			{
				try
				{
					Interlocked.Exchange(ref _lastSequence, await GetMaxSequenceAsync(cancellationToken));
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					_logger.LogWarning(ex, "Could not refresh the activity sequence for a new subscriber.");
				}
			}

			var subscription = new ActivitySubscription(this, capacity);
			lock (_subscribersGate)
				_subscribers.Add(subscription);
			return subscription;
		}
		finally
		{
			_drainLock.Release();
		}
	}

	/// <summary>
	/// Requests a drain. Used by the file watcher and available for explicit
	/// signaling (tests, or an in-process writer that already knows it changed data).
	/// </summary>
	public void Signal() => _signals.Writer.TryWrite(0);

	internal void Unsubscribe(ActivitySubscription subscription)
	{
		lock (_subscribersGate)
			_subscribers.Remove(subscription);
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			while (await _signals.Reader.WaitToReadAsync(stoppingToken))
			{
				// Collapse all pending signals before draining.
				while (_signals.Reader.TryRead(out _))
				{
				}

				try
				{
					// Let a concurrent SQLite transaction commit before reading.
					// The delay is signal-driven, so an idle feed still queries nothing.
					if (_signalDebounce > TimeSpan.Zero)
						await Task.Delay(_signalDebounce, stoppingToken);

					await DrainAsync(stoppingToken);
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
					break;
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "Activity broadcast drain failed.");
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Host shutting down.
		}
		finally
		{
			StopWatcher();
			CompleteAllSubscribers();
		}
	}

	private async Task DrainAsync(CancellationToken cancellationToken)
	{
		await _drainLock.WaitAsync(cancellationToken);
		try
		{
			lock (_subscribersGate)
			{
				// No subscribers: never touch the database.
				if (_subscribers.Count == 0)
					return;
			}

			while (true)
			{
				var batch = await GetActivityAsync(
					Interlocked.Read(ref _lastSequence), DrainBatchSize, cancellationToken);

				if (batch.Count == 0)
					return;

				foreach (var activity in batch)
				{
					var current = Interlocked.Read(ref _lastSequence);
					if (activity.Sequence <= current)
						continue;

					Interlocked.Exchange(ref _lastSequence, activity.Sequence);
					Broadcast(activity);
				}

				if (batch.Count < DrainBatchSize)
					return;
			}
		}
		finally
		{
			_drainLock.Release();
		}
	}

	private void Broadcast(ActivityEvent activity)
	{
		List<ActivitySubscription>? overflowed = null;

		lock (_subscribersGate)
		{
			foreach (var subscription in _subscribers)
			{
				if (!subscription.TryWrite(activity))
					(overflowed ??= new List<ActivitySubscription>()).Add(subscription);
			}

			if (overflowed is not null)
			{
				foreach (var subscription in overflowed)
				{
					// Complete it so the SSE client disconnects and reconnects with
					// its last seen sequence, replaying whatever it missed.
					_subscribers.Remove(subscription);
					subscription.Complete();
				}
			}
		}
	}

	private async Task<long> GetMaxSequenceAsync(CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<WarTableDbContext>();
		return await db.ActivityEvents.MaxAsync(e => (long?)e.Sequence, cancellationToken) ?? 0;
	}

	private async Task<List<ActivityEvent>> GetActivityAsync(
		long since,
		int limit,
		CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var service = scope.ServiceProvider.GetRequiredService<WarTableService>();
		return await service.GetActivityAsync(null, since, limit);
	}

	private void StartWatcher()
	{
		if (_databaseFileName is null || _watchDirectory is null)
			return;

		if (!Directory.Exists(_watchDirectory))
		{
			_logger.LogDebug(
				"Activity file watcher disabled: database directory {Directory} does not exist.",
				_watchDirectory);
			return;
		}

		try
		{
			var watcher = new FileSystemWatcher(_watchDirectory, _databaseFileName + "*")
			{
				NotifyFilter = NotifyFilters.FileName |
							   NotifyFilters.LastWrite |
							   NotifyFilters.Size |
							   NotifyFilters.CreationTime,
				IncludeSubdirectories = false,
				EnableRaisingEvents = false,
			};

			watcher.Created += OnFileChanged;
			watcher.Changed += OnFileChanged;
			watcher.Deleted += OnFileChanged;
			watcher.Renamed += OnFileRenamed;
			watcher.Error += (_, _) => Signal();

			if (Interlocked.CompareExchange(ref _watcher, watcher, null) is not null)
			{
				watcher.Dispose();
				return;
			}

			watcher.EnableRaisingEvents = true;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Could not start the activity file watcher at {Directory}.", _watchDirectory);
		}
	}

	private void StopWatcher()
	{
		var watcher = Interlocked.Exchange(ref _watcher, null);
		if (watcher is null)
			return;

		try
		{
			watcher.EnableRaisingEvents = false;
			watcher.Dispose();
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "Error while disposing the activity file watcher.");
		}
	}

	private void OnFileChanged(object sender, FileSystemEventArgs e)
	{
		if (IsRelevantFile(e.Name) || IsRelevantFile(Path.GetFileName(e.FullPath)))
			Signal();
	}

	private void OnFileRenamed(object sender, RenamedEventArgs e)
	{
		if (IsRelevantFile(e.Name) ||
			IsRelevantFile(e.OldName) ||
			IsRelevantFile(Path.GetFileName(e.FullPath)) ||
			IsRelevantFile(Path.GetFileName(e.OldFullPath)))
		{
			Signal();
		}
	}

	/// <summary>
	/// The main database file, its WAL, and its rollback journal are write signals.
	/// <c>-shm</c> is shared memory and changes constantly; <c>.bak-*</c> backups are
	/// written by the initializer, not by activity mutations.
	/// </summary>
	private bool IsRelevantFile(string? fileName)
	{
		if (string.IsNullOrEmpty(fileName) || _databaseFileName is null)
			return false;

		return fileName.Equals(_databaseFileName, StringComparison.OrdinalIgnoreCase) ||
			   fileName.Equals(_databaseFileName + "-wal", StringComparison.OrdinalIgnoreCase) ||
			   fileName.Equals(_databaseFileName + "-journal", StringComparison.OrdinalIgnoreCase);
	}

	private void CompleteAllSubscribers()
	{
		ActivitySubscription[] subscriptions;
		lock (_subscribersGate)
		{
			subscriptions = _subscribers.ToArray();
			_subscribers.Clear();
		}

		foreach (var subscription in subscriptions)
			subscription.Complete();
	}
}

/// <summary>
/// A single SSE client's bounded view of the activity stream. Disposing it detaches
/// the client; a full buffer completes it so the client can reconnect with a cursor.
/// </summary>
public sealed class ActivitySubscription : IDisposable
{
	private readonly ActivityBroadcaster _owner;
	private readonly Channel<ActivityEvent> _channel;
	private int _disposed;

	internal ActivitySubscription(ActivityBroadcaster owner, int capacity)
	{
		_owner = owner;
		_channel = Channel.CreateBounded<ActivityEvent>(
			new BoundedChannelOptions(capacity)
			{
				SingleReader = true,
				SingleWriter = false,
				// Wait mode makes TryWrite return false when full, which is how
				// slow subscribers are detected and dropped.
				FullMode = BoundedChannelFullMode.Wait,
			});
	}

	public ChannelReader<ActivityEvent> Reader => _channel.Reader;

	internal bool TryWrite(ActivityEvent activity) => _channel.Writer.TryWrite(activity);

	internal void Complete() => _channel.Writer.TryComplete();

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
			return;

		_owner.Unsubscribe(this);
		_channel.Writer.TryComplete();
	}
}
