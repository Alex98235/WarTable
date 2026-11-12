# WarTable — Product and Domain Design

This document describes the current design. Implementation is the source of truth
for exact behavior. For deployment and configuration, see
[`deployment.md`](deployment.md); for the local development loop, see
[`development.md`](development.md); for container operations, see
[`container.md`](container.md).

## 1. One-line

WarTable is the durable shared state between a human and their AI agents: why a
project exists, what the plan is, what is true, and what is next.

It is not a kanban board, not a chat client, and not an activity journal. It is
the place a planning conversation lands so work can survive the conversation.

**Deployment model:** one self-hosted WarTable server on a trusted LAN owns the
data. The web UI and agents on the desktop, laptop, or server are clients.
Multiple devices see the same state; there are no accounts and no offline writes.

## 2. Principles

1. **Planning happens in conversation. WarTable stores the result.** The agent
   is the primary writer; the human is the primary reviewer and steerer.
2. **The agent drafts; either party ratifies.** The tool is policy-neutral. It
   does not enforce approval gates. Whether an agent must ask first is a matter
   for the agent's instructions, not the schema.
3. **Five access patterns, not one.** State is queried, narrative is derived,
   knowledge is searched, directives and preferences are always loaded, and
   attention is pushed.
4. **Knowledge, directives, and preferences are different things.**
   - Knowledge is campaign-scoped and retrieved on demand.
   - Directives are campaign-scoped and always applied.
   - Preferences are human-scoped and always applied.
5. **Brief, don't dump.** Every read path has a bounded briefing. Full dumps are
   available but are not the default.
6. **Provenance on every mutation.** Every change records who did it and when.
   Activity is derived from that log, never authored by hand.
7. **The plan is a DAG, not columns.** Dependencies are explicit; parallelism is
   derived from the graph.
8. **No status theater.** A campaign is a folder, not a work item. Its objectives
   carry the state that matters.
9. **One server is the source of truth.** Multiple clients may connect over the
   LAN, but there is one database and no offline write queue.
10. **The agent is an unreliable narrator.** An agent's output looks identical
    whether it reasoned or post-hoc justified, so no check that inspects only
    that output can separate the two. WarTable does not try to detect a
    fabricated rationale; it refuses to let an agent-authored field become
    load-bearing on assertion alone.
    - Shape constrains a write; it never certifies it. A filled field is not
      evidence the field is true.
    - A fabricated field must be inert. Standing is derived from use and
      resolvability, never assigned — the same reason objective status is
      derived from steps.
    - What is load-bearing resolves against ground truth the agent did not
      write: a step that exists, an evidence row, a commit, an external source.
      A basis that cannot be dereferenced is self-identifying as unfounded.
    - This binds future surfaces, not just notes. The question for a new feature
      is not "can the agent be trusted with this?" but "what happens when it is
      wrong?"

### Deployment model

```
desktop browser --+
desktop agent   --+
laptop browser  --+--> WarTable server (LAN) --> SQLite
laptop agent    --+
```

- One self-hosted server owns the database. It runs on the human's LAN,
  typically a small always-on Linux box.
- Browsers on the desktop and laptop are thin clients. They do not keep a
  separate persistent data store and they do not write offline.
- Agents connect over MCP. Stdio mode is for agents co-located with the server;
  agents on a desktop or laptop use MCP Streamable HTTP over the LAN.
- Live updates are server-pushed. After a reconnect, clients resync from a
  `since` cursor rather than replaying the whole campaign.
- SQLite is sufficient: one server serializes writes, and the workload is
  low-volume.
- There are no accounts. The server is single-human. LAN access is trusted;
  exposing it beyond the LAN requires a VPN or an authenticated reverse proxy.
  The production contract lives in [`deployment.md`](deployment.md).

## 3. Canonical scenarios

### A. Cold start after a week

The human opens the dashboard and sees an attention inbox: two flagged steps, one
blocked objective, one open question. The agent starts with a single briefing
call: campaign and charter excerpt, ready steps, blockers, flagged steps,
directives, preferences, recent decisions, and design-doc excerpts. It does not
read forty journal entries.

**Requirements:** computed briefing, attention queue, ready computation from
dependencies, always-on directives and preferences.

### B. Passing remark

