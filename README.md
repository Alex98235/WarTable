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

- **Campaign** — a folder for a body of work. It has a Charter note but no status.
- **Objective** — a durable outcome inside a campaign, with a campaign-scoped
  alias (`#N`) and a `Seed`, `Draft`, or `Active` plan state. A seed is a captured
  unfinished thought: visible, but not actionable until drafted.
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

## Semantic knowledge search (optional)

WarTable can index Bunker notes and answer "have we said anything about X?"
semantically as well as by keyword. The embedding model is **not bundled**:
WarTable talks to any standalone service that exposes the OpenAI-compatible
`POST /v1/embeddings` contract (Ollama, llama.cpp, LM Studio, vLLM, or a hosted
provider). The service is stateless; WarTable owns chunking, the disposable
sidecar index, ranking, and fallback.

Enable it in `WarTable.Api/appsettings.json`, a local `.env`, or with
environment variables:

```json
"Embeddings": {
  "Enabled": true,
  "Endpoint": "http://localhost:11434/v1",
  "Model": "embeddinggemma",
  "Dimensions": 768,
  "MaxInputTokens": 512
}
```

```bash
# Environment equivalent
Embeddings__Enabled=true
Embeddings__Endpoint=http://localhost:11434/v1
Embeddings__Model=embeddinggemma
Embeddings__Dimensions=768
```

For local development, copy `.env.example` to `.env` and set the values there:

```bash
cp .env.example .env
```

WarTable loads the nearest `.env` automatically at startup (walking up from the
current directory and application directory). Existing process environment
variables take precedence, so shell/CI overrides still win. Set
`WARTABLE_SKIP_DOTENV=1` to disable loading or `WARTABLE_ENV_FILE=/path/.env`
to point at a specific file. `.env` is gitignored.

When running through Docker Compose, `docker-compose.yml` passes the
`Embeddings__*` values into the container. Its endpoint default is
`http://host.docker.internal:11434/v1`, because `localhost` inside the
container means the container itself. On Linux the compose file adds
`host-gateway` for that name. If Ollama runs as a Compose service, set the
endpoint to that service instead, for example
`http://ollama:11434/v1`. Compose reads `.env` from the directory containing
the compose file and uses it for `${...}` substitution; the file is not
mounted into the container, so only variables listed under `environment`
reach WarTable. The same `.env` can configure local and container runs; use the
appropriate endpoint for each.

`Dimensions` is optional; if omitted, the first index pass uses the vector size
returned by the service. `Embeddings__ApiKey` is sent as a Bearer token when set.
The index lives in `wartable.embeddings.db` beside the main database and is safe
to delete; WarTable rebuilds it in the background.

Built-in prompt profiles cover EmbeddingGemma, BGE, E5, Nomic, Qwen3-Embedding,
OpenAI `text-embedding-3`, and raw text. Matching is by model id; override with
`Embeddings:Profile` or add `Embeddings:Profiles` entries with
`QueryTemplate` / `DocumentTemplate` (placeholders: `{text}`, `{title}`,
`{kind}`, `{tags}`, `{heading}`, `{campaign}`, `{instruction}`). The external
service only needs to turn text into vectors.

Search is hybrid by default: existing keyword matches plus semantic chunks,
merged with reciprocal rank fusion. If the service is down, disabled, or the
index is empty, the API returns keyword results with `degraded: true` rather
than failing.

```bash
curl 'http://localhost:5110/api/knowledge/search?q=rate+limit&campaignId=<id>'
curl 'http://localhost:5110/api/knowledge/status'
curl -X POST 'http://localhost:5110/api/knowledge/reindex'
```

Agents use the `search_knowledge` MCP tool. In the Bunker, turn on
`Semantic` and press Enter to search meaning across live notes; results are
evidence (note, alias, snippet, heading context, match source), not generated
answers.

## Connect an agent (OpenCode)

WarTable exposes 31 MCP tools over stdio and Streamable HTTP. The recommended
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
> - A campaign's **Charter** note is its durable framing: why it exists, scope,
>   and architecture. Read the charter and relevant `DesignDoc` notes before
>   proposing work. Update the charter with `update_note`; campaigns have no
>   separate intent field.
> - When you propose a multi-step plan, offer to import it with `import_plan`.
>   Draft objectives are visible but not actionable until activated.
> - Seeds are unfinished human thoughts captured before planning. When the briefing
>   lists seeds and no ready work is queued, pick one up: discuss it with the human,
>   then promote it to `Draft` or `Active` with `update_objective`.
> - Refer to objectives and Bunker notes by their campaign-scoped alias (`#N`) and
>   steps by `#N.M`. Never refer to work by title or count.
> - Before starting, call `get_briefing` for the campaign or `get_attention` across
>   campaigns. Use `get_plan` when you need the complete objective and step graph.
>   Read active directives, the charter, and preferences first.
> - Move steps `Open` -> `InProgress` -> `Done` as you work. Use `add_step_update`
>   for notes, blockers, results, and handoffs; attach evidence when a step is
>   done. Flag work that needs human review.
> - Record durable decisions, gotchas, references, and open questions in the
>   campaign Bunker with `add_note`. Use `DesignDoc` notes for component-level
>   design detail. Directives are hard, always-on campaign rules.
> - Before adding a note, or when the human asks whether something was already
>   discussed, use `search_knowledge` (hybrid semantic + keyword) and cite the
>   note alias and snippet rather than assuming.
> - Preferences are advisory, human-global rules. If a campaign directive
>   conflicts with a preference, surface the conflict.
> - Objective status is derived from steps; do not set it directly.
> - WarTable does not enforce approval gates. Verify before implementation only if
>   your own instructions require it.

This instruction applies to subagents too. Subagents do not inherit the parent
agent's prompt or conversation in OpenCode, OpenClaw, or similar clients. If you
are a subagent with WarTable tools, call `get_briefing` or `get_plan` yourself
rather than relying on the parent's summary.

For the HTTP API and live-update internals, see
[`docs/design.md`](docs/design.md). For deployment, see
[`docs/deployment.md`](docs/deployment.md).
