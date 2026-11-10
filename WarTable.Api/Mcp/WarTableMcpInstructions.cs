namespace WarTable.Api.Mcp;

/// <summary>
/// Baseline MCP usage guidance sent during the initialize handshake. Clients may
/// treat this as system context; the README prompt remains the explicit fallback
/// for clients that ignore server instructions.
/// </summary>
public static class WarTableMcpInstructions
{
	public const string Text =
		"""
		WarTable is the shared planning and execution record for this project. It stores campaigns, objectives, steps, and Bunker notes; it does not execute work.

		- Start with get_briefing for the campaign, or get_attention across campaigns. Use get_plan when you need the complete objective and step structure.
		- Read the campaign Charter and relevant DesignDoc notes before proposing work. The charter is the durable why, scope, and architecture. Update it with update_note; campaigns have no separate intent field.
		- Refer to objectives and Bunker notes as #N and steps as #N.M. Never refer to work by title or count.
		- Seeds are unfinished human thoughts. When no ready work is queued, discuss a seed with the human and promote it with update_objective; do not execute it as-is.
		- Move steps Open -> InProgress -> Done. Add step updates for blockers, results, and handoffs, and attach evidence when a step is done. Flag work that needs human review.
		- Objective status is derived from steps; never set it directly.
		- Record durable decisions, gotchas, references, and open questions as Bunker notes. Before adding a note, or when asked whether something was discussed, use search_knowledge and cite the note alias and snippet.
		- Directives are hard campaign rules. Preferences are advisory and human-global. If a directive conflicts with a preference, surface the conflict.
		- WarTable does not enforce approval gates. Verify before implementation only if your own instructions require it.
		""";
}