Mid-task the human says: "By the way, never use newlined braces." The agent
captures a preference with provenance. It is applied the next time the agent
works, in any campaign.

**Requirements:** frictionless preference capture over MCP and UI, human
scope, provenance, retire/edit, always injected.

### C. Plan import and execution

Planning finishes in chat. The agent asks whether to move the plan to WarTable.
On approval it calls `import_plan` once: campaign (or existing campaign),
objectives, steps, dependencies. The plan lands as `Draft` or `Active` depending
on the requested `plan_state`. The agent activates it and starts work. If the
agent's instructions require human approval first, it asks; the tool does not
force it.

**Requirements:** atomic bulk import, aliases returned, `PlanState`, one call
instead of dozens of `create_step` calls.

### D. Parallel subagents

The orchestrator decomposes an active objective into steps with explicit
dependencies. Two steps with no edge between them are ready; a subagent picks
one up, posts progress updates, attaches evidence, and marks it done. A third step
depends on both and stays blocked until they finish.

**Requirements:** `depends_on`, ready-set computation, step updates, evidence,
assignee/assigner. No fleet leases or claim system.

### E. Direction change

A new decision supersedes an older one. The old note stays resolvable and is
marked `Superseded`. The briefing shows only active decisions. The plan tree can
be edited; the activity log records what changed.

**Requirements:** note status and supersession links, briefing filtering,
activity log.

### F. Parked plan

The human and agent sketch a plan that will not be executed yet. It is imported
with objectives in `Draft`. Draft objectives do not pollute the ready set or the
urgent attention queue. They sit in the plan tree with a calm badge until either
party activates them.

Before that, a passing idea can be captured as a `Seed`: a one-line unfinished
thought with an alias. Seeds appear in the briefing and attention so the idea is
not lost, but they never produce ready steps. Drafting a seed promotes it to
`Draft`.

**Requirements:** objective-level `PlanState`, seed and draft filtering in
briefing and attention.

### G. Project close

A campaign is finished. Archiving sets `ArchivedAt`; archived campaigns are
hidden from list views by default, remain reachable by id, and keep resolving
their aliases. Its knowledge remains searchable; human preferences survive
independently.

**Requirements:** archive marker, list filtering, alias resolution for history.

## 4. Domain model

### Campaign

A namespace and folder. It is not a work item and has no status.

| Field | Notes |
|---|---|
| `Id` | GUID |
| `Title` | |
| `Tags` | Comma-separated labels. |
| `NextAliasNumber` | Campaign-scoped monotonic counter for objectives and notes. |
| `CreatedAt`, `UpdatedAt` | |
| `ArchivedAt` | Nullable. Set when the campaign is archived. |

The campaign charter is a `Charter` Bunker note, not a campaign column. One
active charter is allowed; campaign creation can create it automatically. A
`DesignDoc` note records a component or area. Both are ordinary notes for
aliases, links, status, search, activity, and MCP note tools. Campaign responses
add `CharterExcerpt` and `CharterAlias` derived from the active charter. Campaigns
have no intent field; a legacy `Intent` column remains in SQLite only so existing
databases stay writable, and the application never reads or exposes it.

Relationships: objectives, bunker notes.

### Objective

A durable outcome. The unit of planning.

| Field | Notes |
|---|---|
| `Id` | GUID |
| `CampaignId` | |
| `Alias` | `#N`, campaign-scoped, never reused, resolves after soft delete. |
| `Title` | |
| `Description` | Markdown. Outcome and constraints. |
| `PlanState` | `Seed`, `Draft`, or `Active`. Objective-level. A seed is a captured unfinished thought. |
| `Status` | Derived cache: `planned`, `in-progress`, `needs-review`, `blocked`, `completed`. |
| `SortOrder` | Display order. |
| `NextStepNumber` | Per-objective counter for step numbers. |
| `CreatedAt`, `UpdatedAt`, `DeletedAt` | Soft delete. |

Steps drive `Status`. A seed or draft objective is visible but not actionable.

### Step

One atomic, verifiable unit of work.

