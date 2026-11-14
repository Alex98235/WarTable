using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using WarTable.Api.Data;
using WarTable.Api.Models;
using WarTable.Api.Services;

namespace WarTable.Api.Endpoints;

/// <summary>
/// Server-Sent Events feed for the activity log. Live delivery is driven by the
/// <see cref="ActivityBroadcaster"/> file watcher rather than polling the database,
/// so an idle connection issues no queries. The endpoint replays its backlog from
/// the client's cursor and then streams the broadcaster's live events.
/// </summary>
public static class EventEndpoints
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
		Converters = { new JsonStringEnumConverter() },
	};

	private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(15);

	public static void MapEventEndpoints(this WebApplication app)
	{
		app.MapGet("/api/events",
			async (
				Guid? campaignId,
				long? since,
				HttpContext context,
				WarTableService service,
				WarTableDbContext db,
				ActivityBroadcaster broadcaster,
				CancellationToken cancellationToken) =>
			{
				var response = context.Response;
				response.ContentType = "text/event-stream";
				response.Headers.CacheControl = "no-cache";
				response.Headers.Connection = "keep-alive";
				response.Headers["X-Accel-Buffering"] = "no";

				// Without an explicit cursor, start at the current head so only new
				// events are streamed (a client can pass ?since=0 to replay history).
				var head = await db.ActivityEvents.MaxAsync(e => (long?)e.Sequence, cancellationToken)
					?? 0;
				var (cursor, resync) = ResolveCursor(since, head);

				using var subscription = await broadcaster.SubscribeAsync(
					cancellationToken: cancellationToken);

				try
				{
					// The client's cursor was ahead of the log, so it belongs to another
					// database. Tell it where the head is instead of streaming silence it
					// would wait on forever.
					if (resync)
					{
						await response.WriteAsync(
							$"id: {head}\nevent: resync\ndata: {{}}\n\n", cancellationToken);
						await response.Body.FlushAsync(cancellationToken);
					}
					// Replay the backlog from the client's cursor in batches. The
					// broadcaster only pushes events written after the subscription,
					// so this closes the gap for reconnecting clients.
					while (true)
					{
						var backlog = await service.GetActivityAsync(campaignId, cursor, 100);
						if (backlog.Count == 0)
							break;

						foreach (var activity in backlog)
						{
							await WriteEventAsync(response, activity, cancellationToken);
							cursor = activity.Sequence;
						}

						await response.Body.FlushAsync(cancellationToken);

						if (backlog.Count < 100)
							break;
					}

					// Drain anything the broadcaster buffered while the backlog was
					// replaying. Sequence comparison dedupes against the replay.
					while (subscription.Reader.TryRead(out var buffered))
					{
						if (!IsDeliverable(buffered, campaignId, cursor))
							continue;

						await WriteEventAsync(response, buffered, cancellationToken);
						cursor = buffered.Sequence;
					}

					await response.Body.FlushAsync(cancellationToken);

					// Live stream. The 15s timeout is network-only keepalive; it never
					// triggers a database query.
					while (!cancellationToken.IsCancellationRequested)
					{
						using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
						timeout.CancelAfter(KeepAliveInterval);

						bool hasData;
						try
						{
							hasData = await subscription.Reader.WaitToReadAsync(timeout.Token);
						}
						catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
						{
							await response.WriteAsync(": keepalive\n\n", cancellationToken);
							await response.Body.FlushAsync(cancellationToken);
							continue;
						}

						// Channel completed (shutdown or slow-subscriber drop).
						if (!hasData)
							break;

						var wrote = false;
						while (subscription.Reader.TryRead(out var activity))
						{
							if (!IsDeliverable(activity, campaignId, cursor))
								continue;

							await WriteEventAsync(response, activity, cancellationToken);
							cursor = activity.Sequence;
							wrote = true;
						}

						if (wrote)
							await response.Body.FlushAsync(cancellationToken);
					}
				}
				catch (OperationCanceledException)
				{
					// Client disconnected; nothing to clean up beyond the using scope.
				}
			}).WithTags("Events");
	}

	/// <summary>
	/// The stream cursor for a new connection. A client cursor ahead of the head
	/// cannot have come from this log: sequences restart when a database is
	/// recreated or restored from a backup, so it belongs to a different one.
	/// It is replaced by the head and the client is told to resync, because
	/// otherwise every event is filtered out as already seen and the connection
	/// looks healthy while delivering nothing.
	/// </summary>
	public static (long Cursor, bool Resync) ResolveCursor(long? since, long head)
	{
		if (!since.HasValue)
			return (head, false);

		return since.Value > head ? (head, true) : (since.Value, false);
	}

	private static bool IsDeliverable(ActivityEvent activity, Guid? campaignId, long cursor)
		=> activity.Sequence > cursor &&
		   (!campaignId.HasValue || activity.CampaignId == campaignId.Value);

	private static Task WriteEventAsync(
		HttpResponse response,
		ActivityEvent activity,
		CancellationToken cancellationToken)
	{
		var json = JsonSerializer.Serialize(activity, JsonOptions);
		return response.WriteAsync(
			$"id: {activity.Sequence}\nevent: activity\ndata: {json}\n\n",
			cancellationToken);
	}
}
