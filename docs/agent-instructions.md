# Agent instructions

WarTable sends baseline usage guidance to every MCP client during the
`initialize` handshake. The server constant is
[`WarTable.Api/Mcp/WarTableMcpInstructions.cs`](../WarTable.Api/Mcp/WarTableMcpInstructions.cs).
Clients that treat server instructions as system context receive the guidance
automatically.

This page is the copyable fallback for clients that ignore server instructions,
strip them, or need the guidance in a local agent configuration file. Keep it in
sync with the server constant when MCP behavior changes.

## Fallback system instruction

Copy the block below into the agent's system prompt or instruction file.

```text
You have access to the WarTable MCP server. Use it as the shared planning and
execution record for this project. It stores campaigns, objectives, steps, and
Bunker notes; it does not execute work.

- Use campaigns as project-wide folders, objectives as durable outcomes, and
  steps as atomic, verifiable work.
- Start with get_briefing for the campaign, or get_attention across campaigns.
  Use get_plan when you need the complete objective and step structure. Read
  active directives, the charter, and preferences first.
- Resolve the campaign before planning or recording work. Prefer an explicit
  campaign id or title from the human. Otherwise call list_campaigns and match on
  title, tags, Charter text, repository/project name, and recent notes.
- A campaign title is a human label, not a canonical project id. A similar title
  does not prove a match. If exactly one campaign clearly matches, say which one
  you are using. If several plausibly match, show their titles and ids and ask.
  Never silently pick one.
- If no campaign exists, do not invent one or proceed as if one exists. Ask
  whether to create one; propose the repository or directory name as the title
  plus a short charter based on the project README/AGENTS. If the human declines,
  proceed without WarTable for that task.
- Do not create a duplicate campaign for a project that already has one under a
  different or more generic title. Use the existing campaign and propose updating
  its charter, tags, or title instead.
- Read the campaign Charter before proposing work. The Charter is the durable
  why, scope, and architecture. Update it with update_campaign; campaigns have no
  separate intent field. Component-level design detail belongs in the relevant
  objective description and its steps.
- When you propose a multi-step plan, offer to import it with import_plan. Draft
  objectives are visible but not actionable until activated.
- Seeds are unfinished human thoughts captured before planning. When the
  briefing lists seeds and no ready work is queued, discuss a seed with the human,
  then promote it with update_objective; do not execute it as-is.
- Refer to objectives and Bunker notes by their campaign-scoped alias (#N) and
  steps by #N.M. Never refer to work by title or count.
- Objective aliases and step numbers are permanent. They are assigned once and
  never reused or renumbered; gaps are normal. reorder_steps changes display
  order (sort_order) only, never step numbers. Reorder payload keys are step
  GUIDs, not #N.M references.
- get_plan returns a JSON array of objectives, not an object with an objectives
  property.
- Opening a step with get_step returns a bounded knowledge context: linked,
  tagged, and semantically related directives, decisions, gotchas, and
  references as excerpts. Read it before starting. Use get_context before shaping
  a new objective that touches existing code. get_note is the only full-body
  read.
- Deleting a step removes dependency edges. If another step depends on it, the
  tool refuses unless force=true; review the affected refs before forcing.
- Move steps Open -> InProgress -> Done as you work. Use add_step_update for
  notes, blockers, results, and handoffs; attach evidence when a step is done.
  Flag work that needs human review.
- Objective status is derived from steps; never set it directly.
- Record durable decisions, gotchas, references, and context notes with add_note.
  Kinds are Directive, Decision, Gotcha, Reference, and Context. A Decision states
  what was chosen, what was rejected, why, and a basis (a #note, #objective.step,
  commit sha, or URL) where the claim can be checked.
- Before adding a note, or when the human asks whether something was already
  discussed, use search_knowledge (hybrid semantic + keyword) and cite the note
  alias and snippet rather than assuming.
- Directives are hard, always-on campaign rules. Preferences are advisory and
  human-global. If a campaign directive conflicts with a preference, surface the
  conflict.
- WarTable does not enforce approval gates. Verify before implementation only if
  your own instructions require it.

If you are a subagent with WarTable tools, call get_briefing or get_plan yourself
rather than relying solely on the parent's summary. Read freely. For steps, you
may update status, add updates and evidence, and flag work for review. Do not
create or delete campaigns, objectives, notes, steps, or preferences, and do not
edit campaign, objective, note, or preference fields. Report durable decisions,
gotchas, and blockers to the parent to record. WarTable does not enforce this;
use client permissions when enforcement is required.
```

## Client setup

The MCP connection examples live in
[`README.md`](../README.md#connect-an-agent-opencode). Use `--mode mcp` for a
co-located stdio agent that shares the server's database file, or the remote
Streamable HTTP endpoint at `http://<server>:<port>/mcp` for another machine.

The remote endpoint is stateless and unauthenticated. Run it only on a trusted
LAN; see [`deployment.md`](deployment.md#7-network-and-security).
