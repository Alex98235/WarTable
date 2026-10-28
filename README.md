# WarTable

**Human-AI planning workspace.** A local web app + MCP server for tracking campaigns, objectives, intel, and sitreps alongside your projects.

Built with C# ASP.NET Minimal API, EF Core + SQLite, and Svelte 5.

## Install (prebuilt binary)

Grab the latest `wartable-win-x64.zip` or `wartable-linux-x64.tar.gz` from the
Releases page. The archive contains a single self-contained binary — no .NET
runtime required. The web UI is embedded in it.

```bash
./wartable --port 5110                       # local only
./wartable --host 0.0.0.0 --port 5110        # reachable from your LAN
./wartable --mode mcp                        # MCP stdio server
```

## Quick Start

```bash
# Serve mode (web UI + HTTP API — default)
cd WarTable.Api
dotnet run -- --mode serve

# Open http://localhost:5110
```

```bash
# MCP stdio mode (for AI agent integration)
dotnet run -- --mode mcp
```

## OpenCode Integration

Add to your `.opencode.json` or `open.env.json`:

```json
{
  "mcpServers": {
    "wartable": {
      "command": "dotnet",
      "args": ["run", "--project", "C:/full/path/to/WarTable/WarTable.Api", "--", "--mode", "mcp"],
      "env": {}
    }
  }
}
```

### Recommended System Instruction

Add this to your agent instructions so the AI uses WarTable autonomously:

> You have access to the WarTable MCP server. Use it to manage planning and task tracking:
>
> - When you propose a detailed multi-step implementation plan, ask the user if they want to record it in WarTable
> - On approval, create a Campaign, break it into Objectives with appropriate lanes, log key decisions as Intel
> - Objectives, Intel entries and SitReps each have a campaign-scoped alias (`#N`). Refer to work by alias — "blocked on #12" — never by title, and never by count
> - Update Objective lane as work progresses
> - Log important context or gotchas as Intel entries
> - Post SitReps when wrapping up a session, referencing the objectives you worked on
> - Before planning, read the latest SitRep and search Intel for context

## Usage

### Campaigns

A Campaign represents a feature, epic, or initiative. Each campaign has a description (markdown), status, and tags. Think of a campaign the way you think of a repository: it is the namespace that owns its own kanban board, its own intel, and its own sitreps.

### Aliases

Every objective, intel entry and sitrep gets a short integer **alias**, displayed as `#N`. Aliases are numbered by a single monotonic counter per campaign, shared across all three kinds — so inside one campaign `#7` is unambiguous and never refers to two different things. Campaigns themselves have no alias.

Aliases are the stable way to talk about work, both in the UI and for an AI agent over MCP: "blocked on #12", "finished #9 and #10". They are never reused, even after deletion.

### Objectives

Objectives are individual tasks within a campaign. They live on a kanban board with lanes: Backlog → Ready → In Progress → Review → Done.

Objectives are **soft deleted**: they disappear from the board but their alias keeps resolving, so history stays readable.

### Intel

Intel captures durable knowledge: decisions made, context notes, gotchas discovered, or references. Every intel entry belongs to a campaign. Searchable and filterable, also soft deleted.

### SitReps

A SitRep is an **append-only journal entry** for a campaign — a record of one working session, not a second copy of the board state.

A sitrep holds a free-text summary plus references to the objectives it talks about, each tagged as **Completed**, **Blocked**, or **Next**. References are stored as aliases together with a snapshot of the objective's title and lane at the time of writing. The snapshot is deliberate: the sitrep stays accurate even after the objective is renamed, moved, or deleted.

The objectives board remains the single source of truth for *current* state. A sitrep is one observation of that state at a point in time. That is what makes concurrent or asynchronous sessions safe — two sessions writing sitreps produce two journal entries, not two diverging branches of the board. If the two disagree, you reconcile by reading the board, not by merging sitreps.

A typical agent loop:

1. Read the campaign, its objectives, the latest sitrep, and recent intel.
2. Do the work, moving objectives between lanes.
3. Log anything durable as intel.
4. Post a sitrep referencing the objectives that were completed, blocked, or are next.

## MCP Tools

The MCP server exposes 11 tools over stdio. All parameter names are `snake_case`; enum values are PascalCase strings. Protocol keys (`protocolVersion`, `serverInfo`, `inputSchema`, `isError`) stay camelCase as the MCP spec requires; returned data payloads use `snake_case`.

