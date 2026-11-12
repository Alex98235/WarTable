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
		- Resolve the campaign before planning or recording work. Prefer an explicit campaign id or title. Otherwise call list_campaigns and match on title, tags, Charter text, repository/project name, and recent notes. A similar title does not prove a match: if one campaign clearly matches, say which one you are using; if several plausibly match, show their titles and ids and ask; never silently pick one.
		- If no campaign exists, do not invent one or proceed as if one exists. Ask whether to create one, propose the repository or directory name as the title plus a short charter from the project README/AGENTS, and proceed without WarTable if declined. Do not duplicate a campaign that already exists under a different or more generic title; use it and propose updating its charter, tags, or title instead.
		- Read the campaign Charter before proposing work. The charter is the durable why, scope, and architecture.
		- Refer to objectives and Bunker notes as #N and steps as #N.M. Never refer to work by title or count.
		- Objective aliases and step numbers are permanent: assigned once, never reused or renumbered; gaps are normal. reorder_steps changes display order (sort_order) only, never step numbers. Reorder payload keys are step GUIDs, not #N.M references.
		- get_plan returns a JSON array of objectives, not an object with an objectives property.
		- Opening a step with get_step returns a bounded knowledge context: linked, tagged, and semantically related directives, decisions, gotchas, and context notes as excerpts. Read it before starting. Use get_context before shaping a new objective that touches existing code. get_note is the only full-body read.
		- Deleting a step removes dependency edges. If another step depends on it, the tool refuses unless force=true; review the affected refs before forcing.
		- Seeds are unfinished human thoughts. When no ready work is queued, discuss a seed with the human and promote it with update_objective; do not execute it as-is.
		- Move steps Open -> InProgress -> Done. When a step is finished, prefer a step update saying what changed and how it was checked; add evidence only for proof the repository cannot show, as a URL or a commit sha. Flag work that needs human review.
		- Objective status is derived from steps; never set it directly.
		- Each note kind has its own tool: `add_decision`, `add_gotcha`, `add_context`, `add_directive`. There is no generic `add_note`. Before writing one, stop at the first rejection that applies: if the repository already records it, it is not a note; if the human said it and changes a default, use add_preference — never infer one; they are injected into every session; if it is current state or the next thing to do, it is a seed objective; if it is true only of one objective's implementation, it belongs in that objective's description or the step's context. If an agent six months from now would not decide differently without it, write nothing. A decision is only recorded when you can name the alternative you rejected and say why. Before adding a note, or when asked what was discussed, search_knowledge and cite the alias. Search results may include dormant notes with their age; supersede one with supersedes_id rather than duplicating it.
		- Directives are hard campaign rules. Preferences are advisory and human-global. If a directive conflicts with a preference, surface the conflict.
		- Evidence is proof that lives outside the repository — use add_evidence with a URL (Link) or commit sha (Commit). WarTable stores no files, so never point at a path; if the work is in the repository, cite the commit or write a step update instead. Retract a wrong citation with delete_evidence.
		- Every write takes the `row_version` you read with the object; without it the call is rejected. A conflict means another client changed the row since you read it — re-read, then reapply.
		- WarTable does not enforce approval gates. Verify before implementation only if your own instructions require it.
		- Subagents do not inherit the parent prompt or conversation. Call get_briefing or get_plan yourself. Read freely; for steps, update status, add updates and evidence, and flag review items. Do not create or delete campaigns, objectives, notes, steps, or preferences, or edit their fields; report durable findings to the parent. This is advisory, not enforced.
		""";
}
