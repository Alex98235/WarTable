# WarTable Deployment Contract

**Scope:** One self-hosted WarTable server on a trusted LAN, plus local agents
that connect over MCP stdio. There are no accounts and no cloud service. This
document is the source of truth for a supported production deployment.

## Quickstart versus production

These are two different shapes, and only one of them is the production contract:

| | Quickstart | Production |
|---|---|---|
| Guide | [`quickstart/README.md`](../quickstart/README.md) | This document |
| Image tag | `latest` (moving) | Immutable `X.Y.Z` |
| Data | Named volume `wartable-data` | Bind mount `./data:/data` |
| Configuration | No `.env`, no checkout required | `.env` with `WARTABLE_TAG` |
| Updates | Pull and recreate by hand | [`scripts/update.sh`](../scripts/update.sh) with backup and rollback |
| Intended use | First run, evaluation, trusted-LAN convenience | Always-on server whose data matters |

The quickstart is a convenience path. Switch to the pinned, bind-mounted
production setup before relying on backups, controlled updates, or rollback.

---

## 1. Decisions at a glance

| Area | Decision |
|---|---|
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

```text
desktop browser ─┐
desktop agent  ──┤
                 ├──> WarTable server (LAN, container) ──> SQLite + WAL
laptop browser ──┤
laptop agent   ──┘
```

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
|---|---|
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

The image and Compose file are deliverables of the production contract.

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
- No secrets, API keys, or environment-specific configuration are required.

The image does not need `sqlite3` or a shell for normal operation. Cold backups
are taken from the host. A shell may be present for debugging, but it is not an
operational dependency. CI publishes the image to the public GHCR package
`ghcr.io/alex98235/wartable`.

### 5.2 Compose

The Compose file must pin an immutable version tag, not `latest`:

```yaml
services:
  wartable:
    platform: linux/amd64
    image: ghcr.io/alex98235/wartable:${WARTABLE_TAG:?set WARTABLE_TAG}
    container_name: wartable
    restart: unless-stopped
    ports:
      - "5110:5110"
    volumes:
      - ./data:/data
    command:
      - --mode
      - serve
      - --host
      - 0.0.0.0
      - --port
      - "5110"
      - --db
      - /data/wartable.db
    environment:
      Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command: Warning
    healthcheck:
      test: ["CMD", "curl", "-fsS", "http://localhost:5110/api/health"]
      interval: 30s
      timeout: 5s
      retries: 3
      start_period: 10s
```

`.env`:

```dotenv
WARTABLE_TAG=0.1.0
```

Pin the immutable `X.Y.Z` release being deployed; `0.1.0` is the first final
release. `X.Y` is a moving minor alias; `latest` is opt-in testing only and must
never be the documented production default.

For the no-checkout first run that uses `latest` and a named volume, see the
[quickstart](../quickstart/README.md). The quickstart is deliberately not the
production shape; switch to this pinned, bind-mounted setup before relying on
backups or [`scripts/update.sh`](../scripts/update.sh).

For a local build from the checkout, compose the build override with the base
file. It adds the local `build` context and tags the image
`wartable:${WARTABLE_TAG}`:

```bash
docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build
```

The base `docker-compose.yml` never builds; production always pulls the
published image.

See [`container.md`](container.md) for build and operation commands.

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
- Do not rely on the automatic schema backup as the only copy. It only runs
  when the schema version changes.

### 8.3 Restore

1. Stop the service.
2. Move the current `data/` contents aside; do not merge old and new WAL files.
3. Extract the chosen backup into `data/`.
4. Start the service.
5. Check `/api/health`, then open the UI and confirm a known campaign.

### 8.4 Schema changes

WarTable has no migrations. Startup stamps a schema version. If the stored
schema does not match the current version, the database is copied to
`wartable.db.bak-<timestamp>` and a new empty database is created.

Consequences:

- A release that bumps the schema version **discards the live data** after
  taking its automatic backup.
- Release notes must state the schema version and whether the update recreates
  the database.
- Take a cold backup before any update, even when the release claims to be
  compatible.
- If the data matters and a schema bump is announced, do not update until a
  migration or an export path exists. That is a deliberate limitation of the
  current product, not an accident.

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

- Releases are driven by annotated Git tags of the form `vX.Y.Z`.
- CI publishes the public `linux/amd64` image to
  `ghcr.io/alex98235/wartable` from the tagged commit. Container tags: `X.Y.Z`
  (immutable), `X.Y` (minor alias), and `latest` (moving, testing only).
  Pre-release tags do not move `latest`.
