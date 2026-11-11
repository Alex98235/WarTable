# WarTable — Design Direction

Status: Implemented for v0.1.0. This remains the product and domain design; the
code is authoritative for schema details and endpoint behavior.

Historical design notes are marked in sections 10, 12, and 13.

## 1. One-line

WarTable is the durable shared state between a human and their AI agents: why a
project exists, what the plan is, what is true, and what is next.

It is not a kanban board, not a chat client, and not an activity journal. It is
the place a planning conversation lands so work can survive the conversation.

**Deployment model:** one self-hosted WarTable server on a trusted LAN owns the
data. The web UI and agents on the desktop, laptop, or server are clients.
Multiple devices see the same state; there are no accounts and no offline writes.

## 2. What was wrong

The previous shape was an artifact tracker wearing AI-native clothes. The kanban
was one symptom; SitReps and Intel were the same mistake in a different form.

- **SitReps were write-only.** Nothing read them at the moment of work. They
  duplicated state the steps already encoded and grew without bound.
- **Intel was a silo.** Knowledge was stored but never injected when it mattered.
  The agent was told to "search intel" manually, which is lossy and expensive.
- **The plan had no shape.** Kanban columns and `parallelGroup` implied ordering
  by convention. The app could not answer "what can be worked on now?"
- **"Done" had no evidence.** A step was done because the agent said so. Trust,
  not throughput, is the bottleneck in AI collaboration.
- **Context grew monotonically.** `get_campaign` returned everything, including
  every journal entry. The tool got worse the longer a project lived.
- **The UI organized by object type.** Tabs for Objectives/SitReps/Intel are an
  admin view of a database, not a command surface.

## 3. Principles

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

### Deployment model

