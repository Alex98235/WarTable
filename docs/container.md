# Container Operations

The container is the supported LAN server for the web UI, HTTP API, MCP
Streamable HTTP at `/mcp`, and SSE at `/api/events`. The deployment contract is
[`deployment.md`](deployment.md); this page is the container-specific companion.
For building and releasing, see [`development.md`](development.md).

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

The quickstart deliberately uses `latest`. Production pins an immutable `X.Y.Z`
through `WARTABLE_TAG`; the base Compose file never defaults to `latest`.

## Quickstart

The first-run path is [`../quickstart/README.md`](../quickstart/README.md). It
uses the public `latest` image and the named volume `wartable-data`, with no
`.env` or host data directory.

From a checkout, run it from the `quickstart` directory:

```bash
cd quickstart
docker compose up -d
```

Or copy [`quickstart/docker-compose.yml`](../quickstart/docker-compose.yml) into
an empty directory and run `docker compose up -d` there. Do **not** run the root
`docker compose up -d` for a first run: the root file is the pinned production
shape and expects `WARTABLE_TAG` and a bind-mounted `./data` directory.

## Production Compose

Run these commands from the repository root:

```bash
cp .env.example .env
# Set WARTABLE_TAG to the immutable release being deployed.
mkdir -p data
sudo chown -R 10001:10001 data   # Linux host; the container runs as UID/GID 10001
docker compose pull
docker compose up -d
docker compose ps
curl -fsS http://localhost:5110/api/health
```

Compose pulls `ghcr.io/alex98235/wartable:${WARTABLE_TAG}`. Validate the file
without starting anything:

```bash
docker compose --env-file .env config
```

The service publishes `5110`, bind-mounts `./data` to `/data`, restarts
`unless-stopped`, and runs:

```text
--mode serve --host 0.0.0.0 --port 5110 --db /data/wartable.db
```

### Optional semantic search

The Compose file passes these `Embeddings__*` values into the container and
reads them from `.env` when present:

```text
Embeddings__Enabled        default: false
Embeddings__Endpoint       default: http://host.docker.internal:11434/v1
Embeddings__Model          default: embeddinggemma
Embeddings__Dimensions     default: 768
Embeddings__MaxInputTokens default: 512
Embeddings__ApiKey         default: empty (sent as Bearer token when set)
Embeddings__Profile        default: auto (matches the model id)
```

Compose reads `.env` from the directory containing `docker-compose.yml` (the
repository root for production) and uses it for `${...}` substitution. The file
is **not** mounted into the container; only variables listed under
`environment` become process environment for WarTable.

Use `host.docker.internal`, not `localhost`, for an embedding service running on
the host: inside the container `localhost` is the container. The Compose file
adds the Linux `host-gateway` mapping for that name. If Ollama is a Compose
service, set the endpoint to its service name, for example
`http://ollama:11434/v1`. To enable semantic search:

```bash
# .env beside docker-compose.yml
Embeddings__Enabled=true
```

Then recreate the service and verify the index:

```bash
docker compose up -d
curl -fsS http://localhost:5110/api/knowledge/status
```

The index is `/data/wartable.embeddings.db`; it is disposable and rebuilds when
the model or profile changes. If the service is unavailable, search falls back
to keyword results with `degraded: true`.

## Operations

```bash
docker compose logs -f --tail=200     # logs go to stdout/stderr
docker compose restart
docker compose stop
docker compose down                   # keeps ./data
docker compose exec wartable id       # uid=10001 gid=10001
docker compose exec wartable sh       # debugging shell
curl -fsS http://localhost:5110/api/health
```

The image health check runs `curl -fsS http://localhost:5110/api/health` every
30 seconds. A healthy response is HTTP 200 with `{"status":"ok"}`.

`Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command` is set to
`Warning` in Compose so idle operation does not log every SQL statement.

## Data ownership and persistence

