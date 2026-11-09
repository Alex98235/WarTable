# WarTable

WarTable is a shared planning surface for a human and their AI agents.

It is aimed at solo developers using agentic workflows who need a clearer
picture of how a project is taking shape. A **campaign** is the project.
**Objectives** are the durable outcomes; **steps** are the atomic, verifiable
work. The human and the agent see the same live plan, and the agent can update
it through MCP. WarTable gives that work a durable structure and a memory that
survives sessions.

WarTable also gives the agent a place to store small but meaningful bits of
information:

- **Preferences** — global soft rules about how you like to work, worth
  remembering between sessions.
- **Bunker notes** — campaign-specific knowledge: directives, decisions,
  gotchas, references, and open questions.

For example:

**Prompt:** "... And please document any code you write."
> Project-specific; lands as a pinned **Directive** in the campaign **Bunker**.

**Prompt:** "... I usually format my C code with two newlines before a function
definition."
> Human preference; lands as a global **Preference**. A campaign **Directive**
> overrides it, and the agent should surface the conflict.

Letting an agent run loose on a GitHub repo—or worse, your local files—often
produces a mess of files, issues, states, notes, and plans. WarTable is where
the plan, the work, and the context live together.

Built with C# ASP.NET Minimal API, EF Core + SQLite, Svelte 5, and the official
MCP C# SDK. The full product and domain design lives in
[`docs/design.md`](docs/design.md).

## Quickstart (Docker, one command)

Run the public image with a named volume. There is no checkout, `.env`, host data
directory, or `chown` step:

```bash
docker run -d \
  --name wartable \
  --restart unless-stopped \
  --platform linux/amd64 \
  -p 5110:5110 \
  -v wartable-data:/data \
  ghcr.io/alex98235/wartable:latest \
  --mode serve --host 0.0.0.0 --port 5110 --db /data/wartable.db

curl -fsS http://localhost:5110/api/health
```

Open <http://localhost:5110>. Logs are `docker logs -f wartable`. The database
lives in the `wartable-data` volume; recreating the container keeps it.

This follows the moving `latest` tag and is meant for a first run. See
[`quickstart/README.md`](quickstart/README.md) for the Compose option,
backup/update commands, port conflicts, and troubleshooting. For a controlled
deployment, pin an immutable version as described in
[`docs/deployment.md`](docs/deployment.md).

## Install a binary

Download `wartable-<version>-win-x64.zip` or
`wartable-<version>-linux-x64.tar.gz` from the Releases page, along with the
matching `.sha256` sidecar. The archive is a single self-contained binary with
the web UI embedded; no .NET runtime is required.

```bash
./wartable --port 5110                       # local only
./wartable --host 0.0.0.0 --port 5110        # reachable from your LAN
./wartable --mode mcp --db ~/.wartable/wartable.db
```

The Linux binary still needs the host's **glibc** and **ICU** libraries. Install
your distribution's ICU package (`libicu72` on Debian 12, `libicu74` on Ubuntu
24.04) before starting it. The Windows binary needs no extra packages.

## Documentation

| Document | What it covers |
| --- | --- |
| [`quickstart/README.md`](quickstart/README.md) | Canonical first run with Docker or Compose |
| [`docs/deployment.md`](docs/deployment.md) | Supported production contract: pinned image, bind mount, data, backup, updates |
| [`docs/container.md`](docs/container.md) | Container image tags, operations, volumes, smoke and update tests |
| [`docs/development.md`](docs/development.md) | Build, test, frontend dev loop, local image, release workflow |
| [`docs/design.md`](docs/design.md) | Product and domain design |

## Concepts

- **Campaign** — a folder for a body of work. It has intent but no status.
- **Objective** — a durable outcome inside a campaign, with a campaign-scoped
  alias (`#N`) and a `Draft` or `Active` plan state.
- **Step** — atomic, verifiable work with a stable reference (`#N.M`), explicit
  dependencies, and an `Open` / `InProgress` / `Done` status (plus `Flagged` and
  `Blocked`). Objective status is derived from its steps.
- **Bunker** — campaign knowledge and always-on directives, stored as notes with
  status, tags, links, and supersession.
- **Preferences** — advisory rules that apply to the human across campaigns. A
  campaign directive overrides a conflicting preference.
- **Briefing and Attention** — a bounded "what matters now" view of one campaign,
  and a cross-campaign inbox for flagged, blocked, open, and stale work.
- **Live updates** — every mutation is recorded as activity and pushed to the UI
  over server-sent events.

SQLite on the server is the single source of truth. Browsers are thin clients;
there are no accounts and no offline write queue.

## Connect an agent (OpenCode)

WarTable exposes 30 MCP tools over stdio and Streamable HTTP. The recommended
OpenCode V2 configuration for an agent on the same machine as the source
checkout is:

```json
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "servers": {
      "wartable": {
        "type": "local",
        "command": [
          "dotnet", "run",
          "--project", "C:/full/path/to/WarTable/WarTable.Api",
          "--", "--mode", "mcp"
        ]
      }
    }
  }
}
```

With a published binary, replace `command` with the executable and database
path:

```json
"command": ["C:/path/wartable.exe", "--mode", "mcp", "--db", "C:/Users/you/.wartable/wartable.db"]
```

For an agent on another machine, use the remote transport:

```json
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "servers": {
      "wartable": {
        "type": "remote",
        "url": "http://<server>:5110/mcp"
      }
    }
  }
}
```

`/mcp` is stateless and unauthenticated; it is intended for a trusted LAN. The
stdio process and the server must use the same local database path.

### Recommended system instruction

Add this to your agent instructions so the AI uses WarTable as the shared
planning and execution record:

> You have access to the WarTable MCP server. Use it to manage planning and work:
>
> - Use **campaigns** as project-wide folders, **objectives** as durable
>   outcomes, and **steps** as atomic verifiable work.
> - When you propose a multi-step plan, offer to import it with `import_plan`.
>   Draft objectives are visible but not actionable until activated.
> - Refer to objectives and Bunker notes by their campaign-scoped alias (`#N`) and
>   steps by `#N.M`. Never refer to work by title or count.
> - Before starting, call `get_briefing` for the campaign or `get_attention` across
>   campaigns. Read active directives and preferences first.
> - Move steps `Open` -> `InProgress` -> `Done` as you work. Use `add_step_update`
>   for notes, blockers, results, and handoffs; attach evidence when a step is
>   done. Flag work that needs human review.
> - Record durable decisions, gotchas, references, and open questions in the
>   campaign Bunker with `add_note`. Directives are hard, always-on campaign rules.
> - Preferences are advisory, human-global rules. If a campaign directive
>   conflicts with a preference, surface the conflict.
> - Objective status is derived from steps; do not set it directly.
> - WarTable does not enforce approval gates. Verify before implementation only if
>   your own instructions require it.

For the HTTP API and live-update internals, see
[`docs/design.md`](docs/design.md). For deployment, see
[`docs/deployment.md`](docs/deployment.md).
