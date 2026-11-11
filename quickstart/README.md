# WarTable quickstart

The shortest path from nothing to a running WarTable server. This uses the
public `ghcr.io/alex98235/wartable:latest` image and a Docker named volume, so
you do **not** need to clone the repository, create a `.env` file for the
default run, make a host data directory, or run `chown`.

Use this to try WarTable or run it on a trusted LAN. It follows `latest`, which
moves when a release is published. For a controlled production deployment with
an immutable version, a bind-mounted data directory, and a tested update path,
use the [deployment contract](../docs/deployment.md) instead.

## Requirements

- A 64-bit Linux host with Docker Engine and the Docker Compose plugin
  (`docker compose version` should work).
- The host must be able to run `linux/amd64` images. ARM64 hosts are not
  supported.
- A trusted LAN. Do not expose this port directly to the internet.

## Start it

### Option 1: one command

This needs no file download, no checkout, and no `.env`:

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

The response should be `{"status":"ok","service":"WarTable"}`. Open
`http://localhost:5110` in a browser on the same machine, or
`http://<server-ip>:5110` from another machine on the LAN.

The database lives in the named volume `wartable-data`. To update, pull
`latest` and recreate the container with the same `docker run` command; the
volume keeps the database.

### Option 2: Compose file

Download the one-file Compose deployment into an empty directory:

```bash
mkdir wartable
cd wartable
curl -fsSLO https://raw.githubusercontent.com/Alex98235/WarTable/refs/heads/main/quickstart/docker-compose.yml
docker compose up -d
docker compose ps
curl -fsS http://localhost:5110/api/health
```

The Compose file uses the same `latest` image and `wartable-data` volume. It is
the only file required for the default run. An optional `.env` beside it can
enable semantic search.

## Everyday commands

The container is named `wartable` in both options, so these work either way:

```bash
docker logs -f wartable       # follow logs
docker ps --filter name=wartable
docker restart wartable
docker stop wartable          # stop, keep the data volume
docker rm wartable            # remove the container, keep the data volume
docker volume rm wartable-data  # delete the data too
```

If you used the Compose file, the equivalent is:

```bash
docker compose logs -f --tail=200
docker compose ps
docker compose restart
docker compose stop
docker compose down       # keeps the data volume
docker compose down -v    # deletes the data volume
```

`docker rm` and `docker compose down` are safe for the data; `docker volume rm`
and `docker compose down -v` are not.

## Update

Because the quickstart follows `latest`, pull the new image and recreate the
container. With the one-command option:

```bash
docker pull ghcr.io/alex98235/wartable:latest
docker rm -f wartable
# Re-run the same docker run command from "Option 1".
```

With the Compose file:

```bash
docker compose pull
docker compose up -d
```

The named volume keeps the database across the update. For a controlled upgrade
with a cold backup and automatic rollback, switch to the pinned production
setup and [`scripts/update.sh`](../scripts/update.sh), described in
[`docs/deployment.md`](../docs/deployment.md#11-manual-updates-and-rollback).

## Port already in use

The container listens on 5110 inside the container. If 5110 is already used on
the host, change only the host-side mapping. With the one-command option, use
`-p 5111:5110` in the `docker run` command. With Compose:

```bash
WARTABLE_PORT=5111 docker compose up -d
```

Then open `http://localhost:5111` and check
`curl -fsS http://localhost:5111/api/health`.

## Optional semantic search

Semantic search is off by default. To enable it with Compose, add the
`Embeddings__*` keys from `.env.example` to a `.env` beside `docker-compose.yml`
and recreate the service. Compose substitutes those values into the container
environment; the `.env` file itself is not mounted.

The complete key list, defaults, endpoint guidance (`host.docker.internal` for
a host service, a Compose service name for a container), and keyword fallback
behavior are in
[`docs/configuration.md`](../docs/configuration.md#semantic-search). For the
one-command path, pass the same values with `-e` and add
`--add-host=host.docker.internal:host-gateway`.

## Data and backups

The named volume is the entire data footprint. To take a cold backup or restore
one, use the procedures in the deployment contract rather than inventing a
volume copy: [Backups](../docs/deployment.md#82-backups) and
[Restore](../docs/deployment.md#83-restore). Data ownership and persistence are
covered in
[`docs/container.md`](../docs/container.md#data-ownership-and-persistence).

## Troubleshooting

**`exec format error` or an architecture warning.** The image is `linux/amd64`
only. Use an amd64 Linux host; Windows containers and ARM64 are not supported.

**The UI loads but the server is unhealthy.** Follow the logs:

```bash
docker logs -f wartable
```

**The database seems empty after removing the container.** `docker rm` and
`docker compose down` keep the named volume; only `docker volume rm` or
`docker compose down -v` removes it.

**The MCP endpoint.** Remote agents use Streamable HTTP at
`http://<server>:5110/mcp`. For a co-located stdio agent, run the standalone
binary against the same database path; see
[`docs/deployment.md`](../docs/deployment.md#92-co-located-agents).

## Security

WarTable has no accounts, login, API keys, or TLS. Anyone who can reach port
5110 can read and change the campaign data. Run it only on a trusted LAN. If you
need access from outside the LAN, put it behind a VPN or an authenticated
reverse proxy; that is outside the supported contract. See
[`SECURITY.md`](../SECURITY.md) and
[`docs/deployment.md`](../docs/deployment.md#7-network-and-security).

## Next steps

- [Configuration](../docs/configuration.md) — flags, `.env`, and embedding keys.
- [Agent instructions](../docs/agent-instructions.md) — connect an agent and
  copy the fallback system prompt.
- [Production deployment](../docs/deployment.md) — pinned image, bind mount,
  backup, updates, and rollback.
- [Container operations](../docs/container.md) — tags, volumes, logs, and image
  verification workflows.
- [Development](../docs/development.md) — build, test, local image, and release.