| Field | Notes |
|---|---|
| `Id` | GUID |
| `ObjectiveId` | |
| `StepNumber` | Per-objective monotonic number, never reused. Displayed as `#N.M`. |
| `Title` | |
| `Context` | Markdown. Initial context and approach. |
| `Assignee` | Agent or human name. |
| `Assigner` | Actor who assigned it. Defaults to `orchestrator`. |
| `Status` | `open`, `in-progress`, `done`, `flagged`, `blocked`. |
| `SortOrder` | Display order. Reordering does not change `StepNumber`. |
| `CreatedAt`, `UpdatedAt`, `DeletedAt` | Soft delete. |

Relationships: `DependsOn` (many-to-many to steps), updates, evidence.

`Flagged` is the human-attention channel. It maps the parent objective to
`needs-review`. `Done` can be set by any actor; a result update or evidence is
encouraged, not enforced.

Parallelism is derived from the dependency graph. Ready = step is `open`, its
objective is `Active`, and all dependencies are `done`.

### StepUpdate

Append-only, attached to the work it describes. Progress lives here, at the
granularity where it is actually read.

| Field | Notes |
|---|---|
| `Id` | GUID |
| `StepId` | |
| `Actor` | `human` (optionally `human:<display name>`) or `agent:<name>`. |
| `Kind` | `Note`, `Blocker`, `Result`, `Handoff`. |
| `Body` | Markdown. |
| `CreatedAt` | |

### Evidence

Optional proof attached to a step. Makes "done" inspectable.

| Field | Notes |
|---|---|
| `Id` | GUID |
| `StepId` | |
| `Label` | Short description. |
| `Kind` | `Link`, `File`, `Commit`, `Test`, `Screenshot`, `Other`. |
| `Value` | URL, path, hash, or result text. |
| `CreatedBy`, `CreatedAt` | |

### BunkerNote

Campaign knowledge and campaign directives.

| Field | Notes |
|---|---|
| `Id` | GUID |
| `CampaignId` | |
| `Alias` | `#N`, campaign-scoped, shared counter with objectives, never reused. |
| `Kind` | `Charter`, `DesignDoc`, `Directive`, `Decision`, `Gotcha`, `Reference`, `Context`, `Question`. |
| `Title` | |
| `Body` | Markdown. |
| `Status` | `Active`, `Superseded`, or `Resolved` (questions). |
| `Pinned` | Directives are pinned and always injected. |
| `Tags` | |
| `CreatedBy`, `CreatedAt`, `UpdatedAt` | |
| `SupersedesId` | Nullable. For decisions that replace earlier decisions. |
| `DeletedAt` | Soft delete. |

Links to objectives and steps are optional and many-to-many.

**Directives are always-on.** They are hard, campaign-scoped rules: "public API
must stay backward compatible." Other kinds are surfaced through work-surface
context or searched on demand.

### Semantic knowledge search

Optional and provider-neutral. WarTable sends text to any OpenAI-compatible
`/v1/embeddings` service; the service never owns the index. A disposable SQLite
sidecar (`wartable.embeddings.db`) stores heading-aware chunks, content hashes,
model/profile fingerprints, and normalized float32 vectors. Reindexing is
incremental and runs in a background worker; the main database schema is
unchanged.

Search remains evidence-first: hybrid keyword + cosine ranking, top notes with
snippet, alias, kind, heading context, match source (`keyword`, `semantic`, or
`both`), and score. If embeddings are disabled, unavailable, or the index is
empty, the same API returns keyword results with `degraded: true`.

Numeric scores stay internal: hybrid RRF and cosine scores are not comparable, so
the match chip and ordering carry the signal. Model prompt differences live in
profiles (`QueryTemplate`, `DocumentTemplate`, dimensions, max input), not in
provider-specific branches. A vector database is deliberately out of scope until
the corpus outgrows brute force.

### Work-surface knowledge context

The index is also the discovery layer behind work surfaces, not only a search
box. `get_step` and `get_object` attach a bounded `KnowledgeContext` built from
explicit step/objective links, requested area tags, campaign directives, and
semantic nearest neighbours. `get_context` exposes the same retrieval before an
objective exists, so a new integration can ask what is already known about a
component. `get_plan` adds only explicit-link counts and never runs semantic
retrieval per objective.