```
desktop browser ─┐
desktop agent  ──┤
                 ├──> WarTable server (LAN) ──> SQLite
laptop browser ──┤
laptop agent   ──┘
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

## 4. Canonical scenarios

### A. Cold start after a week

The human opens the dashboard and sees an attention inbox: two flagged steps, one
blocked objective, one open question. The agent starts with a single briefing
call: campaign charter, current focus, ready steps, blockers, directives,
preferences, recent decisions. It does not read forty journal entries.

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
dependencies. Two steps with no edge between them are ready; a subagent claims
one, posts progress updates, attaches evidence, and marks it done. A third step
depends on both and stays blocked until they finish.

**Requirements:** `dependsOn`, ready-set computation, step updates, evidence,
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

A campaign is finished. Its knowledge remains searchable; human preferences
survive independently. Archiving stale or finished campaigns is a future
concern; for now a campaign is simply a folder that stops changing.

**Requirements:** deferred archive semantics, alias resolution for history.

## 5. Domain model

### Campaign

A namespace and folder. It is not a work item and has no status.

| Field | Notes |
|---|---|
| `Id` | GUID |
| `Title` | |
| `NextAliasNumber` | Campaign-scoped monotonic counter for objectives and notes. |
| `CreatedAt`, `UpdatedAt` | |
| `ArchivedAt` | Nullable. Future archive support. |

The campaign charter is a `Charter` Bunker note, not a campaign column. One
active charter is allowed; campaign creation can create it automatically. A
`DesignDoc` note records a component or area. Both are ordinary notes for
aliases, links, status, search, activity, and MCP note tools. The old
`Campaign.Intent` column is retained only so older SQLite files stay writable;
the application never reads or exposes it.

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
| `Status` | Derived cache: `Planned`, `InProgress`, `NeedsReview`, `Blocked`, `Completed`. |
| `SortOrder` | Display order. |
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
| `Assigner` | Actor who assigned it. |
| `Status` | `Open`, `InProgress`, `Done`, `Flagged`, `Blocked`. |
| `SortOrder` | Display order. Reordering does not change `StepNumber`. |
| `CreatedAt`, `UpdatedAt`, `DeletedAt` | Soft delete. |

Relationships: `DependsOn` (many-to-many to steps), updates, evidence.

`Flagged` is the human-attention channel. It maps the parent objective to
`NeedsReview`. `Done` can be set by any actor; a result update or evidence is
encouraged, not enforced.

Parallel groups are derived from the dependency graph. Ready = step is `Open`,
its objective is `Active`, and all dependencies are `Done`.

### StepUpdate

Append-only, attached to the work it describes. This is the replacement for
SitReps, at the granularity where it is actually read.

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

Campaign knowledge and campaign directives. The Bunker replaces Intel.

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
empty, the same API returns keyword results with `degraded: true`. The Bunker
keeps its browse list by default; a `Semantic` switch turns the note list into a
matches panel and runs only on Enter, because embedding calls cost time and
money. Numeric scores stay internal: hybrid RRF and cosine scores are not
comparable, so the match chip and ordering carry the signal.
Model prompt differences live in profiles (`QueryTemplate`, `DocumentTemplate`,
dimensions, max input), not in provider-specific branches. A vector database is
deliberately out of scope until the corpus outgrows brute force.

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
`linked-objective`, `tag`, `directive`, `charter`, `semantic`, `keyword`), and a count of
omitted entries. Ranking puts pinned/directive and explicit links above tags,
then semantic score. Only active notes are considered; superseded notes stay
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

- **Briefing** - charter excerpt, focus, ready steps, blockers, flagged steps,
  open questions, active directives, active preferences, recent decisions,
  design-doc excerpts, seeds, and recent activity. Bounded and token-aware.
- **Attention** — cross-campaign inbox: flagged steps, blocked objectives, open
  questions, stale in-progress steps. Draft plans and seeds get a calm separate section.
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

- **Campaign:** no status. Created as a folder. Archive later via `ArchivedAt`.
- **Objective:** `Seed` → `Draft` → `Active` → completed (derived from steps). Either actor
  can change `PlanState`. There is no separate "abandoned" state; delete or leave
  it Draft.
- **Step:** `Open` → `InProgress` → `Done`. `Flagged` and `Blocked` can occur at
  any time. A blocked step should carry a `Blocker` update explaining why.
- **Note:** `Active` → `Superseded` (decisions) or `Resolved` (questions).
- **Preference:** `Active` → retired, preserving history.

### Conflict rule

When a campaign directive and a human preference disagree, the directive
wins. The agent states the conflict in its response rather than silently
choosing.

## 6. Human and agent surfaces

Same data, two projections. Multiple browser clients are the same human surface:
they see the same server state and receive the same live updates.

**Human surface:** charter and framing docs, plan review, approval when the
human chooses to require it, attention inbox, Bunker curation, preference
capture. Optimized for reading, correcting, reordering, and flagging - not for
building plans from scratch.

**Agent surface:** briefing, ready/assigned steps, dependency graph, step
updates, evidence, notes, preferences. Optimized for one-call context assembly
and small, frequent mutations.

## 7. MCP surface

The MCP server remains the primary write interface for agents. All parameter
names are `snake_case`; enum values are PascalCase strings. Data payloads use
`snake_case`.

**Transport.** `--mode mcp` (stdio) remains for agents running on the same host
as the server. Agents on a desktop or laptop connect to the server over the LAN
via MCP Streamable HTTP at `/mcp`. Both transports share the same tool classes,
the official `ModelContextProtocol.AspNetCore` 2.4.0 SDK, and the same database.
The initialize response includes baseline usage instructions; clients may treat
them as system context, but the README prompt remains the explicit fallback.
The HTTP transport is stateless: `POST /mcp` handles one JSON-RPC message per
request, `GET`/`DELETE` return 405, and no `Mcp-Session-Id` is minted. Protocol
version is negotiated by the SDK. Tool payloads keep `snake_case` and string
enums.

### Campaigns

| Tool | Parameters | Notes |
|---|---|---|
| `create_campaign` | `title`, `charter`, `tags` | Folder, no status. A non-empty charter is stored as a `Charter` note. |
| `get_campaign` | `campaign_id` | Metadata and summaries, not a full dump. |
| `list_campaigns` | `search`, `include_archived` | |
| `update_campaign` | `campaign_id`, `title`, `tags`, `archived_at` | Edit the charter with `update_note`. |
| `delete_campaign` | `campaign_id` | Hard delete for now. |

### Plans and briefing

| Tool | Parameters | Notes |
|---|---|---|
| `import_plan` | `campaign_id?`, `campaign?`, `objectives[]` | Atomic bulk import. Per-objective `plan_state` defaults to `Draft`. Returns aliases and step refs. |
| `get_plan` | `campaign_id`, `include_drafts?` | Returns a JSON array of objectives with steps and explicit note-link `knowledge` counts. |
| `get_briefing` | `campaign_id`, `since?` | Bounded context for starting work. |
| `get_attention` | `campaign_id?` | Cross-campaign or scoped inbox. |
| `get_activity` | `campaign_id`, `since?`, `limit?` | Internal activity log query. |

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
| `create_objective` | `campaign_id`, `title`, `description`, `plan_state` | Aliases are permanent; deletion does not reuse them. |
| `update_objective` | `objective_id`, `title`, `description`, `plan_state` | |
| `reorder_objectives` | `campaign_id`, `orders` | Changes `SortOrder` only; keys are objective GUIDs, not `#N` aliases. |
| `delete_objective` | `objective_id` | Soft delete; old aliases remain resolvable and are never reused. |

