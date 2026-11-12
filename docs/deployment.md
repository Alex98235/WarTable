# WarTable Deployment Contract

**Scope:** One self-hosted WarTable server on a trusted LAN, plus local agents
that connect over MCP stdio. There are no accounts and no cloud service. This
document is the source of truth for a supported production deployment.

It owns topology, runtime requirements, supported distributions, the production
Compose contract, data and backup, schema lifecycle, updates and rollback,
release artifacts, the network boundary, and the runbook. It does not repeat
container build commands ([`container.md`](container.md)), the environment
variable catalogue ([`configuration.md`](configuration.md)), or local
development ([`development.md`](development.md)). Start at
[`index.md`](index.md) for the documentation map.

## First run

Download only the root [`docker-compose.yml`](../docker-compose.yml) — no checkout,
no `.env`, no `chown`:

```bash
curl -sSLO https://raw.githubusercontent.com/Alex98235/WarTable/main/docker-compose.yml
docker compose up -d
curl -fsS http://localhost:5110/api/health
```

Open <http://localhost:5110>. The container uses the named volume `wartable-data`
by default, so recreating it keeps the database. `WARTABLE_TAG` defaults to `2`,
which tracks non-breaking releases in the major. Production deployments pin an
immutable `X.Y.Z` tag via `.env`; see [section 5.2](#52-compose).

For upgrade instructions, see [`updates.md`](updates.md).

---

## 1. Decisions at a glance

| Area | Decision |
| --- | --- |
| Server host | Linux `amd64`; Docker Engine with Compose v2 already installed |
| Server distribution | Public container image, run with Docker Compose |
| Agent distribution | Self-contained standalone binary for local stdio |
| Browser access | Thin clients on the same LAN; no offline writes |
| Network | LAN-only HTTP on port `5110`; no public exposure |
| Data | SQLite + WAL on a local disk, persisted through a bind mount |
| MCP | Streamable HTTP at `/mcp` for remote agents; stdio for co-located agents |
| Updates | Manual only; no auto-pull, no Watchtower, no self-update |
| Versioning | Immutable semantic-version tags; `latest` is opt-in for testing |
| Backups | Cold backup of the data directory before every update |

---

## 2. Supported distributions

WarTable has **two distribution forms**, not two products. Both run the same
`WarTable.Api` binary and use the same SQLite schema.

### 2.1 Container image — the LAN server

The container is the supported way to run the always-on server:

- web UI
- HTTP API
- MCP Streamable HTTP at `/mcp`
- server-sent events at `/api/events`

The container is built for `linux/amd64` only. ARM64, Windows containers, and
multi-arch manifests are out of scope. CI publishes the image to the public
package `ghcr.io/alex98235/wartable`; anonymous pulls work.

### 2.2 Standalone binary — local agents and bare-metal

The standalone binary is the supported way for a co-located agent to use MCP
stdio:

```bash
./wartable --mode mcp --db /path/to/wartable.db
```

It can also run serve mode without Docker:

```bash
./wartable --host 0.0.0.0 --port 5110 --db /path/to/wartable.db
```

Artifacts are self-contained (`linux-x64` and `win-x64`) and require no .NET
runtime. The Windows binary is primarily for agents on the human's desktop or
laptop. The Linux binary can also back a bare-metal server.

"Self-contained" removes the .NET runtime dependency, not the operating-system
dependency. The Linux binary is still dynamically linked against glibc and the
native ICU globalization libraries. A minimal host or image without ICU fails
at startup with `Couldn't find a valid ICU package`. Install the distribution's
ICU runtime package first, for example `libicu72` on Debian 12 or `libicu74` on
Ubuntu 24.04. The Windows binary uses the ICU libraries that ship with Windows.

A stdio MCP process and the containerized server may share the same database
only when they run on the same host and the same local filesystem. That is the
intended co-located setup; it is not a remote-filesystem deployment.

---

## 3. Topology

- One HTTP server process owns the database for normal operation.
- Browsers are thin clients. They keep no persistent data and do not write
  offline.
- A co-located agent may run the standalone binary in stdio mode against the
  same database. SQLite serializes writes; alias allocation is atomic.
- Remote agents use MCP Streamable HTTP over the LAN. They do not need
  `docker exec`.
- The server pushes activity over SSE. Reconnecting clients resync from their
  last `since` cursor.

Do not run two HTTP servers against the same database. Multiple stdio MCP
processes on the same host are acceptable, but all writers must be on the same
local filesystem.

---

## 4. Runtime requirements

| Requirement | Value |
| --- | --- |
| Host architecture | `linux/amd64` for the container; `linux-x64` or `win-x64` for the binary |
| Container runtime | Docker Engine with Compose v2 |
| HTTP port | `5110` |
| Data path in container | `/data/wartable.db` |
| Default data path for binary | `~/.wartable/wartable.db` |
| Filesystem | Local disk only; no NFS, SMB, or other network share |
| Network | Trusted LAN; no public ingress |
| Accounts | None |
| TLS | Not provided by WarTable |
| Database | SQLite with WAL; no external database server |

The workload is single-human and low-volume. No specific CPU or memory floor is
claimed; size the host like a small always-on Linux service and measure if in
doubt.

The local-filesystem requirement is real:

- SQLite WAL and file locking do not behave correctly on network shares.
- The live-update watcher uses `FileSystemWatcher`/inotify and assumes local
  filesystem notifications.
- The broadcaster applies a short (200 ms by default) event-triggered debounce
  after a file signal so the SQLite transaction has committed before it queries
  `ActivityEvents`. This is not idle polling: the delay runs only after a signal
  and an idle feed issues no queries.
- A genuinely missed notification can delay live delivery until the next write;
  a reconnecting client recovers from the database. It must not be treated as a
  durable message bus.

---

## 5. Container contract

The image and the root [`docker-compose.yml`](../docker-compose.yml) are
deliverables of the production contract. The Compose file is the only copy of
the service definition; this section explains the requirements behind it.

### 5.1 Image

- Base runtime: `mcr.microsoft.com/dotnet/aspnet:10.0` (or the current .NET 10
  runtime image).
- Build stages: Node LTS for the Svelte client, .NET 10 SDK for `WarTable.Api`,
  then the ASP.NET runtime stage.
- Platform: `linux/amd64` only.
- Runtime user: non-root.
- Data directory: `/data`, writable by the runtime user.
- Exposed port: `5110`.
- Health check: `GET /api/health` must return HTTP 200 with
  `{"status":"ok"}`.
- Entry point: run `WarTable.Api` in serve mode with
  `--host 0.0.0.0 --port 5110 --db /data/wartable.db`.
- Logs: stdout/stderr only. The image must not require a log file.
- No secrets or API keys are required for the core server. Optional embedding
  providers may need `Embeddings__ApiKey`.

The image does not need `sqlite3` or a shell for normal operation. Cold backups
are taken from the host. A shell may be present for debugging, but it is not an
operational dependency.

### 5.2 Compose

The production service definition lives in
[`docker-compose.yml`](../docker-compose.yml). Do not fork a second copy of it
into documentation or deployment notes; change the file and let this page
describe the intent. It must keep these properties:

- `platform: linux/amd64` and `image:
  ghcr.io/alex98235/wartable:${WARTABLE_TAG:?set WARTABLE_TAG}`.
- An immutable tag is required. Compose fails fast when `WARTABLE_TAG` is unset,
  and the file never defaults to `latest`.
- `container_name: wartable`, `restart: unless-stopped`.
- Port `5110` published on the host.
- Bind mount `./data:/data` so the database and backups are host-visible.
- The serve-mode command line above with `--db /data/wartable.db`.
- `Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command: Warning`
  so idle operation does not log every SQL statement.
- `extra_hosts: host.docker.internal:host-gateway` for a host-side embedding
  service.
- A BusyBox `wget` health check against `http://127.0.0.1:5110/api/health`
  every 30 seconds.

`.env` beside the Compose file carries the release pin and any optional
overrides:

```dotenv
# Immutable release being deployed. Current releases are the Git tags and
# GitHub Releases in this repository; any X.Y.Z tag is valid.
WARTABLE_TAG=X.Y.Z
```

`X.Y.Z` is immutable, `X.Y` is a moving minor alias, and `latest` is opt-in
testing only. Never document or deploy `latest` in production. Compose reads
`.env` for `${...}` substitution and passes only the variables listed under
`environment` into the container; it is **not** mounted into the container.
[`configuration.md`](configuration.md) is the catalogue of every variable and
its defaults.

For the no-checkout first run, download the root
[`docker-compose.yml`](../docker-compose.yml); it defaults to the `2` tag and a
named volume. Production deployments switch to a bind mount and an immutable
`X.Y.Z` tag as described above. For upgrades, see [`updates.md`](updates.md); for
backups, see [section 8.2](#82-backups).

For a local build from the checkout, compose the build override with the base
file. It adds the local `build` context and tags the image
`wartable:${WARTABLE_TAG}`:

```bash
docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build
```

The base `docker-compose.yml` never builds; production always pulls the
published image. [`container.md`](container.md) covers direct image builds,
image tags, and container operations.

### 5.3 Data volume

- Default: bind mount `./data:/data`, so the database and backups are easy to
  inspect and copy.
- The host directory must be writable by the container's non-root user. Create
  it before first start and set ownership to the image's runtime UID/GID:

```bash
mkdir -p data
sudo chown -R 10001:10001 data
```

- Do not store the database inside the container's writable layer. A recreated
  container must keep the same data.
- A named volume is acceptable if you prefer it, but the default Compose file
  uses a bind mount.

### 5.4 Optional semantic search

Embeddings are disabled by default and are not required for health, planning, or
MCP. WarTable does not bundle a model; it calls an external OpenAI-compatible
`POST /v1/embeddings` service.

Deployment-specific points:

- The Compose file already forwards the `Embeddings__*` values and maps
  `host.docker.internal` to the Linux host gateway.
- `localhost` inside the container is the container itself. If Ollama runs as a
  Compose service, use its service name, for example
  `http://ollama:11434/v1`; otherwise use a reachable host or LAN address.
- The disposable index is `/data/wartable.embeddings.db` and is covered by the
  `./data` bind mount.
- After changing `.env`, recreate the service and verify:

```bash
docker compose up -d
curl -fsS http://localhost:5110/api/knowledge/status
```

If the service is unavailable or the index is still building, search falls back
to keyword results with `degraded: true`. Semantic search does not change the
container health check or the deployment contract. Settings, defaults, and
profiles are documented in [`configuration.md`](configuration.md).

---

## 6. Standalone binary contract

Release archives (the version is substituted from the release tag):

- `wartable-<version>-linux-x64.tar.gz`
- `wartable-<version>-win-x64.zip`
- A `.sha256` sidecar checksum alongside each archive.

The archives are self-contained and contain the web UI. They must not require
a .NET runtime.

Self-contained means "no .NET runtime install", not "no native OS libraries".
On Linux, install glibc and ICU; the usual distro runtime package is `libicu72`
on Debian 12 or `libicu74` on Ubuntu 24.04. Without ICU the binary exits with
`Couldn't find a valid ICU package`. The Windows archive needs no extra
components.

Serve mode:

```bash
./wartable --host 0.0.0.0 --port 5110 --db /var/lib/wartable/wartable.db
```

MCP stdio mode:

```bash
./wartable --mode mcp --db /var/lib/wartable/wartable.db
```

stdio rules:

- stdout is reserved for MCP JSON-RPC. Logs go to stderr.
- The process exits cleanly on stdin EOF.
- The agent client keeps stdin open for the lifetime of the session.

The Windows binary is the normal local-agent path. The Linux binary is for a
Linux local agent or an optional bare-metal server. A bare-metal server is
supported but is not the primary path; the container is.

Optional systemd unit for a bare-metal server:

```ini
[Unit]
Description=WarTable
After=network.target

[Service]
User=wartable
ExecStart=/opt/wartable/wartable --host 0.0.0.0 --port 5110 --db /var/lib/wartable/wartable.db
Restart=on-failure
WorkingDirectory=/var/lib/wartable

[Install]
WantedBy=multi-user.target
```

Agent-side client configuration is in
[`agent-instructions.md`](agent-instructions.md).

---

## 7. Network and security

- Bind the server to the LAN. In the container, `0.0.0.0:5110` is required;
  on a bare-metal server, prefer the LAN interface over `0.0.0.0` if the host
  has multiple interfaces.
- Firewall port `5110` to the trusted LAN. Do not forward it from the router.
- There are no accounts, no login, no API keys, and no authorization checks.
  Anyone who can reach the port can read and mutate the campaign.
- TLS is not provided by WarTable. The supported deployment is plain HTTP on a
  trusted LAN.
- Beyond-LAN access is **unsupported**. If you choose to add a VPN or an
  authenticated reverse proxy, that is your responsibility and outside the
  supported contract. WarTable must not be exposed directly to the public
  internet.
- Do not add CORS, public bind addresses, or auth bypasses as part of a
  deployment change without revisiting this contract.

The durable rule is LAN-only: no public exposure, no built-in accounts or TLS.
Report vulnerabilities as described in [`SECURITY.md`](../SECURITY.md).

---

## 8. Data, backup, restore, and schema changes

### 8.1 Data model

- SQLite database: `/data/wartable.db` in the container; configurable with
  `--db` for the binary.
- WAL mode is enabled for file-backed databases.
- `ActivityEvents` is the canonical live-update log; the SSE stream reads it
  through the file-watcher-driven broadcaster.
- The database is the only persistent state. Container images and binaries are
  replaceable.

### 8.2 Backups

A **cold backup** is required before every update:

```bash
docker compose stop wartable
tar -C data -czf "wartable-data-$(date +%Y%m%dT%H%M%S).tgz" .
docker compose start wartable
```

For a bare-metal server, stop the process and archive the database directory
the same way. The supported update script performs the cold backup
automatically and writes `backups/wartable-<UTC>-from-<current>-to-<target>.tar.gz`
before it changes the tag; see section 11.

A hot backup is optional:

```bash
sqlite3 data/wartable.db ".backup 'data/backups/wartable-$(date +%Y%m%dT%H%M%S).db'"
```

Requirements:

- Keep at least the backup taken before the current update and the one before
  that. Prune older backups only after the new version has been validated.
- A backup that has not been restore-tested is not a backup. Test the restore
  procedure at least once.
- Do not rely on the automatic pre-migration backup as the only copy. It lives
  next to the database on the same disk, and it is only as good as that disk.

### 8.3 Restore

1. Stop the service.
2. Move the current `data/` contents aside; do not merge old and new WAL files.
3. Extract the chosen backup into `data/`.
4. Start the service.
5. Check `/api/health`, then open the UI and confirm a known campaign.

### 8.4 Schema changes

WarTable applies EF migrations at startup. Before applying any pending migration
it checkpoints the WAL and copies the database to `wartable.db.bak-<timestamp>`.
If a migration throws, that copy is restored and startup aborts, so a failed
upgrade leaves the previous database in place.

Consequences:

- Schema changes carry live rows forward. A release that changes the schema
  normally keeps campaigns, objectives, steps, and notes.
- Release notes still state the schema generation (`SchemaVersion`), because it
  tells you which releases crossed a schema change.
- A failed migration is loud: the process exits, the previous database is in
  place, and the logs name the migration.
- Migrations are not a backup. A migration can succeed and still damage data, and
  only the cold backup returns you to the previous state.
- WarTable does not migrate down. Rolling back across a schema change means
  restoring the cold backup before starting the older build.
- A database stamped below schema generation `2` predates migrations, has no
  upgrade path, and is backed up and recreated. Only very old installs hit this.
- `--reset-db` discards the database on purpose, after a backup. Nothing discards
  it automatically.

---

## 9. MCP and live updates

### 9.1 Remote agents

- Endpoint: `POST http://<server>:5110/mcp`
- Transport: MCP Streamable HTTP, stateless.
- One JSON-RPC message per request; notifications return `202`.
- `GET` and `DELETE /mcp` return `405`.
- No `Mcp-Session-Id` is minted.
- No authentication. LAN trust is the security boundary.

The server also exposes MCP over stdio with `--mode mcp`. Both transports use
the official `ModelContextProtocol.AspNetCore` SDK and the same tool classes.
The tool surface itself is described in [`design.md`](design.md) and
[`agent-instructions.md`](agent-instructions.md).

### 9.2 Co-located agents

- Use the standalone binary with `--mode mcp`.
- Point `--db` at the same database path the server uses. In a container
  deployment, that means the host path that is bind-mounted to `/data`, or a
  binary running inside the same filesystem namespace. The normal setup is a
  stdio agent on the host against the host-side data directory.
- Do not use `docker exec` as the normal agent path. The remote HTTP endpoint
  is the supported path for agents that are not on the server host.

### 9.3 Live updates

- UI: `GET /api/events?campaignId=<id>&since=<sequence>`
- The server emits `id: <sequence>`, `event: activity`, and a JSON payload.
- A short signal debounce (200 ms by default) lets the SQLite transaction commit
  before the broadcaster drains `ActivityEvents`. The delay is triggered only by
  a file signal; an idle feed issues no queries.
- Reconnecting clients pass their last cursor as `since`.
- A client with no cursor starts at the current head; it does not replay the
  entire history.
- Slow SSE subscribers may be dropped. They recover by reconnecting with
  `since`.
- If a notification is genuinely missed (for example, a watcher overflow), live
  delivery waits for the next write; reconnecting with `since` recovers the
  event from the database.
- The file watcher is local-filesystem-only. A network-share database is not a
  supported deployment.

---

## 10. Versioning and release artifacts

- Releases are driven by annotated Git tags of the form `vX.Y.Z`. The current
  release is whatever tag (and matching GitHub Release) is newest; this document
  deliberately does not hard-code a version number.
- CI publishes the public `linux/amd64` image to
  `ghcr.io/alex98235/wartable` from the tagged commit. Container tags: `X.Y.Z`
  (immutable), `X.Y` (minor alias), and `latest` (moving, testing only).
  Pre-release tags do not move `latest`.
- Standalone archives are named `wartable-<version>-win-x64.zip` and
  `wartable-<version>-linux-x64.tar.gz`, each published with a `.sha256`
  sidecar checksum (for example `wartable-1.0.1-linux-x64.tar.gz.sha256`).
  The image and the standalone archives share the release version.
- Production Compose pins `WARTABLE_TAG` to `X.Y.Z`. `latest` is opt-in and
  must never be the default in the documented deployment. A local build uses the
  `docker-compose.build.yml` override with the same tag.
- A release is not complete until both the image and the standalone binaries
  are available and smoke-tested.
- Release notes include:
  - the container image reference and digest
  - the binary archive checksums
  - the database schema generation and whether it changed since the previous
    tag, with a note that startup migrates the database after taking a backup
  - a full-changelog compare link between the previous and current tag
  - any change to ports, paths, or MCP transport
  - upgrade and rollback steps

Re-run the image smoke test at any time with the manual workflow. It pulls an
already-published image, checks health, campaign creation, the MCP handshake,
a container restart, and database persistence:

```bash
gh workflow run smoke-image.yml -f image=ghcr.io/alex98235/wartable:X.Y.Z
gh run watch
```

The public package pulls anonymously. The workflow keeps a GHCR login for
private forks and to avoid anonymous rate limits; see
[`container.md#private-fork-or-private-package`](container.md#private-fork-or-private-package)
and [`container.md#smoke-test-the-published-image`](container.md#smoke-test-the-published-image).

The version-to-version update test is described in
[`container.md#version-to-version-update-test`](container.md#version-to-version-update-test).

---

## 11. Schema changes and rollback

The full upgrade routine - cold backup, image pull, tag pin, health check, and
rollback - is documented in [`updates.md`](updates.md). This section covers only
what a deployer needs to know about the schema lifecycle, which is part of the
deployment contract.

- WarTable applies EF Core migrations at startup. Before applying any pending
  migration it checkpoints the WAL and copies the database to
  `wartable.db.bak-<timestamp>`. If a migration throws, that copy is restored and
  startup aborts, so a failed upgrade leaves the previous database in place (see
  [section 8.4](#84-schema-changes)).
- Migrations never run backwards. Rolling back across a schema change means
  stopping the service, restoring the cold backup (section 8.2), and starting the
  older image.
- An archive taken before a migration is the only rollback path for data. A
  successful migration does not invalidate that archive; it is safe to restore.
- Restore the cold backup before starting the older build when the schema changed.

Automatic updates are explicitly out of scope. Do not add Watchtower, a
self-update endpoint, GitHub polling, or a timer that pulls and restarts the
service. Updates are manual and deliberate.

---

## 12. Operations runbook

### Start

```bash
docker compose up -d
docker compose ps
curl -fsS http://localhost:5110/api/health
```

### Logs

```bash
docker compose logs -f --tail=200
```

The EF command log is set to `Warning` in the Compose contract to avoid noisy
SQL output during idle operation.

### Stop

```bash
docker compose stop
```

### Restart

```bash
docker compose restart
```

### Backup

See section 8.2. Cold backup is the default.

### Upgrade

See [`updates.md`](updates.md).

### Uninstall

```bash
docker compose down
```

`docker compose down` keeps the bind-mounted `./data` directory. Do not add
`--volumes` unless you explicitly want to delete the data.

### Bare-metal service

If the standalone binary is used as a server, run it under systemd and use
`systemctl stop|start|status wartable`. The same backup and update rules apply.

---

## 13. Non-goals

The following are not supported and should not be added incidentally:

- Kubernetes, Nomad, or cloud hosting
- multi-tenant accounts, login, roles, or per-user data
- public-internet exposure, built-in TLS, or an authenticated reverse proxy
- automatic updates, Watchtower, self-update, or GitHub polling
- high availability, replicas, or multiple HTTP servers against one database
- ARM64 or multi-arch images
- Windows containers
- NFS/SMB/network-share databases
- offline browser writes or CRDT-style conflict resolution
- team collaboration or multi-writer conflict resolution
- a separate "issue tracker" entity; actionable work is an Objective, and
  Bunker notes are knowledge