Context is grouped by kind with per-kind quotas (directives 10, decisions 5,
gotchas 5, references 3, context 3, questions 3, charter 1, design docs 3), so a
long reference cannot bury the gotchas. Each entry carries alias, title, a
240-character excerpt, heading path, match reasons (`linked-step`,
`linked-objective`, `tag`, `directive`, `charter`, `semantic`, `keyword`), and a
count of omitted entries. Ranking puts pinned/directive and explicit links above
tags, then semantic score. Only active notes are considered; superseded notes stay
resolvable through `get_note` but do not enter context. Full bodies are never
included. If embeddings are disabled or unavailable, the same call returns
linked, tagged, directive, charter, and keyword candidates with `degraded: true`.

### Preference

Human-scoped soft rules. The "friendship record" for the one human.

| Field | Notes |
|---|---|
| `Id` | GUID |
| `Text` | The preference, e.g. "Dislikes green." |
| `Rationale` | Why, or where it came from. |
| `Source` | `human` or `agent`. |
| `Active` | Retire rather than delete. |
| `Tags` | |
| `CreatedAt`, `UpdatedAt` | |

Preferences are global and always injected. They are advisory, not blocking. A
campaign directive overrides a conflicting preference, and the agent surfaces
the conflict.

### ActivityEvent

Append-only event log. It is internal infrastructure: it drives SSE live updates,
mutation provenance, and the agent `get_activity` tool. The UI does not render a
chronological activity feed.

| Field | Notes |
|---|---|
| `Id` | GUID |
| `Sequence` | Monotonic cursor used for stream replay and resync. |
| `CampaignId` | Nullable for human-global events such as preferences. |
| `Actor` | `human` (optionally `human:<display name>`) or `agent:<name>`. |
| `EntityType` | Campaign, Objective, Step, Note, Preference. |
| `EntityId` | |
| `Action` | Created, Updated, StatusChanged, PlanStateChanged, NoteAdded, EvidenceAdded, Deleted, etc. |
| `Summary` | Human-readable sentence. |
| `Metadata` | JSON: old/new values, aliases, step refs. |
| `CreatedAt` | |

Every mutation writes one event.

### Computed views

These are not tables.

- **Briefing** — campaign and charter excerpt, ready steps, blocked steps,
  flagged steps, open questions, active directives, active preferences, recent
  decisions, design-doc excerpts, seeds, and recent activity. Bounded and
  token-aware.
- **Attention** — cross-campaign inbox: flagged steps, blocked objectives, open
  questions, and stale in-progress steps. Draft plans and seeds get a calm
  separate section.
- **Ready** — active objectives with open steps whose dependencies are done.

### Aliases and addressing

- Campaigns have no alias; they are folders.
- Objectives and Bunker notes share the campaign's monotonic `#N` counter.
  Soft-deleted objects still resolve with `deleted: true`.
- Steps use a per-objective `StepNumber` displayed as `#N.M`. Stable across
  reordering; never reused.
- Step updates, evidence, and activity events are nested or event-sourced and
  have no aliases.

### Provenance

Every mutation records an actor and a timestamp. Actor format: `human`
(optionally `human:<display name>`) or `agent:<name>`. `Assigner`, `CreatedBy`,
and `Actor` all use the same convention. The event log is canonical; entity
timestamps are caches.

### Lifecycle

- **Campaign:** no status. Created as a folder; archived by setting `ArchivedAt`.
- **Objective:** `Seed` → `Draft` → `Active` → completed (derived from steps).
  Either actor can change `PlanState`. There is no separate "abandoned" state;
  delete or leave it Draft.
- **Step:** `open` → `in-progress` → `done`. `flagged` and `blocked` can occur at
  any time. A blocked step should carry a `Blocker` update explaining why.
- **Note:** `Active` → `Superseded` (decisions) or `Resolved` (questions).
- **Preference:** `Active` → retired, preserving history.

### Storage lifecycle

SQLite is the only persistent store. Startup applies EF migrations and stamps
`PRAGMA user_version` with `DatabaseInitializer.SchemaVersion` (currently 2) as a
generation marker. Pending migrations run after the WAL is checkpointed and the
database copied to `wartable.db.bak-<timestamp>`; a failed migration restores that
copy and aborts startup. A database written before migrations existed is adopted
by recording the initial migration as applied, so live data survives upgrades.
Only `--reset-db` discards a database, and it backs up first. WAL is enabled for
file-backed databases, which is the broadcaster's write signal. The embedding
index is a separate disposable sidecar. Backups, restore, and rollback are
specified in [`deployment.md`](deployment.md).

