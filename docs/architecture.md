# WarTable Architecture

A code map for contributors: how the server, the domain service, the optional
knowledge index, and the Svelte client fit together. It does not restate the
product design — see [`design.md`](design.md) — or the local loop, which is in
[`development.md`](development.md).

One ASP.NET process owns a SQLite database; browsers (JSON API + SSE) and agents
(MCP over streamable HTTP or stdio) are clients, and there is no second datastore.

```text
Svelte UI ──REST/SSE───┐
MCP agent ──HTTP/stdio─┴─> WarTable.Api ─> WarTableService ─> EF Core ─> wartable.db
                                        └─> ActivityBroadcaster <── WAL write events
```

## `Program.cs` modes

`Program.Main` loads `.env` (`Configuration/DotEnv.cs`), parses
`--mode/-m`, `--host/-H`, `--port/-p`, `--db`, and `--help`, creates the database
directory, then dispatches on mode. Defaults: `serve`, `localhost`, `5110`,
`~/.wartable/wartable.db`. An unknown mode exits `1`.

Both modes compose the same core: `WarTableDbContext` on SQLite,
`AddScoped<WarTableService>()`, `AddKnowledgeServices(...)`, and an MCP server
named `wartable-mcp` with instructions from `Mcp/WarTableMcpInstructions.cs` and
tools from the assembly. Both then run `DatabaseInitializer.InitializeAsync` in a
startup scope.

- **serve** (`RunServeAsync`) — the web host: JSON options (cycle ignoring,
  string enums), the `ActivityBroadcaster` singleton plus hosted-service
  registration, static files (physical `wwwroot` first, else
  `ManifestEmbeddedFileProvider`), endpoint groups, `/mcp`, `/api/health`, and
  the listen address.
- **mcp** (`RunMcpAsync`) — a plain host with stdio transport. Logging is cleared
  and re-added with `LogToStandardErrorThreshold = Trace` because stdout carries
  JSON-RPC. No HTTP endpoints, static files, or broadcaster.

## HTTP endpoint groups

Each file in `WarTable.Api/Endpoints/` is a static `Map…Endpoints` extension
called from `Program.cs`:

| File | Routes | Purpose |
| --- | --- | --- |
| `CampaignEndpoints` | `/api/campaigns` | Campaign CRUD, search, archive flag |
| `ObjectiveEndpoints` | `/api/objectives` | Objective CRUD and `PUT /reorder` |
| `StepEndpoints` | `/api/steps` | Step CRUD, `PATCH /{id}/status`, reorder, updates, evidence |
| `NoteEndpoints` | `/api/notes` | Bunker notes, `GET /by-alias` |
| `KnowledgeEndpoints` | `/api/knowledge` | `search`, `status`, `reindex` |
| `PreferenceEndpoints` | `/api/preferences` | Operator preferences |
| `BriefingEndpoints` | `/api/briefing`, `/api/attention`, `/api/activity` | Read models for the UI and agents |
| `AliasEndpoints` | `/api/aliases/resolve`, `/api/objects/resolve` | `#N` alias lookup |
| `EventEndpoints` | `/api/events` | SSE feed |
| `Program.cs` | `/api/health` | Health check for the container |

Endpoints are thin: bind parameters, call `WarTableService`, return the record it
produced. Patch records use "null means unchanged, empty string means clear".

## `WarTableService`

`Services/WarTableService.cs` is the domain core and the only place that writes
plan state. It owns campaign-scoped `#N` alias allocation (atomic, in the insert
transaction), derived objective status from child steps, dependency edges and the
"what is ready" rules, briefing/attention bounds, plan import
(`Services/ImportPlan.cs`), and activity recording. Its projections are the view
records in `Services/Views.cs`, returned by both the API and the MCP tools.

Each mutation saves plan state and then appends its `ActivityEvents` row through
`RecordEventAsync`, which assigns the next sequence, so the SSE cursor never runs
ahead of the data.

## Persistence and schema

`Data/WarTableDbContext.cs` maps ten sets: `Campaigns`, `Objectives`, `Steps`,
`StepDependencies`, `StepUpdates`, `Evidence`, `BunkerNotes`, `BunkerNoteLinks`,
`Preferences`, `ActivityEvents`. Models live in `Models/`. Five of these entities
(`Campaign`, `Objective`, `Step`, `BunkerNote`, `Preference`) implement
`IConcurrencyGuarded` and carry a `RowVersion` GUID configured as
`IsConcurrencyToken()`. The `SaveChanges` override regenerates the token for
every `Added` or `Modified` entry that implements the interface.

`Data/DatabaseInitializer.cs` owns the schema lifecycle through EF migrations:

- `SchemaVersion` is a constant, bumped whenever the EF model changes shape.
  Release notes read it; `Migrations/` carries the actual schema and data steps.
- Startup applies pending migrations, then enables WAL, which gives the broadcaster
  its write signal; in-memory databases skip WAL.
- Before applying anything it checkpoints the WAL and copies the database to
  `<db>.bak-<UTC>`. A failing migration restores that copy and aborts startup.
- A database created before migrations existed has no history. If its stamped
  version is at or above `BaselineSchemaVersion`, the initial migration is
  recorded as applied and later migrations run normally. Below that, there is no
  path forward: it is backed up and recreated.
- `--reset-db` is the only way to discard a database, and it backs up first.
- The canonical data path is the `--db` argument; the container passes
  `/data/wartable.db`.

## Live updates

`Services/ActivityBroadcaster.cs` is the only writer trigger for the SSE feed:

1. A `FileSystemWatcher` watches the database file, `-wal`, and `-journal` (never
   `-shm`); callbacks collapse a burst into one signal on a one-byte channel, so
   the watcher thread never blocks.
