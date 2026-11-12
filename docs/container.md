# Container Operations

The container is the supported LAN server for the web UI, HTTP API, MCP
Streamable HTTP at `/mcp`, and SSE at `/api/events`. This page is the
container-specific companion: image tags, image builds, running the container,
volumes and health, and the image smoke test.

The production contract — topology, runtime requirements, the Compose service
definition, data and backup rules, schema lifecycle, updates, and the network
boundary — is [`deployment.md`](deployment.md). Environment variables are
catalogued in [`configuration.md`](configuration.md). For building and releasing
from source, see [`development.md`](development.md).

The image is `linux/amd64` only. ARM64, Windows containers, and multi-arch
manifests are out of scope.

## Image source and tags

Production Compose pulls the public `ghcr.io/alex98235/wartable` image, published
by CI when a release tag is pushed. An anonymous `docker pull` works.

Container tags map to the release version shared by the image and the standalone
archives:

- `X.Y.Z` — immutable production release. Pin this.
- `X.Y` — moving alias for the latest patch of a minor line.
- `latest` — moving tag, opt-in testing only. Never the production default.

Production pins an immutable `X.Y.Z` through `WARTABLE_TAG`; the base Compose
file never defaults to `latest`.

## Running the container

The service definition is the root [`docker-compose.yml`](../docker-compose.yml).
Do not keep a second copy of it in documentation; read the file for the current
ports, volume, command line, environment, and health check. For the first run,
download the root [`docker-compose.yml`](../docker-compose.yml); `WARTABLE_TAG`
defaults to `2` and the container uses a named volume.

Production start, data-directory ownership, `.env` contents, backup, and update
procedure are all owned by [`deployment.md`](deployment.md):

- [Compose contract and `.env`](deployment.md#52-compose)
- [Data volume ownership](deployment.md#53-data-volume)
- [Backups and restore](deployment.md#82-backups)
- [Upgrading](https://alex98235.github.io/WarTable/updating)
- [LAN-only network boundary](deployment.md#7-network-and-security)

Validate the file without starting anything:

```bash
docker compose --env-file .env config
```

The container runs `wartable` in serve mode with
`--host 0.0.0.0 --port 5110 --db /data/wartable.db`, publishes `5110`, mounts
the named volume `wartable-data` at `/data`, and restarts `unless-stopped`.

## Container operations

```bash
docker compose logs -f --tail=200     # logs go to stdout/stderr
docker compose restart
docker compose stop
docker compose down                   # keeps the wartable-data volume
docker compose exec wartable id       # uid=10001 gid=10001
docker compose exec wartable sh       # debugging shell
curl -fsS http://localhost:5110/api/health
```

The image health check performs an HTTP GET to
`http://127.0.0.1:5110/api/health` every 30 seconds using a static BusyBox
`wget`, so the runtime image does not need `curl` or an apt step. A healthy
response is HTTP 200 with `{"status":"ok"}`.

`Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command` is set to
`Warning` in Compose so idle operation does not log every SQL statement.

The optional semantic-search sidecar (`/data/wartable.embeddings.db`) lives in
the same volume. Enabling or tuning it is a configuration concern; see
[`configuration.md`](configuration.md) and
[`deployment.md` section 5.4](deployment.md#54-optional-semantic-search).

## Data ownership and persistence

- The database is `/data/wartable.db` plus its `-wal` and `-shm` sidecars.
- The container runs as UID/GID `10001`. The named volume `wartable-data` is
  owned by that user because Docker copies the image's `/data` ownership into a
  fresh volume; a bind mount used instead must be writable by that user.
- Do not store the database in the container's writable layer.
- A recreated container keeps its data as long as the named volume is retained.
- `/data` must be on a local disk. SQLite WAL/file locking is unsafe on NFS or
  SMB, and the live-update watcher relies on local filesystem notifications.

## Image build

The base Compose file always pulls a published image. Two local build paths
exist.

Compose override (builds from the checkout and starts the service):

```bash
docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build
```

It tags the result `wartable:${WARTABLE_TAG}`. Direct build:

```bash
docker build --platform linux/amd64 -t wartable:local .
```

The image has three stages: Node 22 builds the Svelte client, the .NET 10 SDK
publishes `wartable`, and the ASP.NET 10 runtime image runs as non-root user
`10001:10001`. [`development.md#local-container-build`](development.md#local-container-build)
covers the same build from the development loop's perspective.

## Smoke test the published image

[`smoke-image.yml`](../.github/workflows/smoke-image.yml) pulls an
already-published image and exercises it without building or pushing anything.
It starts the image with a fresh named volume, waits for `/api/health`, creates a
campaign, checks the MCP `initialize` response and `tools/list`, restarts the
container, and verifies that the campaign and `/data/wartable.db` survived.

Run it manually after a release, or against any published tag:

```bash
gh workflow run smoke-image.yml -f image=ghcr.io/alex98235/wartable:X.Y.Z
gh run watch
```

The job logs in to GHCR with `GITHUB_TOKEN` and `packages: read`; that login is
kept for private forks and to avoid anonymous pull rate limits. A successful run
ends with `IMAGE SMOKE OK image=... digest=... campaign=...`; a failure dumps the
container logs before the job exits.

## Upgrade procedure

Upgrading the container — cold backup, image pull, tag pin, health check, and
rollback — is documented on the site:
<https://alex98235.github.io/WarTable/updating>.

## Private fork or private package

The project package is public. If you publish a fork with a private package, or
hit anonymous rate limits, authenticate with a GitHub token that carries the
`read:packages` scope:

```bash
gh auth refresh -h github.com -s read:packages
gh auth token | docker login ghcr.io -u <github-user> --password-stdin
```

Never commit a token, put it in `.env`, or embed it in a Compose file. A
workflow in this repository can pull a private package with `GITHUB_TOKEN` when
the job grants `packages: read`.