### Steps

| Tool | Parameters | Notes |
|---|---|---|
| `create_step` | `objective_id`, `title`, `context`, `assignee`, `assigner`, `depends_on[]` | `StepNumber` is permanent; deletion does not renumber. |
| `update_step` | `step_id`, `title`, `context`, `assignee`, `assigner`, `status`, `depends_on[]` | |
| `update_step_status` | `step_id`, `status` | Recalculates objective status. |
| `reorder_steps` | `objective_id`, `orders` | Changes `SortOrder` only; keys are step GUIDs, not `#N.M`; `StepNumber` never changes. |
| `delete_step` | `step_id`, `force` | Soft delete. Refuses when other steps depend on it unless `force=true`; force removes edges and reports `removed_dependencies`. |
| `add_step_update` | `step_id`, `body`, `kind`, `actor` | Append-only. |
| `add_evidence` | `step_id`, `label`, `kind`, `value`, `actor` | |

### Bunker

| Tool | Parameters | Notes |
|---|---|---|
| `add_note` | `campaign_id`, `kind`, `title`, `body`, `tags`, `pinned`, `links` | |
| `get_note` | `campaign_id`, `alias` | |
| `search_notes` | `campaign_id?`, `search`, `kind`, `status` | |
| `search_knowledge` | `campaign_id?`, `query`, `mode`, `limit` | Hybrid semantic + keyword over indexed Bunker notes; degrades to keyword. |
| `update_note` | `note_id`, `title`, `body`, `status`, `pinned`, `supersedes_id` | |

### Preferences

| Tool | Parameters | Notes |
|---|---|---|
| `add_preference` | `text`, `rationale`, `tags` | Global to the human. |
| `list_preferences` | `include_inactive` | |
| `update_preference` | `preference_id`, `text`, `rationale`, `active` | Retire rather than delete. |

### Shared

| Tool | Parameters | Notes |
|---|---|---|
| `get_object` | `campaign_id`, `alias`, `include_knowledge?` | Resolves an objective or Bunker note, including soft-deleted. Objectives include a compact summary and bounded knowledge context. |
| `get_step` | `campaign_id`, `objective_alias`, `step_number`, `include_knowledge?` | Resolves `#N.M` with its bounded knowledge context. |
| `get_context` | `campaign_id`, `objective_id?`, `step_id?`, `query?`, `tags?` | Bounded linked/tagged/semantic knowledge for pre-planning or integration work. |

### Removed

`create_sitrep`, `log_intel`, `search_intel`, `update_campaign_status`, and the
`parallel_group` parameters are removed. `get_campaign_plan` is replaced by
`get_plan` and `get_briefing`.

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
  `update_note`. Campaigns have no separate intent field.
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
  with WarTable tools should call `get_briefing` or `get_plan` itself.

## 8. HTTP API

The HTTP API mirrors the MCP surface for the web UI:

