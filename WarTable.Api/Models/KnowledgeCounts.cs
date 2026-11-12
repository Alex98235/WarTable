namespace WarTable.Api.Models;

/// <summary>
/// Explicit note-to-objective link counts for a plan overview. Semantic matches are
/// intentionally excluded so <c>get_plan</c> stays cheap and deterministic.
/// </summary>
public sealed record KnowledgeCounts(
	int Decisions,
	int Gotchas,
	int Context)
{
	public int Total => Decisions + Gotchas + Context;
}
