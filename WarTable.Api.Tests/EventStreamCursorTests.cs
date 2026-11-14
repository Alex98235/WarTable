using WarTable.Api.Endpoints;

namespace WarTable.Api.Tests;

/// <summary>
/// The SSE stream's cursor rule. A cursor from another database used to be
/// indistinguishable from a cursor at the head: both arrive as "nothing new", so
/// the feed went silent and looked healthy. These cases pin the difference.
/// </summary>
public sealed class EventStreamCursorTests
{
	[Theory]
	[InlineData(null, 346L, 346L, false)]
	[InlineData(0L, 346L, 0L, false)]
	[InlineData(346L, 346L, 346L, false)]
	[InlineData(347L, 346L, 346L, true)]
	[InlineData(9999L, 12L, 12L, true)]
	public void ResolveCursor(long? since, long head, long expectedCursor, bool expectedResync)
	{
		var (cursor, resync) = EventEndpoints.ResolveCursor(since, head);

		Assert.Equal(expectedCursor, cursor);
		Assert.Equal(expectedResync, resync);
	}
}
