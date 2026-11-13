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
| Campaign | `list_campaigns`, `get_campaign`, `create_campaign`, `update_campaign`, `archive_campaign`, `unarchive_campaign` | Find the project folder, read or update its charter, archive or restore it. |
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

<!-- instructions:begin — generated from WarTable.Api/Mcp/instructions.txt; do not edit by hand -->

```text
WarTable is the shared planning and execution record for this project. It stores campaigns, objectives, steps, and Bunker notes; it does not execute work.

- Start with get_briefing for the campaign, or get_attention across campaigns. Use get_plan for the complete objective and step structure.
- Resolve the campaign before planning or recording work. Prefer an explicit campaign id or title. Otherwise call list_campaigns and match on title, tags, charter, repository/project name, and recent notes. A similar title does not prove a match: if one matches, say which one you are using; if several match, show their titles and ids and ask; never silently pick one.
- If no campaign exists, do not invent one. Ask whether to create one — repository or directory name as the title, a short charter from README/AGENTS — and proceed without WarTable if declined. Never duplicate a campaign that exists under a different or more generic title; use it and propose updating its charter, tags, or title.
- Read the campaign charter first: it states the purpose and scope, and is framing, never a record of what happened. Do not write history, upgrades, or deletions into it.
- Refer to objectives and Bunker notes as #N and steps as #N.M. Never refer to work by title or count.
- Objective aliases and step numbers are permanent: assigned once, never reused or renumbered; gaps are normal.
- get_plan returns a JSON array of objectives, not an object with an objectives property.
- Read the knowledge context get_step returns before starting. Call get_context before shaping an objective that touches existing code. get_note is the only full-body read.
- No tool deletes a campaign: archive it and say so; hard delete is human-only.
- Seeds are unfinished human thoughts. When no ready work is queued, discuss a seed with the human and promote it with update_objective; do not execute it as-is.
- Move steps Open -> InProgress -> Done. When a step is finished, prefer a step update saying what changed and how it was checked; add evidence only for proof the repository cannot show, as a URL or a commit sha. Flag work that needs human review.
- Objective status is derived from steps; never set it directly.
- One tool per note kind: `add_decision`, `add_gotcha`, `add_context`, `add_directive`; there is no `add_note`. Before writing one, stop at the first rejection that applies: if the repository already records it, it is not a note; if the human said it and it changes a default, use add_preference, never infer one, they reach every session; if it is current state or the next thing to do, it is a seed objective; if it is true only of one objective: the step's context if it dies with the step, a Context note if it outlives the objective. If an agent six months hence would decide the same without it, write nothing. A decision needs the alternative rejected and why; if that alternative is no longer selectable, it is history, not a decision — the log holds it. A rejected claim is not relocated into the charter, an objective, or a step; say nothing. Before adding a note, search_knowledge and cite the alias. A dormant note (with its age): supersede it, do not duplicate.
- Directives are hard campaign rules. Preferences are advisory and human-global. If a directive conflicts with a preference, surface the conflict.
- Evidence is proof that lives outside the repository — use add_evidence with a URL (Link) or commit sha (Commit). WarTable stores no files: never point at a path; if the work is in the repository, cite the commit or write a step update instead. Retract a wrong citation with delete_evidence.
- Every write takes the `row_version` you read with the object; without it the call is rejected. A conflict means another client changed the row — re-read, then reapply.
- WarTable does not enforce approval gates. Verify before implementation only if your own instructions require it.
- Subagents do not inherit the parent prompt or conversation. Call get_briefing or get_plan yourself. Read freely; for steps, update status, add updates and evidence, and flag review items. Do not create or delete campaigns, objectives, notes, steps, or preferences, or edit their fields; report durable findings to the parent. Advisory, not enforced.
```

<!-- instructions:end -->

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