### Conflict rule

When a campaign directive and a human preference disagree, the directive wins.
The agent states the conflict in its response rather than silently choosing.

## 5. Human and agent surfaces

Same data, two projections. Multiple browser clients are the same human surface:
they see the same server state and receive the same live updates.

**Human surface:** charter and framing docs, plan review, approval when the
human chooses to require it, attention inbox, Bunker curation, preference
capture. Optimized for reading, correcting, reordering, and flagging — not for
building plans from scratch.

**Agent surface:** briefing, ready/assigned steps, dependency graph, step
updates, evidence, notes, preferences. Optimized for one-call context assembly
and small, frequent mutations.

## 6. MCP surface

MCP is the primary write interface for agents. Parameter names are `snake_case`;
enum values are PascalCase strings. Data payloads use `snake_case`.

**Transport.** `--mode mcp` (stdio) serves agents running on the same host as the
server. Agents on a desktop or laptop connect to the server over the LAN via MCP
Streamable HTTP at `/mcp`. Both transports share the same tool classes, the
official `ModelContextProtocol.AspNetCore` 2.4.0 SDK, and the same database. The
initialize response includes baseline usage instructions; clients may treat them
as system context, but the README prompt remains the explicit fallback. The HTTP
transport is stateless: `POST /mcp` handles one JSON-RPC message per request,
`GET`/`DELETE` return 405, and no `Mcp-Session-Id` is minted. Protocol version is
negotiated by the SDK.

### Campaigns

| Tool | Parameters | Notes |
|---|---|---|
| `create_campaign` | `title`, `charter`, `tags`, `actor` | Folder, no status. A non-empty charter is stored as a `Charter` note. |
| `get_campaign` | `campaign_id` | Campaign with its objectives and Bunker notes; steps are not included. Prefer `get_briefing` for a bounded start. |
| `list_campaigns` | `search`, `include_archived` | Archived campaigns are hidden by default. |
| `update_campaign` | `campaign_id`, `title`, `tags`, `archived_at`, `actor` | Edit the charter with `update_note`. |
| `delete_campaign` | `campaign_id`, `actor` | Hard delete. |

### Plans and briefing

| Tool | Parameters | Notes |
|---|---|---|
| `import_plan` | `campaign_id?`, `campaign?`, `objectives[]`, `actor` | Atomic bulk import. Per-objective `plan_state` defaults to `Draft`. Returns aliases and step refs. |
| `get_plan` | `campaign_id`, `include_drafts?` | JSON array of objectives with steps and explicit note-link `knowledge` counts. |
| `get_briefing` | `campaign_id`, `since?` | Bounded context for starting work. |
| `get_attention` | `campaign_id?` | Cross-campaign or scoped inbox. |
| `get_activity` | `campaign_id?`, `since?`, `limit?` | Internal activity log query. |

`import_plan` example:

```json
{
  "campaign": { "title": "Auth overhaul", "charter": "Replace the legacy auth flow." },
  "objectives": [
    {
      "key": "schema",
      "title": "Design the new auth schema",
      "plan_state": "Active",
      "steps": [
        { "key": "model", "title": "Model users and sessions", "assignee": "fixer" },
        { "key": "migrate", "title": "Write the migration", "assignee": "fixer", "depends_on": ["model"] }
      ]
    }
  ]
}
```

`key` is a client-side handle used only to express dependencies within the
import. The response maps keys to real ids, aliases, and `#N.M` refs.

### Objectives

| Tool | Parameters | Notes |
|---|---|---|
| `create_objective` | `campaign_id`, `title`, `description`, `plan_state`, `actor` | Aliases are permanent; deletion does not reuse them. |
| `update_objective` | `objective_id`, `title`, `description`, `plan_state`, `actor` | |
| `reorder_objectives` | `campaign_id`, `orders`, `actor` | Changes `SortOrder` only; keys are objective GUIDs, not `#N` aliases. |
| `delete_objective` | `objective_id`, `actor` | Soft delete; old aliases remain resolvable and are never reused. |

### Steps

