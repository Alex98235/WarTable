---
sidebar_position: 4
title: Configuration
---

# Configuration

WarTable reads command-line flags, a `.env` file, environment variables, and
`appsettings.json`. The same keys work for a published binary and for the
container.

## Command-line flags

Flags control where the process runs. They are read before the web server
starts.

| Flag | Alias | Values | Default | Meaning |
| --- | --- | --- | --- | --- |
| `--mode` | `-m` | `serve`, `mcp` | `serve` | `serve` runs the web UI and HTTP API. `mcp` runs a stdio MCP server and does not listen on a port. |
| `--host` | `-H` | interface or hostname | `localhost` | Interface to bind in `serve` mode. Use `0.0.0.0` to accept LAN connections. |
| `--port` | `-p` | integer | `5110` | HTTP port in `serve` mode. |
| `--db` | — | file path | `~/.wartable/wartable.db` | SQLite database path. The parent directory is created if needed. |
| `--help` | `-h` | — | — | Print usage and exit. |

`--host` and `--port` are ignored in `mcp` mode. The stdio process and the web
server must point at the same database file to see the same data.

## `.env` and precedence

WarTable loads one dotenv file at startup, before the application host is built.
It looks for the file in this order:

1. The path in `WARTABLE_ENV_FILE`, when set.
2. The nearest `.env` walking upward from the current working directory.
3. The nearest `.env` walking upward from the application directory.
4. No file. Startup continues without one.

The file must be named `.env` for automatic discovery. A missing file is not an
error unless `WARTABLE_ENV_FILE` points at a path that does not exist.

Precedence, highest to lowest:

1. Existing process environment variables.
2. The loaded `.env` file.
3. `appsettings.json` and `appsettings.{Environment}.json`.
4. Built-in defaults.

Values already present in the process environment are never overwritten by
`.env`, so shell and CI overrides win. `.env` values do override
`appsettings.json` because they are applied as environment variables before the
host is built.

Two variables control loading itself. They are read from the process
environment, not from inside the dotenv file:

| Variable | Effect |
| --- | --- |
| `WARTABLE_SKIP_DOTENV` | Disables dotenv loading when set to `1`, `true`, `yes`, or `on` (case-insensitive). |
| `WARTABLE_ENV_FILE` | Path to a specific dotenv file. Relative paths resolve from the current working directory. Takes precedence over automatic discovery. |

Nested keys use `__` in environment variables and `:` in JSON. For example,
`Embeddings__Enabled` is `Embeddings:Enabled` in `appsettings.json`. Environment
variable names are case-insensitive on Windows and case-sensitive on Linux, so
match the documented casing on Linux.

## Docker Compose

Compose has two separate mechanisms:

- `${VAR}` substitution reads the `.env` beside the Compose file and the shell
  environment, and writes the value into the rendered Compose configuration.
- `environment:` entries decide which variables become process environment
  inside the container.

Compose reads `.env` from the directory containing the Compose file but does
**not** mount it into the container. Setting a key in `.env` alone does nothing
unless the Compose file references it under `environment:` or in a `${...}`
substitution.

The shipped `docker-compose.yml` forwards `WARTABLE_TAG` as the image tag and
passes the `Embeddings__*` keys listed under `environment:`. `WARTABLE_TAG` is
substitution-only; it is not an application setting inside the container.

Only `Enabled`, `Endpoint`, `Model`, `Dimensions`, `MaxInputTokens`, `ApiKey`,
and `Profile` are passed through by the Compose file. To use another
`Embeddings__*` key from `.env`, add it under `environment:` in your Compose
file, or pass it with `-e` on `docker run`.

Inside the container, `localhost` and `127.0.0.1` mean the container itself. Use
`host.docker.internal` for a service on the Docker host. If the embedding
service is another Compose service, use its service name, for example
`http://ollama:11434/v1`.

## Semantic search

:::note
Semantic search is optional and disabled by default. When enabled, WarTable
calls an OpenAI-compatible `POST /v1/embeddings` endpoint. The embedding model
is not bundled. WarTable owns chunking, the disposable index, ranking, and the
keyword fallback.
:::

| Environment key | Default | Meaning |
| --- | --- | --- |
| `Embeddings__Enabled` | `false` | Turns the background indexer and semantic search on. |
| `Embeddings__Endpoint` | `http://localhost:11434/v1` | Base URL of the embedding service. A full `/embeddings` URL also works. Compose defaults to `http://host.docker.internal:11434/v1`. |
| `Embeddings__Model` | empty | Model id sent to the service. Required when enabled. Compose defaults to `embeddinggemma`. |
| `Embeddings__Dimensions` | `0` | Expected vector size. `0` uses the matched profile's size, or the size reported by the service. Compose defaults to `768`. |
| `Embeddings__MaxInputTokens` | `0` | Chunk-size hint. `0` uses the matched profile's limit. |
| `Embeddings__BatchSize` | `32` | Notes sent per indexing batch. |
| `Embeddings__TimeoutSeconds` | `30` | HTTP timeout for embedding requests. |
| `Embeddings__PollSeconds` | `30` | How often the background worker reconciles notes into the index. |
| `Embeddings__TopK` | `8` | Default number of semantic matches returned. Clamped to 1–50. |
| `Embeddings__MinScore` | `0.2` | Minimum cosine score for a semantic match. |
| `Embeddings__SidecarPath` | empty | Index file path. Defaults to `wartable.embeddings.db` beside the main database. Safe to delete; it rebuilds. |
| `Embeddings__Profile` | empty | Overrides the prompt profile matched from the model id. |
| `Embeddings__ApiKey` | empty | Sent as a Bearer token when set. |

A local Ollama setup in `.env`:

```dotenv
Embeddings__Enabled=true
Embeddings__Endpoint=http://localhost:11434/v1
Embeddings__Model=embeddinggemma
Embeddings__Dimensions=768
```

After changing `.env`, recreate the service and check the index status:

```bash
docker compose up -d
curl -fsS http://localhost:5110/api/knowledge/status
```

### When semantic search is off

With `Embeddings__Enabled=false`, WarTable does not start the background
indexer, does not call an embedding service, and does not create the sidecar
index. Knowledge search still works by keyword. If the service is enabled but
unreachable, or the index is still building, search returns keyword results with
`degraded: true` instead of failing.

## Logging

Logging uses the standard .NET configuration stack. `appsettings.json` sets
`Default` to `Information` and `Microsoft.AspNetCore` to `Warning`. The Compose
file sets the EF command log to `Warning` to keep SQL noise out of container
logs.

```bash
Logging__LogLevel__Default=Debug
Logging__LogLevel__Microsoft.AspNetCore=Warning
Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Warning
```

`Logging__LogLevel__<Category>` accepts `Trace`, `Debug`, `Information`,
`Warning`, `Error`, `Critical`, or `None`.
