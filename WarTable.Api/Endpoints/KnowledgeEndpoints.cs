using Microsoft.Extensions.DependencyInjection;
using WarTable.Api.Services.Embeddings;

namespace WarTable.Api.Endpoints;

/// <summary>HTTP surface for semantic knowledge search and index status.</summary>
public static class KnowledgeEndpoints
{
	public static void MapKnowledgeEndpoints(this WebApplication app)
	{
		var group = app.MapGroup("/api/knowledge").WithTags("Knowledge");

		group.MapGet("/search", async (
			Guid? campaignId,
			string? q,
			string? mode,
			int? limit,
			KnowledgeSearchService service,
			CancellationToken cancellationToken) =>
		{
			if (string.IsNullOrWhiteSpace(q))
				return Results.BadRequest(new { error = "q is required" });

			if (!TryParseMode(mode, out var searchMode))
			{
				return Results.BadRequest(new
				{
					error = $"Invalid mode: {mode}. Valid values: Keyword, Semantic, Hybrid",
				});
			}

			var response = await service.SearchAsync(
				campaignId, q, searchMode, limit, cancellationToken);
			return Results.Ok(response);
		});

		group.MapGet("/status", async (
			KnowledgeIndexService service,
			CancellationToken cancellationToken) =>
			Results.Ok(await service.GetStatusAsync(cancellationToken)));

		group.MapPost("/reindex", async (
			IServiceScopeFactory scopeFactory,
			CancellationToken cancellationToken) =>
		{
			using var scope = scopeFactory.CreateScope();
			var service = scope.ServiceProvider.GetRequiredService<KnowledgeIndexService>();
			return Results.Ok(await service.ReconcileAsync(cancellationToken));
		});
	}

	private static bool TryParseMode(string? value, out KnowledgeSearchMode mode)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			mode = KnowledgeSearchMode.Hybrid;
			return true;
		}

		return Enum.TryParse(value, ignoreCase: true, out mode);
	}
}
