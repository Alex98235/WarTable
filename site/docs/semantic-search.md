---
sidebar_position: 5
title: Semantic search
---

# Semantic search

WarTable's knowledge search works in two modes. Keyword search is always on.
Semantic search is an optional layer that finds notes by meaning instead of by
matching words, and it falls back to keyword results when it is unavailable.

Three properties matter more than the model you choose:

- **Provider-neutral.** WarTable talks to any service that implements the
  OpenAI-compatible `POST /v1/embeddings` contract. Ollama, LM Studio, vLLM, and
  hosted providers all work.
- **The service never owns the index.** Chunks, vectors, and fingerprints live in
  a disposable SQLite sidecar beside the main database, so changing providers
  does not move your knowledge anywhere.
- **Search never breaks.** If embeddings are disabled, unreachable, or still
  indexing, the same call returns keyword results with `degraded: true`.

## What it changes

Semantic search affects three surfaces:

- **Bunker search.** The `Semantic` switch turns search into meaning search and
  shows ranked match cards. Each card says whether it matched by `keyword`,
  `semantic`, or `both`. Raw scores are not shown, because hybrid and cosine
  scores are not comparable.
- **Agent search.** `search_knowledge` returns the same hybrid results with
  snippets, aliases, kinds, and heading context. `search_notes` stays a plain
  keyword filter.
- **Work-surface context.** A step's `knowledge` block and `get_context` add
  semantically nearby notes to the ones linked or tagged to the work, under
  per-kind caps.

## Enable it

Point WarTable at an embedding service. A local Ollama setup:

```dotenv
Embeddings__Enabled=true
Embeddings__Endpoint=http://localhost:11434/v1
Embeddings__Model=embeddinggemma
Embeddings__Dimensions=768
```

Restart, then check the index:

```bash
curl -fsS http://localhost:5110/api/knowledge/status
```

The status response reports whether embeddings are `enabled`, `configured`, and
`ready`, how many notes (`sources`) and `chunks` the index holds, the `model`,
`profile`, and `dimensions` in use, the last index time, and any error. Reconcile
on demand with `POST /api/knowledge/reindex`; you rarely need it, because the
background worker runs on a poll.

:::note
The embedding model is not bundled. WarTable owns chunking, the index, ranking,
and the keyword fallback. The service only turns text into vectors.
:::

## Choosing a model

Any model behind an OpenAI-compatible endpoint works, but instruction-tuned
embedding models expect different text for a query and for a document. WarTable
handles that with a **profile**: a query template, a document template, expected
dimensions, and a chunk-size limit.

Profiles are pre-configured for:

| Profile | Matched from the model id |
| --- | --- |
| `embeddinggemma` | `embeddinggemma` |
| `bge` | a model id containing `bge` |
| `e5` | a model id containing `e5` |
| `nomic` | a model id containing `nomic` |
| `qwen3-embedding` | `qwen3-embedding` or `qwen3_embedding` |
| `openai-text-embedding-3` | a model id containing `text-embedding-3` |

