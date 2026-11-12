# Configuration

This page is the repository-side configuration contract: `.env` discovery and
precedence, the environment key catalogue, and Docker Compose substitution.
Product and domain design stay in [`design.md`](design.md); production operations
stay in [`deployment.md`](deployment.md).

For the user-facing walkthrough — command-line flags, copy-paste examples, and
per-shape setup — see the
[site configuration page](https://alex98235.github.io/WarTable/configuration).

WarTable starts from `WarTable.Api/appsettings.json`, then applies environment
configuration. At startup it also loads the nearest `.env` file (or an explicit
one) into the process environment without overwriting variables that already
exist. The same keys work for a local `dotnet run`, a published binary, and a
container.

## `.env` loading and precedence

WarTable loads a dotenv file once at startup, before the ASP.NET host is built.
Discovery order:

1. The path in `WARTABLE_ENV_FILE`, when set.
2. Otherwise, the nearest `.env` walking upward from the current working
   directory.
3. If none is found there, the nearest `.env` walking upward from the
   application directory.
4. If no file exists, startup continues without one.

The file must be named `.env` for automatic discovery. Missing files are not an
error unless `WARTABLE_ENV_FILE` points at a path that does not exist.

Precedence, highest to lowest:

1. Existing process environment variables.
2. The loaded `.env` file.
3. `appsettings.json` and `appsettings.{Environment}.json`.
4. Built-in defaults.

Values already present in the process environment are never overwritten by
`.env`, so shell and CI overrides win. `.env` values do override
`appsettings.json` because they are applied as environment variables before the
host is built. `.env` is gitignored. A commented template lives in
[`.env.example`](../.env.example).

Two variables control loading itself. They are read from the process
environment, not from inside the dotenv file:

| Variable | Effect |
| --- | --- |
| `WARTABLE_SKIP_DOTENV` | Disables dotenv loading when set to `1`, `true`, `yes`, or `on` (case-insensitive). Any other value leaves loading enabled. |
| `WARTABLE_ENV_FILE` | Absolute or relative path to a specific dotenv file. Relative paths resolve from the current working directory. Takes precedence over automatic discovery. |

## Environment keys

Nested configuration uses `__` as the separator in environment variables and
`:` in JSON. For example, `Embeddings__Enabled` is `Embeddings:Enabled` in
`appsettings.json`. Environment variable names are case-insensitive on Windows
and case-sensitive on Linux, so match the documented casing on Linux.

### Semantic search

Semantic search is optional and disabled by default. When enabled, WarTable
calls an OpenAI-compatible `POST /v1/embeddings` endpoint. The embedding service
is stateless and never owns the index; WarTable keeps a disposable SQLite
sidecar. See [`design.md`](design.md#semantic-knowledge-search) for the design.

| Environment key | appsettings key | Default | Meaning |
| --- | --- | --- | --- |
| `Embeddings__Enabled` | `Embeddings:Enabled` | `false` | Turns the background indexer and semantic search on. |
| `Embeddings__Endpoint` | `Embeddings:Endpoint` | `http://localhost:11434/v1` | Base URL of the embedding service. A full `/embeddings` URL also works. Compose defaults to `http://host.docker.internal:11434/v1`. |
| `Embeddings__Model` | `Embeddings:Model` | empty | Model id sent to the service. Required when enabled; Compose defaults to `embeddinggemma`. |
| `Embeddings__Dimensions` | `Embeddings:Dimensions` | `0` | Expected vector size. `0` uses the matched profile's size, or the size reported by the service when the profile does not define one. |
| `Embeddings__MaxInputTokens` | `Embeddings:MaxInputTokens` | `0` | Chunk-size hint. `0` uses the matched profile's limit (512–8192 depending on the profile). |
| `Embeddings__BatchSize` | `Embeddings:BatchSize` | `32` | Notes sent per indexing batch. |
| `Embeddings__TimeoutSeconds` | `Embeddings:TimeoutSeconds` | `30` | HTTP timeout for embedding requests. |
| `Embeddings__PollSeconds` | `Embeddings:PollSeconds` | `30` | How often the background worker reconciles notes into the index. |
| `Embeddings__TopK` | `Embeddings:TopK` | `8` | Default number of semantic matches returned. Clamped to 1–50. |
| `Embeddings__MinScore` | `Embeddings:MinScore` | `0.2` | Minimum cosine score for a semantic match. |
| `Embeddings__SidecarPath` | `Embeddings:SidecarPath` | empty | Index file path. Defaults to `wartable.embeddings.db` beside the main database. Safe to delete; it rebuilds. |
| `Embeddings__Profile` | `Embeddings:Profile` | empty | Overrides the prompt profile that would otherwise be matched from the model id. |
| `Embeddings__ApiKey` | `Embeddings:ApiKey` | empty | Sent as a Bearer token when set. |
| `Embeddings__Profiles__N__*` | `Embeddings:Profiles` | empty | Custom profile entries. Use a numeric index for `N` in environment variables. |

Built-in prompt profiles cover EmbeddingGemma, BGE, E5, Nomic, Qwen3-Embedding,
OpenAI `text-embedding-3`, and raw text. Matching is by model id. A custom
profile has these fields:

| Field | Default | Meaning |
| --- | --- | --- |
| `Name` | `custom` | Profile name stored in the index fingerprint. |
| `Match` | escaped name | Regular expression matched against the model id. |
| `Dimensions` | `0` | Expected vector size; `0` lets the service decide. |
| `MaxInputTokens` | `512` | Chunk-size limit. |
| `QueryTemplate` | `{text}` | Template for queries. Placeholders: `{text}`, `{title}`, `{kind}`, `{tags}`, `{heading}`, `{campaign}`, `{instruction}`. |
| `DocumentTemplate` | `{text}` | Template for indexed note chunks. Same placeholders. |
| `Normalize` | `true` | Normalizes vectors before storing or comparing them. |
| `Instruction` | empty | Task instruction available to templates such as Qwen3. |

If the embedding service is down, disabled, or the index is empty, search
returns keyword results with `degraded: true` instead of failing.

### Logging

Logging uses the standard .NET configuration stack. `appsettings.json` sets
`Default` to `Information` and `Microsoft.AspNetCore` to `Warning`. The shipped
Compose file sets the EF command log to `Warning` to keep SQL noise out of
container logs.

```bash
Logging__LogLevel__Default=Debug
Logging__LogLevel__Microsoft.AspNetCore=Warning
Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Warning
```

`Logging__LogLevel__<Category>` accepts `Trace`, `Debug`, `Information`,
`Warning`, `Error`, `Critical`, or `None`.

## Docker Compose substitution

Compose has two separate mechanisms, and they are easy to confuse:

| Mechanism | What it reads | What reaches the container |
| --- | --- | --- |
| `${VAR}` substitution | The `.env` beside the Compose file, the shell environment, and Compose CLI settings | The substituted value in the rendered Compose configuration |
| `environment:` entries | The same sources, via `${VAR:-default}` | Only the variables listed under `environment:` become process environment inside the container |

Compose reads `.env` from the directory containing the Compose file but does
**not** mount it into the container. Setting a key in `.env` alone does nothing
unless the Compose file references it in `environment:` or in a `${...}`
substitution.

What the Compose file forwards:

- The repository [`docker-compose.yml`](../docker-compose.yml) is the only
  shipped Compose file. `WARTABLE_TAG` defaults to `2`, which tracks non-breaking
  releases in the current major; set it to a full `X.Y.Z` release to pin one, and
  to a new major when you choose to move. It forwards
  `Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command` and the
  `Embeddings__*` keys listed under `environment:`.
- [`docker-compose.build.yml`](../docker-compose.build.yml) is a build override
  for local images, not a second deployment file. It requires `WARTABLE_TAG`
  because it uses it as the local image tag.
- `WARTABLE_TAG` is substitution-only. It is not an application setting inside
  the container.

Only `Enabled`, `Endpoint`, `Model`, `Dimensions`, `MaxInputTokens`, `ApiKey`,
and `Profile` are currently passed through by the shipped Compose file. To use
another `Embeddings__*` key (for example `BatchSize`, `TimeoutSeconds`,
`PollSeconds`, `TopK`, `MinScore`, or `SidecarPath`) from `.env`, add it under
`environment:` in your Compose file, or pass it with `-e` on `docker run`.

Inside the container, `localhost` and `127.0.0.1` mean the container itself. Use
`host.docker.internal` for a service on the Docker host; the shipped Compose file
maps that name to the Linux host gateway. If the embedding service is another
Compose service, use its service name, for example `http://ollama:11434/v1`.

## Related

- [`design.md`](design.md) — semantic search and work-surface context design.
- [`deployment.md`](deployment.md) — the production configuration contract and
  Compose file.
- [Site updating page](https://alex98235.github.io/WarTable/updating) — upgrade
  procedures.
- [`development.md`](development.md) — local environment and test loop.
