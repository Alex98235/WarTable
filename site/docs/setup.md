---
sidebar_position: 2
title: Setup
---

# Setup

This page covers the first run of the container image and the first agent
connection. The server is a single `linux/amd64` container; the database lives in
a named Docker volume.

## Requirements

- A Linux host with Docker Engine and Compose v2.
- Port `5110` free on the host.
- A trusted LAN. WarTable has no accounts or TLS.

:::note
The Compose file publishes the port as `5110:5110`. If something else already
listens on `5110`, the container cannot start.
:::

## First run

Download only the root `docker-compose.yml`. There is no checkout, no `.env`, and
no host data directory to create:

```bash
curl -sSLO https://raw.githubusercontent.com/Alex98235/WarTable/main/docker-compose.yml
docker compose up -d
curl -fsS http://localhost:5110/api/health
```

Open [http://localhost:5110](http://localhost:5110). The health check answers
`{"status":"ok","service":"WarTable"}`.

The Compose file uses the named volume `wartable-data`, so recreating the
container keeps the database. `WARTABLE_TAG` defaults to `2`, which tracks
non-breaking releases in the current major. To pin an exact release, set
`WARTABLE_TAG` in a `.env` file beside the Compose file:

```dotenv
WARTABLE_TAG=2.1.3
```

Then run `docker compose up -d` again. See [Configuration](./configuration.md)
for the rest of the environment keys and [Updating](./updating.md) for the
upgrade procedure.

## Connect an agent

WarTable exposes MCP over Streamable HTTP at `/mcp` and over stdio with
`--mode mcp`.

For an agent on another machine, point it at the server:

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

The remote endpoint is stateless and unauthenticated. It is intended for a
trusted LAN.

For an agent on the same host as the database, use the standalone binary in
stdio mode and point `--db` at the same database file the server uses:

```json
"command": ["C:/path/wartable.exe", "--mode", "mcp", "--db", "C:/Users/you/.wartable/wartable.db"]
```

:::warning
The stdio process and the server must use the same local database path. Do not
run two HTTP servers against one database.
:::

The server sends baseline usage guidance during the MCP `initialize` handshake.
If your client ignores server instructions, copy the fallback block from
[Embedding WarTable in an agent workflow](./embedding.md) into the agent's
configuration.
