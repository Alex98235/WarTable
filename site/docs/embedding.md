---
sidebar_position: 6
title: Embedding WarTable in an agent workflow
---

# Embedding WarTable in an agent workflow

WarTable is the shared planning record your agent reads and writes. It does not
run work. An agent uses MCP to read the current plan, keep steps moving, and
record durable knowledge.

## The MCP endpoint

- Endpoint: `POST http://<server>:5110/mcp`
- Transport: MCP Streamable HTTP, stateless
- One JSON-RPC message per request; notifications return `202`
- `GET` and `DELETE /mcp` return `405`
- No `Mcp-Session-Id` is minted
- No authentication; LAN trust is the security boundary

Because the transport is stateless, an agent can reconnect at any time without
losing a session. Each request is self-contained.

For a co-located agent, run the standalone binary with `--mode mcp` and point
`--db` at the same database file the server uses. The stdio process and the
server must share a local filesystem.

## Tool families

The tools are grouped by what they change.

| Family | Tools | Use |
| --- | --- | --- |
| Campaign | `list_campaigns`, `get_campaign`, `create_campaign`, `update_campaign`, `delete_campaign` | Find the project folder, read or update its charter. |
| Objective | `get_plan`, `create_objective`, `update_objective`, `delete_objective`, `reorder_objectives`, `import_plan` | Shape durable outcomes and import a proposed plan. |
| Step | `get_step`, `create_step`, `update_step`, `update_step_status`, `reorder_steps`, `add_step_update`, `add_evidence`, `delete_evidence`, `delete_step` | Move atomic work through open, in progress, and done. |
| Notes | `add_decision`, `add_gotcha`, `add_context`, `add_directive`, `update_note`, `delete_note`, `get_note` | Record knowledge. Each note kind has its own add tool; there is no generic `add_note`. |
| Preferences | `add_preference`, `list_preferences`, `update_preference` | Record advisory, human-global rules. |
| Context and search | `get_briefing`, `get_attention`, `get_activity`, `get_context`, `search_notes`, `search_knowledge`, `get_object` | Start a session, inspect related knowledge, find what was already discussed. |

Notes are Markdown. A note carries a status, tags, links, and optional
supersession, so an old decision can be retired without being duplicated.

Every write takes the `row_version` the agent read with the object. Without it
the call is rejected. A conflict means another client changed the row since it
was read; the agent re-reads and reapplies.

## What the agent should be told

WarTable sends baseline usage guidance during the MCP `initialize` handshake.
Clients that treat server instructions as system context receive it
automatically. For clients that ignore or strip server instructions, copy this
block into the agent's system prompt or instruction file:

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
- If no campaign exists, do not invent one. Ask whether to create one — repository
  or directory name as the title, a short charter from README/AGENTS — and
  proceed without WarTable if declined.
- Never duplicate a campaign that exists under a different or more generic title.
  Use it and propose updating its charter, tags, or title.
- Read the campaign charter first. It states the purpose and scope, and is
  framing, never a record of what happened: do not write history, upgrades, or
  deletions into it. Update it with update_campaign; campaigns have no separate
  intent field.
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
  context notes as excerpts. Read it before starting. Use get_context before
  shaping a new objective that touches existing code. get_note is the only
  full-body read.
- Deleting a step removes dependency edges. If another step depends on it, the
  tool refuses unless force=true; review the affected refs before forcing.
- Move steps Open -> InProgress -> Done as you work. Use add_step_update for
  notes, blockers, results, and handoffs. When a step is finished, prefer a step
  update saying what changed and how it was checked; add evidence only for proof
  the repository cannot show, as a URL or a commit sha. Flag work that needs
  human review.
- Objective status is derived from steps; never set it directly.
- Each note kind has its own MCP tool: add_decision, add_gotcha, add_context,
  add_directive. There is no add_note. Before writing one, stop at the
  first rejection that applies: if the repository already records it, it is not a
  note; if the human said it and changes a default, use add_preference — never
  infer one; they are injected into every session; if it is current state or the
  next thing to do, it is a seed objective; if it is true only of one objective's
  implementation, it belongs in that objective's description or the step's
  context. If an agent six months hence would decide the same without it, write
  nothing. A decision needs the alternative rejected and why; if that alternative
  is no longer selectable, it is history, not a decision — the log holds it. A
  claim this ladder rejects is not relocated into the charter, an objective, or a
  step; say nothing.
- Before adding a note, or when the human asks what was already discussed, use
  search_knowledge (hybrid semantic + keyword) and cite the alias rather than
  assuming. A dormant note in results (with its age): supersede it, do not
  duplicate.
- Directives are hard, always-on campaign rules. Preferences are advisory and
  human-global. If a campaign directive conflicts with a preference, surface the
  conflict.
- Evidence is proof that lives outside the repository — use add_evidence with
  a URL (Link) or commit sha (Commit). WarTable stores no files, so never point
  at a path; if the work is in the repository, cite the commit or write a step
  update instead. Retract a wrong citation with delete_evidence.
- Every write takes the row_version you read with the object; without it the
  call is rejected. A conflict means another client changed the row since you
  read it — re-read, then reapply.
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

## A first exchange

A typical session starts with the briefing and ends with a step update:

1. `list_campaigns` if the project is not already known, then `get_briefing`
   with the campaign id.
2. `get_plan` only when the full objective and step structure is needed.
3. Do the work outside WarTable.
4. `update_step_status` and `add_step_update` with what changed and how it was
   checked.
5. `add_decision`, `add_gotcha`, or `add_directive` only when the knowledge
   would change a future decision.

Search is hybrid: semantic when embeddings are enabled, keyword otherwise. A
disabled or unreachable embedding service does not break the agent; results fall
back to keywords with `degraded: true`.
