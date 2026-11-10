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
				var cursor = since
					?? await db.ActivityEvents.MaxAsync(e => (long?)e.Sequence, cancellationToken)
					?? 0;

				using var subscription = await broadcaster.SubscribeAsync(
					cancellationToken: cancellationToken);

				try
				{
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