The exact templates, dimensions, and token limits live in
[`EmbeddingProfile.cs`](https://github.com/Alex98235/WarTable/blob/main/WarTable.Api/Services/Embeddings/EmbeddingProfile.cs);
the code is authoritative. Set `Embeddings__Profile` to a profile name to use it
regardless of the model id.

:::warning
If no profile matches and you do not define one, WarTable falls back to raw
`{text}` templates and logs a warning. Search still works, so the problem is easy
to miss, but results are usually worse. Define a profile for any model that
expects prefixes.
:::

Two things to get right:

- **Dimensions follow the model size, not the family.** The built-in values
  describe one size in each family: `bge` and `e5` are 768 (base), `nomic` is
  768, `qwen3-embedding` is 1024 (0.6B), and `openai-text-embedding-3` is 1536
  (small). A larger variant needs `Embeddings__Dimensions` set to its own size,
  or the first index pass fails with a dimension mismatch.
- **Leave `Dimensions` and `MaxInputTokens` at `0`** to take the profile's
  values. Override them only when your variant differs.

## Defining a profile

Add profiles under `Embeddings__Profiles__N__*`, where `N` is a numeric index.

| Key | Meaning |
| --- | --- |
| `Name` | Profile name. Also the value for `Embeddings__Profile`. |
| `Match` | Regular expression matched against the model id. Defaults to the escaped name. |
| `Dimensions` | Vector size. `0` uses the profile's size, or lets the service decide. |
| `MaxInputTokens` | Chunk-size limit. Defaults to `512`. |
| `QueryTemplate` | Text sent for a search query. |
| `DocumentTemplate` | Text sent for a note chunk. |
| `Normalize` | Normalize vectors before storage. Defaults to `true`. |
| `Instruction` | Optional instruction for models that take one, exposed as `{instruction}`. |

Templates support these placeholders: `{text}`, `{title}`, `{kind}`, `{tags}`,
`{heading}`, `{campaign}`, and `{instruction}`.

Example: a model that wants an instruction on queries and a title on documents.

```dotenv
Embeddings__Profiles__0__Name=acme-embed
Embeddings__Profiles__0__Dimensions=1024
Embeddings__Profiles__0__MaxInputTokens=2048
Embeddings__Profiles__0__QueryTemplate=Instruct: find notes that answer the query. Query: {text}
Embeddings__Profiles__0__DocumentTemplate={title} {text}
```

Custom profiles are matched before built-in ones, and an explicit
`Embeddings__Profile` wins over both.

Environment values are single-line. The shipped Compose file forwards the
tuning keys but no custom profiles, so a profile goes under `environment:` in a
`docker-compose.override.yml`, which `docker compose` merges automatically. If a
template needs a line break, use a YAML block scalar, because the container
image has no editable `appsettings.json`:

```yaml
services:
  wartable:
    environment:
      Embeddings__Profiles__0__Name: acme-embed
      Embeddings__Profiles__0__Dimensions: 1024
      Embeddings__Profiles__0__QueryTemplate: |-
        Instruct: {instruction}
        Query: {text}
```

A standalone binary can instead set the same values in `appsettings.json` in its
working directory. Most models need only a single-line prefix, so a multi-line
template is rarely required.

## The index

The index is a disposable SQLite file, `wartable.embeddings.db`, beside the main
database. It stores heading-aware chunks, content hashes, model and profile
fingerprints, and normalized vectors. It is safe to delete: the next reconcile
rebuilds it.

The fingerprint covers the model, profile name, dimensions, token limit, and
both templates. Changing any of them makes the stored vectors stale, so the next
reconcile re-embeds the affected notes. That is expected after a model change.

Only notes whose chunk hashes changed are embedded, so the write path stays fast
and the worker is cheap.

## When it is off

With `Embeddings__Enabled=false`, WarTable does not start the background indexer,
does not call an embedding service, and does not create the sidecar. Knowledge
search still works by keyword, and `search_knowledge` returns `degraded: true`
so an agent can tell the difference.

## Tuning

The shipped Compose file forwards these, so setting one in `.env` is enough.

- `Embeddings__MinScore` (default `0.2`) is the minimum cosine score for a
  semantic match. Raise it if unrelated notes keep surfacing.
- `Embeddings__TopK` (default `8`, clamped to 1–50) is the default result count.
- `Embeddings__BatchSize`, `TimeoutSeconds`, and `PollSeconds` control indexing
  throughput. The defaults are fine for a LAN box.
- `Embeddings__SidecarPath` moves the index. Leaving it beside the main database
  keeps it with your data; it is disposable either way.

## Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| `Embedding service returned N dimensions, but Embeddings:Dimensions is M` | The model's output size does not match the configured or profile value. Set `Embeddings__Dimensions` to the model's size. |
| A warning that no profile matched the model | The model needs a profile. Set `Embeddings__Profile`, or define one under `Profiles`. |
| Search returns `degraded: true` | Embeddings are disabled, the service is unreachable, or the index is still building or empty. Check `/api/knowledge/status`. |
| Results only match words, never meaning | Semantic search is off, or the vectors are weak for this model. Confirm the status endpoint shows the model and profile you expect. |
