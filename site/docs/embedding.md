---
sidebar_position: 6
title: Embedding WarTable in an agent workflow
---

import Instructions from './_instructions.generated.mdx';

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

<Instructions />

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