| Tool | Parameters | Notes |
|---|---|---|
| `create_step` | `objective_id`, `title`, `context`, `assignee`, `assigner`, `depends_on[]`, `actor` | `StepNumber` is permanent; deletion does not renumber. |
| `update_step` | `step_id`, `title`, `context`, `assignee`, `assigner`, `status`, `depends_on[]`, `actor` | Omitted `depends_on` leaves dependencies unchanged; an empty array clears them. |
| `update_step_status` | `step_id`, `status`, `actor` | Recalculates objective status. |
| `reorder_steps` | `objective_id`, `orders`, `actor` | Changes `SortOrder` only; keys are step GUIDs, not `#N.M`; `StepNumber` never changes. |
| `delete_step` | `step_id`, `force`, `actor` | Soft delete. Refuses when other steps depend on it unless `force=true`; force removes edges and reports `removed_dependencies`. |
| `add_step_update` | `step_id`, `body`, `kind`, `actor` | Append-only. |
| `add_evidence` | `step_id`, `label`, `value`, `kind`, `actor` | |

### Bunker

| Tool | Parameters | Notes |
|---|---|---|
| `add_note` | `campaign_id`, `kind`, `title`, `body`, `tags`, `pinned`, `links`, `supersedes_id`, `actor` | |
| `get_note` | `campaign_id`, `alias` | |
| `search_notes` | `campaign_id?`, `search`, `kind`, `status` | |
| `search_knowledge` | `campaign_id?`, `query`, `mode`, `limit` | Hybrid semantic + keyword over indexed Bunker notes; degrades to keyword. |
| `update_note` | `note_id`, `title`, `body`, `status`, `pinned`, `tags`, `supersedes_id`, `actor` | |

### Preferences

| Tool | Parameters | Notes |
|---|---|---|
| `add_preference` | `text`, `rationale`, `tags`, `source` | Global to the human. |
| `list_preferences` | `include_inactive` | |
| `update_preference` | `preference_id`, `text`, `rationale`, `active`, `tags` | Retire rather than delete. |

### Shared

| Tool | Parameters | Notes |
|---|---|---|
| `get_object` | `campaign_id`, `alias`, `include_knowledge?` | Resolves an objective or Bunker note, including soft-deleted. Objectives include a compact summary and bounded knowledge context. |
| `get_step` | `campaign_id`, `objective_alias`, `step_number`, `include_knowledge?` | Resolves `#N.M` with its bounded knowledge context. |
| `get_context` | `campaign_id`, `objective_id?`, `step_id?`, `query?`, `tags?` | Bounded linked/tagged/semantic knowledge for pre-planning or integration work. |

### Recommended agent behavior

- Read `get_briefing` before starting work. Do not read the whole campaign.
  Use `get_plan` when the complete objective and step graph is needed.
- When opening a step, read its `knowledge` block before starting: linked,
  tagged, and semantically related directives, decisions, gotchas, and
  references are returned as excerpts. Use `get_context` before shaping an
  objective that integrates with existing code. `get_note` is the only
  full-body read.
- Resolve the campaign before planning or recording work. Prefer an explicit
  campaign ID or title; otherwise use `list_campaigns` and match on title, tags,
  Charter text, repository/project name, and recent notes. A title similar in
  theme to the current project does not prove a match. If one campaign clearly
  matches, state which one; if several plausibly match, show their titles and IDs
  and ask. Never silently pick one.
- If no campaign exists, ask before creating one. Propose the repository or
  directory name as the title and a short charter from the project README/AGENTS.
  If the human declines, proceed without WarTable. Do not duplicate an existing
  campaign under a more generic title; use it and propose updating its charter,
  tags, or title instead.
- Read the campaign Charter and relevant DesignDoc notes before proposing work.
  The charter is the durable why, scope, and architecture; update it with
  `update_note`.
- When a planning phase finishes, offer to import the plan. Import as `Draft` if
  it will not be executed immediately; activate when starting.
- Pick up seeds when no ready work is queued: discuss the thought with the human,
  then promote it with `update_objective`; do not execute it as-is.
- Record durable decisions, gotchas, references, and open questions in the Bunker
  as they arise. Use DesignDoc notes for component-level design detail.
- Before adding a note, or when asked whether something was already discussed,
  use `search_knowledge` and cite the note alias and snippet.
- Capture a preference when the human expresses a durable preference. If it is
  ambiguous, ask before storing it.
- Post step updates as work progresses, especially blockers and results. Attach
  evidence when marking a step done.