- Standalone archives are named `wartable-<version>-win-x64.zip` and
  `wartable-<version>-linux-x64.tar.gz`, each published with a `.sha256`
  sidecar checksum (for example `wartable-0.1.0-linux-x64.tar.gz.sha256`).
  The image and the standalone archives share the release version.
- Production Compose pins `WARTABLE_TAG` to `X.Y.Z`. `latest` is opt-in and
  must never be the default in the documented deployment. A local build uses the
  `docker-compose.build.yml` override with the same tag.
- A release is not complete until both the image and the standalone binaries
  are available and smoke-tested.
- Release notes include:
  - the container image reference and digest
  - the binary archive checksums
  - the database schema version and whether it changed since the previous tag,
    including the backup-and-recreate warning when it did
  - a full-changelog compare link between the previous and current tag
  - any change to ports, paths, or MCP transport
  - upgrade and rollback steps

Re-run the image smoke test at any time with the manual workflow. It pulls an
already-published image, checks health, campaign creation, MCP
`initialize`/`tools/list`, a container restart, and database persistence:

```bash
gh workflow run smoke-image.yml -f image=ghcr.io/alex98235/wartable:0.1.0
gh run watch
```

The public package pulls anonymously. The workflow keeps a GHCR login for
private forks and to avoid anonymous rate limits; see
[`container.md#private-fork-or-private-package`](container.md#private-fork-or-private-package)
and [`container.md#smoke-test-the-published-image`](container.md#smoke-test-the-published-image).

The version-to-version update test is described in
[`container.md#version-to-version-update-test`](container.md#version-to-version-update-test).

---

## 11. Manual updates and rollback

Automatic updates are explicitly out of scope. Do not add Watchtower, a
self-update endpoint, GitHub polling, or a timer that pulls and restarts the
service. Updates are manual and deliberate.

### 11.1 Prerequisites

- Linux host with Bash 4 or newer.
- Docker Engine with Compose v2, plus `curl`, `tar`, and `gzip` on `PATH`.
- The production [`docker-compose.yml`](../docker-compose.yml), which pulls the
  `linux/amd64` image from the public GHCR package. Do not use the local-build
  override for a production update.
- `.env` in the repository root with an immutable `WARTABLE_TAG=X.Y.Z`.
- `data/wartable.db` in the bind-mounted data directory (default `./data`).
- GHCR access. The published package is public, so an anonymous pull works. For
  a private fork or package, authenticate with a token that has `read:packages`;
  see
  [`container.md#private-fork-or-private-package`](container.md#private-fork-or-private-package).
- A data directory owned by the container's runtime user, `10001:10001`:

  ```bash
  sudo chown -R 10001:10001 data
  ```

Run the script as root or as a user that can talk to the Docker daemon and read
the data directory. When run as root, a rollback restores the data ownership to
`10001:10001`. An unprivileged run stops with a sudo/ownership hint if the data
is unreadable; it never changes permissions on a live deployment.

### 11.2 Container update with the script

[`scripts/update.sh`](../scripts/update.sh) is the supported manual update path.

```bash
./scripts/update.sh <X.Y.Z> [--yes] [--dry-run] [--allow-latest]
```

Examples:

```bash
# Show the exact plan without pulling, stopping, or writing anything.
./scripts/update.sh <X.Y.Z> --dry-run

# Prompt for "yes" after showing current -> target.
./scripts/update.sh <X.Y.Z>

# Skip the prompt for a planned maintenance window.
./scripts/update.sh <X.Y.Z> --yes
```

The target must be an immutable full semantic version, `X.Y.Z`, with an optional
leading `v`. A pre-release suffix such as `0.1.0-rc.1` is accepted. `latest` is
rejected unless `--allow-latest` is passed; that option is for testing only and
must not be used for production. An equal target and current tag is a no-op and
exits nonzero without changing anything.

Unless `--yes` is given, the script prints the plan and requires typing `yes`.
`--dry-run` prints the plan and exits before any pull or mutation. The script:

1. Preflights the commands, `.env`, Compose file, and `data/wartable.db`.
2. Pulls `ghcr.io/alex98235/wartable:<target>` **before** stopping the service.
   A pull failure leaves the running service and `.env` untouched.
3. Stops the service and writes a cold backup to
   `backups/wartable-<UTC>-from-<current>-to-<target>.tar.gz`. It verifies that
   `wartable.db` is inside the archive before continuing. `backups/` is
   git-ignored.
4. Atomically replaces `WARTABLE_TAG` in `.env`.
5. Recreates with `docker compose up -d --remove-orphans` and polls `HEALTH_URL`
   (default `http://127.0.0.1:5110/api/health`) for `"status":"ok"`.
6. Prints the old tag, new tag, backup path, image digest, and a reminder to keep
   the previous image locally for rollback.

If the backup cannot be written or verified, the script restarts the current
service and exits without changing `.env`.

### 11.3 Failure behavior and rollback

Exit status:

- `0` — the update succeeded and the new version is healthy.
- `1` — the update failed without leaving the new version running. A failure
  before the backup leaves the service and `.env` untouched; a failure after the
  backup runs automatic rollback, which restores the previous tag, image, and
  data and waits for health.
- `3` — the update and automatic rollback both failed. The script prints manual
  recovery instructions; use them rather than guessing.

On failure after the backup, the script:

1. Dumps recent container logs.
2. Stops the new container.
3. Restores the cold backup into `data/`.
4. Preserves the failed data directory as `data.failed-<UTC>` for inspection.
5. Restores the previous `WARTABLE_TAG` in `.env`.
6. Starts the previous image, pulling it first if it is no longer local.
7. Waits for health again.

This is a manual, foreground operation: there is no watchdog, timer, or
automatic update process. If the rollback also fails, the script exits `3` and
prints the backup path, failed data path, previous tag, and a recovery outline.
Use the exact paths printed by the script; the shape is:

```bash
cd /path/to/WarTable
docker compose stop wartable
mv data "data.failed-$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p data
tar -xzf backups/wartable-<timestamp>-from-<old>-to-<new>.tar.gz -C data
sudo chown -R 10001:10001 data
# Set WARTABLE_TAG=<old> in .env, then:
WARTABLE_TAG=<old> docker compose --env-file .env -f docker-compose.yml \
  up -d --remove-orphans wartable
curl -fsS http://127.0.0.1:5110/api/health
```

### 11.4 Schema changes

Before any update:

1. Read the release notes.
2. Confirm the database schema version and whether it changed.
3. Take the cold backup even if the release claims compatibility. The script
   does this automatically; the manual fallback below does not.

WarTable has no migrations. If startup detects that the stored schema does not
match the current version, it copies the database to
`wartable.db.bak-<timestamp>` and creates a new empty database (see section
8.4). A schema bump therefore recreates the database and discards the live rows
after the automatic backup. A rollback restores the cold backup, not the new
empty database. An older WarTable version may not be able to read a database
created by a newer schema; do not update across a schema bump unless you accept
the reset or have an export path.

### 11.5 Manual Compose fallback

Use this only if the script cannot run or you need to intervene by hand. It does
not provide automatic rollback; the script is the primary path.

1. Read the release notes and confirm the schema version.
2. Take a cold backup (section 8.2).
3. Record the current `WARTABLE_TAG` and image ID.
4. Set `WARTABLE_TAG` in `.env` to the new immutable version.
5. Pull and recreate:

   ```bash
   docker compose pull
   docker compose up -d --remove-orphans
   ```

6. Wait for health:

   ```bash
   curl -fsS http://localhost:5110/api/health
   ```

7. Verify the UI, a campaign read, `/mcp` `tools/list`, and an SSE reconnect.
8. If anything fails, set `WARTABLE_TAG` back to the previous version and
   recreate. If the schema changed, restore the cold backup (section 8.3) before
   starting the old image. Keep the container logs.
9. Keep the previous image until the new version has survived normal use. Do not
   prune it during the update.

### 11.6 Standalone binary update

1. Take a cold backup.
2. Download the new archive and checksum; verify the checksum.
3. Stop the process or systemd unit.
4. Replace the binary; keep the previous binary until validation.
5. Start the process.
6. Check `/api/health` if serve mode is running, or run a stdio MCP smoke test.
7. Roll back by restoring the previous binary and the database backup if the
   schema changed.

### 11.7 Rollback limits

- The update script can roll back automatically only after it has taken the cold
  backup. Failures during the preflight or image pull happen before the service
  or `.env` is touched.
- Rolling back the image/binary is safe only if the database schema did not
  change. A schema bump recreates the database; the old version cannot read the
  new file, and the new file is empty unless restored.
- The cold backup is the rollback path for data. The image tag is the rollback
  path for code.
- Manual Compose updates have no automatic rollback watchdog. The supported
  update script is the path that provides one.

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

See section 11.2 for the supported update script, or section 11.5 for the
manual Compose fallback.

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
- database migrations; schema changes currently reset the database after a
  backup
- a separate "issue tracker" entity; actionable work is an Objective, and
  Bunker notes are knowledge