2. The hosted loop waits for that signal, sleeps a short debounce (200 ms default)
   so the transaction commits, drains `ActivityEvents` after the last sequence,
   and pushes to bounded subscriber channels. A slow client is dropped and
   recovers by reconnecting with `since`.

There is no timer and no idle polling: an idle feed issues no queries.
`EventEndpoints` sets the SSE headers, picks the start cursor (client `since`, or
the current head), replays the backlog to close the gap against the live
subscription, then streams `id:`/`event: activity`/JSON frames with a 15-second
keepalive comment.

The endpoint is `GET /api/events?campaignId=<id>&since=<sequence>`. A client
with no cursor starts at the current head and does not replay the entire
history; a reconnecting client passes its last cursor as `since`. If a
notification is genuinely missed (for example, a watcher overflow), live
delivery waits for the next write, and reconnecting with `since` recovers the
event from the database. The watcher is local-filesystem-only; a network-share
database is not a supported deployment.

## Embeddings pipeline

Semantic search is optional and degrades to keyword search. The path is:

1. **Options** — `Options/EmbeddingsOptions.cs` binds the `Embeddings` section
   (`Enabled`, `Endpoint`, `Model`, `Profile`, `Dimensions`, `MaxInputTokens`,
   `BatchSize`, `PollSeconds`, `TopK`, `MinScore`, `SidecarPath`).
   `AddKnowledgeServices` defaults `SidecarPath` to `wartable.embeddings.db`
   beside the main database.
2. **Profile** — `EmbeddingProfileProvider` maps the configured model (or an
   explicit profile) to query/document templates, dimensions, max input, and
   normalization, and exposes a fingerprint. The external service stays a plain
   text-to-vector endpoint.
3. **Sidecar index** — `SqliteKnowledgeIndex` implements `IKnowledgeIndex` in a
   separate file so the main schema never depends on the provider, storing chunk
   hashes, model identity, vectors, and state; brute-force cosine is intentional
   at this scale.
4. **Background worker** — `KnowledgeIndexWorker` is a no-op when disabled and
   otherwise calls `KnowledgeIndexService.ReconcileAsync` every `PollSeconds`.
   Notes are chunked by `MarkdownChunker` (heading-aware), and only chunks whose
   hash changed are embedded through `OpenAiEmbeddingClient` and replaced.
5. **Search** — `KnowledgeSearchService` keeps keyword retrieval as the baseline,
   merges semantic hits with reciprocal rank fusion, and returns
   `degraded: true` instead of failing when the service is unavailable.
6. **Context** — `KnowledgeContextService` builds the bounded knowledge block for
   a step, objective, or ad-hoc query. Explicit links, tags, and campaign
   directives always apply; semantic recall layers on top; failures fall back to
   the deterministic sources.

## MCP tool registration

`Mcp/Tools/` contains `[McpServerToolType]` classes — `CampaignTools`,
`ObjectiveTools`, `PlanTools`, `StepTools`, `BunkerTools`, `PreferenceTools`, and
`LookupTools`. Their `[McpServerTool(Name = "…")]` methods are discovered by
`WithToolsFromAssembly(serializerOptions: McpJson.Options)` in both modes, so the
stdio and HTTP surfaces stay identical. `Mcp/McpResults.cs` and `Mcp/McpJson.cs`
normalize payloads, and `WarTableMcpInstructions.cs` is the instruction text
agents receive at `initialize`.

## Frontend build

`WarTable.Client/` is a Svelte 5 + Vite app. `vite.config.js` writes the bundle
to `../WarTable.Api/wwwroot` with `emptyOutDir` and proxies `/api` to
`http://localhost:5110` in dev. `lib/api.js` is the REST client,
`lib/events.svelte.js` owns the SSE subscription and cursor, `lib/router.js` the
hash routes, and `pages/`/`components/` the screens. The API project embeds
`wwwroot/**` as an `EmbeddedResource` with `GenerateEmbeddedFilesManifest` and
excludes it from publish output, which is why the frontend build must precede the
backend build in a clean tree.

## Request and tool-call flow

An HTTP write, for example a status change:

1. The UI calls `PATCH /api/steps/{id}/status` with a `rowVersion`; `StepEndpoints`
   checks the token is present (returns `428 Precondition Required` if missing),
   then binds the request and calls `WarTableService`.
2. The service validates, applies the concurrency check (`ApplyConcurrencyToken`),
   updates the step, recomputes the parent objective's derived status, saves, then
   appends an `ActivityEvent`. A `DbUpdateConcurrencyException` at save time is
   translated to a `ConcurrencyConflictException` and returned as `409 Conflict`
   with the current server-side entity.
3. SQLite commits to WAL; the watcher signals the broadcaster, which drains the
   new events after its debounce and pushes them to open SSE connections, so the
   UI applies the change without a full refresh.

An agent tool call: the client sends `tools/call` to `/mcp` (stateless HTTP) or
over stdio; `WithToolsFromAssembly` dispatches to the tool method; the tool
parses the optional `row_version` (required, returned as error if missing or
malformed), calls the same `WarTableService` method the HTTP endpoint calls;
`McpResults`/`McpJson` shape the reply, and the same `ActivityEvents` row drives
any open UI. A stale `row_version` returns a single-block JSON error object with
`"error":"conflict"` and `"current"` containing the entity, so the agent can
re-read and retry deliberately.

## Testing

`WarTable.Api.Tests/` is an xUnit project included in `WarTable.slnx`; it targets
service behavior rather than HTTP plumbing. [`development.md#tests`](development.md#tests)
has the command.