- `/api/campaigns`
- `/api/objectives`
- `/api/steps`
- `/api/steps/{id}/updates`
- `/api/steps/{id}/evidence`
- `/api/notes`
- `/api/preferences`
- `/api/briefing`
- `/api/attention`
- `/api/activity`
- `/api/events` (server-sent events for live updates)
- `/api/aliases/resolve`
- `/mcp` (MCP Streamable HTTP transport for remote agents)

`/api/sitrep` and `/api/intel` are removed.

## 9. Screen spec

All views subscribe to `/api/events`. Cross-device changes appear without a
manual refresh, and reconnecting clients resync from their last event cursor.

### Dashboard

- **Attention inbox** at the top: flagged steps, blocked objectives, open
  questions, stale in-progress work, and seeds awaiting draft. Each item shows
  campaign, objective/step, reason, age, and a link.
- **Campaign folders** below: title, charter excerpt, active/draft objective
  counts, last activity, tags. No status badges and no status counts.
- **Recent decisions and preferences** as small secondary panels.

### Campaigns list

- Folder cards: title, charter excerpt, progress summary, last activity, tags.
- Search. No status filter. Archived campaigns hidden by default once archive
  exists.

### Campaign detail

- **Hero:** title, tags, compact stats, and one primary action. The active Charter
  note appears as a 2–3 line preview with `Read charter` / `Show less`, an edit
  action, and a link into the Bunker. No charter and inactive charter states are
  compact inline invitations.
- **Bounded brief:** "What matters now" with `Needs you`, `Ready`, and
  `Questions`, capped at two rows per group plus `+N` counts. Directives and
  recent decisions collapse into one context chip row; preferences appear only
  here, capped at four.
- **Plan tiers:** `In play` (active, unfinished), `Parked` (drafts and seeds),
  and `Finished` (completed objectives, collapsed by default). Finished rows are
  one line, with no progress, reorder, or state controls; `updatedAt` is shown
  as last activity, never as a completion timestamp. Reorder applies only within
  `In play`.
- **Quick capture:** one input to save a seed — title plus optional note, no
  steps. Seed rows live under `Parked`; "Shape" promotes one into a draft. Seeds
  never expose ready or blocked actions.
- **Knowledge rail:** sticky on desktop; Bunker search/recent notes plus a
  compact DesignDoc index and create action. Charter and design docs are not
  repeated in recent notes. The global Steering panel is not on this page.
- No SitReps/Intel tabs.

### Objective detail

- Header: alias, title, `PlanState`, derived status, description.
- Steps: ordered list with dependency indicators, assignee/assigner, status
  control, updates thread, and evidence.
- Ready and blocked indicators; "blocked by #12.1" chips.

### Bunker

- Search and filters by kind and status.
- Pinned directives at the top.
- `Semantic` switch: turns search into meaning search, replaces the browse list
  with ranked match cards, hides pin/delete/edit actions on results, and keeps
  the normal detail panel. Pinned directives collapse to a quiet reminder.
  Degraded/index-building states are stated inline; no raw scores.
- Note detail: markdown body, links to objectives/steps, supersession chain,
  provenance.
- Capture form.

### Preferences

- Operator-global page: active preferences, retired preferences, add/edit/retire.
- Provenance and rationale visible.
- Optional review queue for agent-proposed preferences.

### Attention

- Cross-campaign inbox with filters by kind, campaign, and age.
- Draft plans and seeds appear in calm separate sections, not as urgent items.

## 10. Impact on the current codebase (historical)

This table records the migration from the pre-v0.1.0 shape. It is kept as design
history; the implementation is authoritative.

| Current | Change |
|---|---|
| `Campaign.Status` | Removed. Campaign is a folder. |
| `Campaign.Description` | Removed. Replaced by the `Charter` Bunker note. |
| `Objective.Status` | Becomes a derived cache; add `PlanState`. |
| `Step.ParallelGroup` | Removed. Add `DependsOn`; derive parallelism. |
| `Step` | Add `StepNumber`, updates, evidence. |
| `SitRep`, `SitRepObjective` | Removed. Replaced by `StepUpdate` and `ActivityEvent`. |
| `Intel` | Replaced by `BunkerNote` with status, supersession, and links. |
| — | Add `Preference`, `Evidence`, `ActivityEvent`. |
| MCP | Done: uses the official `ModelContextProtocol.AspNetCore` 2.4.0 SDK. The 32 tools, `snake_case` payloads, stdio, and stateless Streamable HTTP at `/mcp` are implemented. |
| Networking | Add `/api/events` (SSE) driven by `ActivityBroadcaster`: a `FileSystemWatcher` on the SQLite DB/WAL signals new activity, so idle clients cause no database polling. Clients resync with a `since` cursor. |
| UI | Remove SitReps/Intel tabs and campaign status controls; add plan tree, Bunker, Preferences, Attention. Subscribe to live events. |
| README | Done: rewritten for the v0.1.0 product. |
| Database | Disposable. Recreate with `EnsureCreated`; no migrations. |