### Campaigns

| Tool | Description | Parameters |
| --- | --- | --- |
| `create_campaign` | Create a campaign | `title`\*, `description`, `tags` |
| `get_campaign` | Campaign by id, including objectives, intel and sitreps | `campaign_id`\* |
| `list_campaigns` | List campaigns | `search`, `status` |
| `update_campaign_status` | Change campaign status | `campaign_id`\*, `status`\* |

`status`: `Planning` \| `Active` \| `Completed` \| `Abandoned`

### Objectives

| Tool | Description | Parameters |
| --- | --- | --- |
| `create_objective` | Create an objective under a campaign | `campaign_id`\*, `title`\*, `description`, `lane` |
| `list_objectives` | List objectives with their aliases | `campaign_id`, `lane` |
| `update_objective_lane` | Move an objective to a different lane | `id`\*, `lane`\* |

`lane`: `backlog` \| `ready` \| `in-progress` \| `review` \| `done`. Lane is the objective's only state field — there is no separate status.

### Intel

| Tool | Description | Parameters |
| --- | --- | --- |
| `log_intel` | Log a durable note on a campaign | `title`\*, `body`\*, `campaign_id`\*, `category`, `tags` |
| `search_intel` | Search intel | `search`, `category`, `campaign_id` |

`category`: `Decision` \| `Context` \| `Gotcha` \| `Reference`

### SitReps

| Tool | Description | Parameters |
|---|---|---|
| `create_sitrep` | Append a sitrep to the campaign journal | `campaign_id`\*, `summary`\*, `source`, `completed`, `blocked`, `next` |

`completed` / `blocked` / `next` are arrays of **objective alias integers**, not titles and not counts. `source` is `human` or `agent:<name>` and defaults to `human`.

### Aliases

| Tool | Description | Parameters |
|---|---|---|
| `get_object` | Resolve a `#N` alias to the object it identifies | `campaign_id`\*, `alias`\* |

Aliases are shared across objectives, intel and sitreps within a campaign, so a bare number is unambiguous inside one campaign. Soft-deleted objects still resolve, with `deleted: true`.

\* required

## Command Line Options

```
--mode, -m  serve | mcp
              serve: run the web UI + HTTP API (default)
              mcp:   run as an MCP stdio server
--host, -H  interface to bind in serve mode (default: localhost)
              use 0.0.0.0 to accept connections from other machines
--port, -p  HTTP port for serve mode (default: 5110)
--db        SQLite database path (default: ~/.wartable/wartable.db)
--help, -h  show help
```

Beware: there is no authentication. Only bind to a non-loopback interface on a
network you trust.

## Build & Run

```bash
dotnet build
dotnet run -- --mode serve
dotnet run -- --mode mcp
```

## Changelog

`CHANGELOG.md` is generated from commit history by
[git-cliff](https://git-cliff.org), using `cliff.toml`:

```bash
npm run changelog     # regenerate CHANGELOG.md from the full history
```

The same config produces the body of every GitHub release, so commit messages
drive the notes. Prefix them with a Conventional Commit type so they land in the
right section: `feat:`, `fix:`, `docs:`, `refactor:`, `perf:`, `test:`, `ci:`,
`build:`, `chore:`. Anything else is listed under "Other" rather than dropped.

Requires the `git-cliff` binary on your `PATH`
(`scoop install git-cliff`, `brew install git-cliff`, or
`cargo install git-cliff`).

## Releasing

Tagging is the release trigger. `npm run release` derives the next version from
the commits since the last tag, prints the notes that will ship, and — only after
you confirm — creates an annotated tag and pushes it:

```bash
npm run release              # interactive
npm run release -- --dry-run # preview only
npm run release -- --yes     # no prompt
```

It refuses to run on a dirty tree, off `main`, or if the tag already exists.

Pushing the tag runs `.github/workflows/release.yml`, which builds `win-x64` and
`linux-x64` self-contained binaries and publishes a GitHub release whose body is
generated by git-cliff from `cliff.toml`.

The version bump follows the commits: `feat:` bumps minor, `fix:` bumps patch,
and a `!` or `BREAKING CHANGE` bumps major.

## Database

SQLite database stored at `~/.wartable/wartable.db` by default. Created automatically on first run.

Schema changes are handled by deleting the database file during development — startup uses `EnsureCreated()`, which does nothing if the file already exists. There are no migrations; this is a single-user local tool and the database is disposable.
