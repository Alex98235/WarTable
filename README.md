# WarTable

WarTable is a shared planning surface for a human and their AI agents.

It is aimed at solo developers using agentic workflows who need a clearer
picture of how a project is taking shape. A **campaign** is the project.
**Objectives** are the durable outcomes; **steps** are the atomic, verifiable
work. The human and the agent see the same live plan, and the agent can update
it over MCP. WarTable gives that work a durable structure and a memory that
survives sessions.

It also stores the context around the work:

- **Preferences** — global soft rules about how you like to work, worth
  remembering between sessions.
- **Bunker notes** — campaign-specific knowledge: directives, decisions,
  gotchas, references, and open questions.

Built with C# ASP.NET Minimal API, EF Core + SQLite, Svelte 5, and the official
MCP C# SDK. The product and domain design lives in
[`docs/design.md`](docs/design.md); the implementation map lives in
[`docs/architecture.md`](docs/architecture.md).

## Table of Contents
<!-- TOC -->

- [WarTable](#wartable)
  - [Table of Contents](#table-of-contents)
  - [30 second start](#30-second-start)
  - [Install a binary](#install-a-binary)
  - [Core concepts](#core-concepts)
  - [Documentation](#documentation)
  - [Connect an agent (OpenCode)](#connect-an-agent-opencode)
  - [Optional semantic search](#optional-semantic-search)
  - [License and security](#license-and-security)

<!-- /TOC -->

## 30 second start

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

This follows the moving `latest` tag and is meant for a first run. For the
Compose option, everyday commands, updates, port conflicts, and troubleshooting,
see [`quickstart/README.md`](quickstart/README.md). For a controlled deployment,
pin an immutable version as described in
[`docs/deployment.md`](docs/deployment.md).

## Install a binary

Download `wartable-<version>-win-x64.zip` or
`wartable-<version>-linux-x64.tar.gz` from the
[Releases page](https://github.com/Alex98235/WarTable/releases), along with the
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

## Core concepts

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

## Documentation

[`docs/index.md`](docs/index.md) is the full map, including which document owns
each topic. The short version:

| Document | Owns |
| --- | --- |
| [`quickstart/README.md`](quickstart/README.md) | Canonical first run with Docker or Compose |
| [`docs/configuration.md`](docs/configuration.md) | CLI flags, `.env`, environment keys, Compose substitution |
| [`docs/agent-instructions.md`](docs/agent-instructions.md) | Copyable fallback agent instructions |
| [`docs/design.md`](docs/design.md) | Product and domain design |
| [`docs/architecture.md`](docs/architecture.md) | Codebase structure, HTTP API, and live updates |
| [`docs/deployment.md`](docs/deployment.md) | Supported production contract: pinned image, data, backup, updates |
| [`docs/container.md`](docs/container.md) | Container tags, operations, volumes, smoke and update tests |
| [`docs/development.md`](docs/development.md) | Build, test, frontend dev loop, local image, release workflow |
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | Contribution workflow and review expectations |

## Connect an agent (OpenCode)

WarTable exposes MCP tools over stdio and Streamable HTTP. The recommended
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

Or use the remote transport for LAN deployments:

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

The server sends baseline usage guidance during the MCP `initialize` handshake.
For clients that ignore server instructions, copy the fallback prompt from
[`docs/agent-instructions.md`](docs/agent-instructions.md) into your agent
configuration.

## Optional semantic search

WarTable can index Bunker notes and answer "have we said anything about X?"
semantically as well as by keyword. The embedding model is **not bundled**:
WarTable talks to any standalone service that exposes the OpenAI-compatible
`POST /v1/embeddings` contract (Ollama, llama.cpp, LM Studio, vLLM, or a hosted
provider). WarTable owns chunking, the disposable sidecar index, ranking, and
keyword fallback when the service is unavailable.

Enable it with `Embeddings__Enabled=true` and an endpoint. The complete key
list, defaults, `.env` behavior, and Compose guidance are in
[`docs/configuration.md`](docs/configuration.md#semantic-search). The design
rationale is in
[`docs/design.md`](docs/design.md#semantic-knowledge-search).

## License and security

WarTable is released under the [MIT License](LICENSE).

It has no accounts, login, API keys, or TLS. Anyone who can reach the port can
read and change campaign data, so run it only on a trusted LAN. Report
vulnerabilities as described in [`SECURITY.md`](SECURITY.md); the supported
network posture is in
[`docs/deployment.md`](docs/deployment.md#7-network-and-security).

For contribution workflow, see [`CONTRIBUTING.md`](CONTRIBUTING.md).