- Refer to work by alias and `#N.M`, never by title or count.
- Objective aliases and step numbers are permanent: assigned once, never reused
  or renumbered; gaps are normal. `reorder_steps` changes display order
  (`sort_order`) only, never step numbers, and its keys are step GUIDs, not
  `#N.M` references. `get_plan` returns a JSON array of objectives, not an object
  with an `objectives` property. Deleting a step removes dependency edges; if
  another step depends on it, the tool refuses unless `force=true`.
- Flag a step when human input is needed.
- If a campaign directive conflicts with a human preference, follow the
  directive and say so.
- Subagents do not inherit the parent agent's prompt or conversation. A subagent
  with WarTable tools should call `get_briefing` or `get_plan` itself. Read
  freely; for steps, it may update status, add updates and evidence, and flag
  work for review. It should not create or delete campaigns, objectives, notes,
  steps, or preferences, or edit their fields; report durable findings to the
  parent. This is advisory; clients that need enforcement should use permissions.

## 7. HTTP API

The HTTP API mirrors the MCP surface for the web UI and other clients. Payloads
are JSON with camelCase properties and string enum values.

| Route | Methods | Purpose |
|---|---|---|
| `/api/health` | `GET` | Liveness check. |
| `/api/campaigns` | `GET`, `POST` | List (`search`, `includeArchived`) or create a campaign. |
| `/api/campaigns/{id}` | `GET`, `PUT`, `DELETE` | Read, patch (title, tags, `archivedAt`), or hard-delete a campaign. |
| `/api/objectives` | `GET`, `POST` | List (`campaignId`, `includeDrafts`) or create an objective. |
| `/api/objectives/{id}` | `GET`, `PUT`, `DELETE` | Read, patch, or soft-delete an objective. |
| `/api/objectives/reorder` | `PUT` | Reorder objectives; keys are objective GUIDs. |
| `/api/steps` | `GET`, `POST` | List by `objectiveId` or create a step. |
| `/api/steps/{id}` | `GET`, `PUT`, `DELETE` | Read, patch, or soft-delete a step (`force` on delete). |
| `/api/steps/reorder` | `PUT` | Reorder steps; keys are step GUIDs. |
| `/api/steps/{id}/status` | `PATCH` | Quick status change. |
| `/api/steps/{id}/updates` | `GET`, `POST` | List or append step updates. |
| `/api/steps/{id}/evidence` | `GET`, `POST` | List or attach evidence. |
| `/api/notes` | `GET`, `POST` | List (`campaignId`, `search`, `kind`, `status`, `includeDeleted`) or create a Bunker note. |
| `/api/notes/{id}` | `GET`, `PUT`, `DELETE` | Read, patch, or soft-delete a note. |
| `/api/notes/by-alias` | `GET` | Resolve a note by `campaignId` and `alias`. |
| `/api/preferences` | `GET`, `POST` | List (`includeInactive`) or capture a preference. |
| `/api/preferences/{id}` | `PUT` | Edit or retire a preference. |
| `/api/knowledge/search` | `GET` | Hybrid knowledge search (`campaignId`, `q`, `mode`, `limit`). |
| `/api/knowledge/status` | `GET` | Embedding availability and index state. |
| `/api/knowledge/reindex` | `POST` | Reconcile the index. |
| `/api/briefing` | `GET` | Bounded briefing (`campaignId`, `since`). |
| `/api/attention` | `GET` | Cross-campaign or scoped attention inbox. |
| `/api/activity` | `GET` | Activity log (`campaignId`, `since`, `limit`). |
| `/api/aliases/resolve` | `GET` | Resolve `#N` in a campaign; `/api/objects/resolve` is a compatibility alias. |
| `/api/events` | `GET` | Server-sent events for live updates (`campaignId`, `since`). |
| `/mcp` | `POST` | MCP Streamable HTTP transport for remote agents. |

`/api/events` starts at the log head when no cursor is supplied and replays from
`?since=` for reconnecting clients.

## 8. Screen spec

All views subscribe to `/api/events`. Cross-device changes appear without a
manual refresh, and reconnecting clients resync from their last event cursor.

### Dashboard

- **Attention inbox** at the top: flagged steps, blocked objectives, open
  questions, and stale in-progress work. Each group caps its visible rows and
  links to the full inbox.
