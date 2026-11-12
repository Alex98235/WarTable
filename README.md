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
  gotchas, and context.

Built with C# ASP.NET Minimal API, EF Core + SQLite, Svelte 5, and the official
MCP C# SDK. The product and domain design lives in
[`docs/design.md`](docs/design.md); the implementation map lives in
[`docs/architecture.md`](docs/architecture.md).

## Table of Contents
<!-- TOC -->

- [WarTable](#wartable)
  - [Table of Contents](#table-of-contents)
  - [Run it](#run-it)
  - [Core concepts](#core-concepts)
  - [Documentation](#documentation)
  - [License and security](#license-and-security)

<!-- /TOC -->

## Run it

- **Container:** download the root [`docker-compose.yml`](docker-compose.yml)
  and run `docker compose up -d` — see the
  [setup guide](https://alex98235.github.io/WarTable/setup).
- **Standalone binary:** download a self-contained archive from the
  [Releases page](https://github.com/Alex98235/WarTable/releases) — see the
  [standalone guide](https://alex98235.github.io/WarTable/standalone).
- **An agent:** point an MCP client at the server — see
  [embedding WarTable in an agent workflow](https://alex98235.github.io/WarTable/embedding).

Configuration and upgrades live on the same site:
[configuration](https://alex98235.github.io/WarTable/configuration) and
[updating](https://alex98235.github.io/WarTable/updating).

## Core concepts

- **Campaign** — a folder for a body of work. It has a charter but no status.
- **Objective** — a durable outcome inside a campaign, with a campaign-scoped
  alias (`#N`) and a `Seed`, `Draft`, or `Active` plan state. A seed is a captured
  unfinished thought: visible, but not actionable until drafted.
- **Step** — atomic, verifiable work with a stable reference (`#N.M`), explicit
  dependencies, and an `Open` / `InProgress` / `Done` status (plus `Flagged` and
  `Blocked`). Objective status is derived from its steps.
- **Bunker notes** — campaign knowledge in four kinds: `Directive`, `Decision`,
  `Gotcha`, and `Context`. Notes are Markdown with tags, links, and
  supersession; their status is derived, not stored.
- **Preferences** — advisory rules that apply to the human across campaigns. A
  campaign directive overrides a conflicting preference.
- **Evidence** — proof that lives outside the repository, recorded as a `Link`
  or a `Commit`.
- **Briefing and Attention** — a bounded "what matters now" view of one campaign,
  and a cross-campaign inbox for flagged, blocked, open, and stale work.
- **Live updates** — every mutation is recorded as activity and pushed to the UI
  over server-sent events.

SQLite on the server is the single source of truth. Browsers are thin clients;
there are no accounts and no offline write queue.

## Documentation

[`docs/index.md`](docs/index.md) is the full map. User-facing procedure lives on
the [documentation site](https://alex98235.github.io/WarTable/); this repository
keeps the design and contributor references.

**Using WarTable**

| You want to | Read |
| --- | --- |
| Install and run the container | [Setup](https://alex98235.github.io/WarTable/setup) |
| Run a standalone binary | [Standalone](https://alex98235.github.io/WarTable/standalone) |
| Configure it | [Configuration](https://alex98235.github.io/WarTable/configuration) |
| Upgrade it | [Updating](https://alex98235.github.io/WarTable/updating) |
| Connect an agent | [Embedding](https://alex98235.github.io/WarTable/embedding) |

**Contributing and internals**

| Document | Owns |
| --- | --- |
| [`docs/design.md`](docs/design.md) | Product and domain design |
| [`docs/architecture.md`](docs/architecture.md) | Codebase structure, HTTP API, and live updates |
| [`docs/configuration.md`](docs/configuration.md) | Configuration contract: `.env` precedence, environment keys, Compose substitution |
| [`docs/deployment.md`](docs/deployment.md) | Supported production contract: pinned image, data, backup, updates |
| [`docs/container.md`](docs/container.md) | Container tags, operations, volumes, smoke and update tests |
| [`docs/development.md`](docs/development.md) | Build, test, frontend dev loop, local image, release workflow |
| [`docs/agent-instructions.md`](docs/agent-instructions.md) | Where the fallback agent instruction lives and how to keep it in sync |
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | Contribution workflow and review expectations |

## License and security

WarTable is released under the [MIT License](LICENSE).

It has no accounts, login, API keys, or TLS. Anyone who can reach the port can
read and change campaign data, so run it only on a trusted LAN. Report
vulnerabilities as described in [`SECURITY.md`](SECURITY.md); the supported
network posture is in
[`docs/deployment.md`](docs/deployment.md#7-network-and-security).

For contribution workflow, see [`CONTRIBUTING.md`](CONTRIBUTING.md).