- The database is `/data/wartable.db` plus its `-wal` and `-shm` sidecars.
- The container runs as UID/GID `10001`; a bind-mounted host directory must be
  writable by that user. A named volume also works because Docker copies the
  image's `/data` ownership into it.
- Do not store the database in the container's writable layer.
- A recreated container keeps its data as long as the bind mount or named volume
  is retained.
- `/data` must be on a local disk. SQLite WAL/file locking is unsafe on NFS or
  SMB, and the live-update watcher relies on local filesystem notifications.

## Backup and restore

Use the cold-backup procedure in
[`deployment.md` section 8.2](deployment.md#82-backups): stop the service,
archive `data/`, then start it. Restore follows
[section 8.3](deployment.md#83-restore). Take a cold backup before every update.

## Update

[`scripts/update.sh`](../scripts/update.sh) is the supported manual update path.
It performs the preflight, target pull, cold backup, `.env` change, recreate,
health check, and automatic rollback described in
[`deployment.md` section 11](deployment.md#11-manual-updates-and-rollback).

```bash
./scripts/update.sh <X.Y.Z> --dry-run   # plan only
./scripts/update.sh <X.Y.Z>             # confirm with "yes"
./scripts/update.sh <X.Y.Z> --yes       # no prompt
```

The target must be a full immutable `X.Y.Z` tag, optionally prefixed with `v`.
`latest` requires `--allow-latest` for testing only. There is no automatic update
timer or watchdog.

## Local build

Build from the checkout with the Compose override:

```bash
docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build
```

It tags the result `wartable:${WARTABLE_TAG}`. The base file never builds.
[`development.md`](development.md#local-container-build) covers the direct
`docker build` command and the three build stages.

## Smoke test the published image

[`smoke-image.yml`](../.github/workflows/smoke-image.yml) pulls an
already-published image and exercises it without building or pushing anything.
It starts the image with a fresh named volume, waits for `/api/health`, creates a
campaign, checks the MCP `initialize` response and `tools/list` (31 tools),
restarts the container, and verifies that the campaign and `/data/wartable.db`
survived.

Run it manually after a release, or against any published tag:

```bash
gh workflow run smoke-image.yml -f image=ghcr.io/alex98235/wartable:0.1.0
gh run watch
```

The `image` input defaults to `ghcr.io/alex98235/wartable:0.1.0` when the
workflow is started from the UI. The job logs in to GHCR with `GITHUB_TOKEN` and
`packages: read`; that login is kept for private forks and to avoid anonymous
pull rate limits. A successful run ends with
`IMAGE SMOKE OK image=... digest=... campaign=... tools=31`; a failure dumps
the container logs before the job exits.

## Version-to-version update test

[`update-image.yml`](../.github/workflows/update-image.yml) is a manual
`workflow_dispatch` test for [`scripts/update.sh`](../scripts/update.sh). Both
`from_version` and `to_version` are required and must name two published
versions. The job starts `from_version` in an isolated temporary workspace,
seeds a campaign and Bunker note, upgrades to `to_version`, checks health, the
running image, `.env`, the seeded data, and the `wartable.db` backup archive,
then deliberately updates back to `from_version` and rechecks health and data.

```bash
gh workflow run update-image.yml -f from_version=<from> -f to_version=<to>
gh run watch
```

A successful run ends with
`UPGRADE OK from=<from> to=<to> rollback=<from> campaign=<id> backup=<path>`.

Published releases currently share one schema, so the workflow does not
exercise a real schema bump. It also does not force the automatic
health-failure rollback path; that path is covered by the local sandbox suite. A
real schema-bump upgrade remains untested until a release changes the schema.

## Network and security

- LAN-only plain HTTP on `5110`. Firewall it to the trusted LAN; do not forward
  it from the router.
- No accounts, login, API keys, authorization, or TLS are provided. Anyone who
  reaches the port can read and mutate the campaign.
- Beyond-LAN exposure is unsupported. A VPN or authenticated reverse proxy is
  outside the supported contract.

See [`deployment.md` section 7](deployment.md#7-network-and-security).

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