- **Parked sections** below the urgent groups: draft plans and seeds, styled as
  calm work rather than urgent items.
- **Campaign folders** below: title, charter excerpt, active/draft objective
  counts, last activity, tags. No status badges and no status counts.
- **Recent decisions and preferences** as small secondary panels.

### Campaigns list

- Folder cards: title, charter excerpt, progress summary, last activity, tags.
- Search by title, charter, or tags.
- `Include archived` toggle; archived campaigns are hidden by default and carry
  an `Archived` badge when shown.
- No status filter.

### Campaign detail

- **Hero:** title, tags, compact stats (`in play`, `finished`, steps done, last
  update), and the primary actions (`Edit campaign`, `Bunker`, `+ Objective`).
- **Charter:** the active Charter note as a 3-line preview with `Read charter` /
  `Show less`, an edit action, and a link into the Bunker. No charter and inactive
  charter states are compact inline invitations, not empty panels.
- **Bounded brief:** "What matters now" with `Needs you` (flagged and blocked),
  `Ready`, and `Questions`, capped at two rows per group plus `+N` counts.
  Directives and recent decisions collapse into one context chip row;
  preferences appear only here, capped at four.
- **Seed capture strip:** one input above the plan to park a thought — title plus
  optional detail, no steps.
- **Plan tiers:** `In play` (active, unfinished), `Parked` (seeds, then drafts),
  and `Finished` (completed objectives, collapsed by default; auto-opened only
  when there is no open work). Finished rows are one line, with no progress,
  reorder, or state controls; `updatedAt` is shown as last activity, never as a
  completion timestamp. Reorder applies only within `In play`.
- **Knowledge rail:** sticky on desktop and stacked on narrow screens. Bunker
  search with recent notes, plus a compact DesignDoc index and create action.
  Charter and design docs are not repeated in recent notes.
- The page is organized around the plan, not by object type.

### Objective detail

- Header: alias, title, `PlanState`, derived status, description.
- Steps: ordered list with dependency indicators, assignee/assigner, status
  control, updates thread, and evidence.
- Ready and blocked indicators; "blocked by #12.1" chips.

### Bunker

- Search and filters by kind and status; optional deleted notes.
- Pinned directives at the top.
- `Semantic` switch: turns search into meaning search and replaces the browse
  list with ranked match cards; queries run on Enter, and the normal detail panel
  stays in place. Pinned directives remain visible above the search bar.
  Degraded and index-building states are stated inline; no raw scores.
- Note detail: markdown body, links to objectives/steps, supersession chain,
  provenance.
- Capture form.

### Preferences

- Operator-global page: active preferences, retired preferences, add/edit/retire.
- Provenance and rationale visible.

### Attention

- Cross-campaign inbox with filters by kind, campaign, and age.
- Draft plans and seeds appear in calm separate sections, not as urgent items.

## 9. Non-goals

- Team collaboration: multi-user accounts, authentication, membership,
  permissions, and sharing. WarTable is single-human; see
  [`designs/single-human-boundary.md`](designs/single-human-boundary.md).
- Cloud hosting and unauthenticated internet exposure. The server is LAN-first;
  access beyond the LAN requires a VPN or an authenticated reverse proxy.
- Offline-first sync, CRDTs, and multi-writer conflict resolution. Clients are
  thin views of one server; there is no offline write queue.
- Fleet coordination: leases, heartbeats, and claim systems. A single agent may
  spawn subagents; dependencies are enough.
- Global knowledge. Knowledge is campaign-scoped. Preferences are the only
  cross-campaign memory.
- Kanban views or columns.
- Mandatory approval gates.
- Rich text or file storage. Markdown and evidence links only.

## 10. Success criteria

- An agent can start a session with one briefing call and know what to do.
- Two devices pointed at the same server see each other's changes within
  seconds, without a manual refresh.
- An agent running on a laptop can reach the same server as the browser UI.
- A passing preference is captured once and applied in future campaigns.
- A plan lands in one atomic import with aliases and step refs.
- "What is ready?" and "what is blocked?" are correct from the dependency graph.
- A human can see everything that needs them in one attention inbox.
- "Done" is inspectable through updates and evidence.
- Campaigns remain folders with no status controls; the plan carries the state.
