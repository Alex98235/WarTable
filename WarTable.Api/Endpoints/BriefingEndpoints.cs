using WarTable.Api.Services;

namespace WarTable.Api.Endpoints;

/// <summary>
/// Maps the read-only briefing, attention inbox, and activity feed endpoints.
/// </summary>
public static class BriefingEndpoints
{
	public static void MapBriefingEndpoints(this WebApplication app)
	{
		app.MapGet("/api/briefing", async (Guid? campaignId, long? since, WarTableService service) =>
		{
			if (!campaignId.HasValue || campaignId.Value == Guid.Empty)
				return Results.BadRequest(new { error = "campaignId is required" });

			try
			{
				var briefing = await service.GetBriefingAsync(campaignId.Value, since);
				return Results.Ok(briefing);
			}
			catch (InvalidOperationException ex)
			{
				return Results.BadRequest(new { error = ex.Message });
			}
		}).WithTags("Briefing");

		app.MapGet("/api/attention", async (Guid? campaignId, WarTableService service) =>
		{
			var attention = await service.GetAttentionAsync(campaignId);
			return Results.Ok(attention);
		}).WithTags("Briefing");

		app.MapGet("/api/activity", async (Guid? campaignId, long? since, int? limit, WarTableService service) =>
		{
			var activity = await service.GetActivityAsync(campaignId, since, limit ?? 50);
			return Results.Ok(activity);
		}).WithTags("Briefing");
	}
}