## 11. Non-goals

- Team collaboration: multi-user accounts, authentication, membership,
  permissions, and sharing. WarTable is single-human.
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
- Database migrations. The single server's database remains disposable.

## 12. Open questions (historical record)

This list records the questions raised during design. Items 3, 5, 8, 11, and 12
were resolved during implementation; the others are retained as context, not as
commitments.

1. **Archive semantics.** Read-only? Hidden by default? Do aliases still resolve?
2. **Evidence.** Links only, or file uploads? Where are files stored?
3. **Questions — resolved.** A question is a Bunker note kind.
4. **Preference capture.** Should the agent write directly, or propose and let the
   human confirm? A review queue is safer but adds friction.
5. **Campaign intent - resolved.** The campaign has one active `Charter` Bunker
   note (why, scope, architecture), created with the campaign or written later.
   The old `Intent` column is legacy storage only.
6. **Re-import.** How should `import_plan` update an existing plan without
   duplicating objectives?
7. **Dependency UX.** How are dependencies added and edited in the UI? How are
   cycles prevented?
8. **LAN access control — resolved.** No accounts or tokens; the supported
   boundary is a trusted LAN, with a VPN or authenticated reverse proxy beyond it.
9. **Flag reasons.** Should `Flagged` require a short reason, or is a `Blocker`
   update enough?
10. **Briefing format.** Structured JSON, compact markdown, or both? What is the
    token budget?
11. **Remote MCP transport — resolved.** Official `ModelContextProtocol.AspNetCore`
    SDK: stateless Streamable HTTP at `/mcp` for remote agents, plus stdio for
    co-located agents.
12. **Live-update transport — resolved.** Server-sent events at `/api/events`,
    driven by `ActivityBroadcaster` watching SQLite file changes. Clients resync
    from a `since` cursor after reconnecting; idle clients cause no DB polling.

## 13. Suggested build order (historical)

This order was executed for v0.1.0 and is kept as a record.

1. **Model and schema.** Campaign, Objective `PlanState`, Step `StepNumber` and
   `DependsOn`, StepUpdate, Evidence, BunkerNote, Preference, ActivityEvent.
2. **Service layer.** Status derivation, ready/blocked computation, attention and
   briefing queries, provenance writes.
3. **MCP surface.** `import_plan`, `get_briefing`, step updates and evidence,
   Bunker, preferences, activity. Include both stdio and Streamable HTTP.
4. **Live updates.** Emit activity events to `/api/events`; add client
   subscription and resync-by-cursor.
5. **UI.** Campaign plan tree, Bunker, Preferences, dashboard attention. Remove
   sitreps, intel tabs, and campaign status controls.
6. **Docs and prompt guidance.** Rewrite README and the recommended agent
   instructions.
7. **Tests.** Status derivation, dependency cycles, ready set, alias resolution,
   import atomicity, briefing bounds, cross-device event delivery.

## 14. Success criteria

- An agent can start a session with one briefing call and know what to do.
- Two devices pointed at the same server see each other's changes within
  seconds, without a manual refresh.
- An agent running on a laptop can reach the same server as the browser UI.
- A passing preference is captured once and applied in future campaigns.
- A plan lands in one atomic import with aliases and step refs.
- "What is ready?" and "what is blocked?" are correct from the dependency graph.
- A human can see everything that needs them in one attention inbox.
- "Done" is inspectable through updates and evidence.
- There is no SitRep, no Intel tab, and no campaign status theater.
