# Development

This page covers building WarTable from source, the local frontend loop, tests,
building the container locally, and the release tooling. For the deployed
production contract, see [`deployment.md`](deployment.md); for container
operations, see [`container.md`](container.md).

## Prerequisites

- .NET 10 SDK
- Node.js 22 and npm
- Docker Engine with Compose v2, for the local image build
- `git-cliff` on `PATH`, for changelog and release commands

## Build

Install the frontend and root tooling once:

```bash
npm install
npm --prefix WarTable.Client install
```

Build both sides:

```bash
npm run build            # frontend, then backend
npm run build:frontend   # Svelte bundle into WarTable.Api/wwwroot
npm run build:backend    # dotnet build WarTable.Api/WarTable.Api.csproj
```

The frontend build must run before the backend compiles in a clean tree: the API
project embeds `wwwroot` into the assembly so a published single-file binary
contains the UI. A physical `wwwroot` is used first at runtime, which is what
makes the local dev loop work.

Run the backend directly:

```bash
dotnet run --project WarTable.Api -- --mode serve
dotnet run --project WarTable.Api -- --mode mcp
```

`serve` is the default and listens on `http://localhost:5110`. Use
`--host 0.0.0.0` to accept LAN connections. The database defaults to
`~/.wartable/wartable.db`; override it with `--db`.

## Local environment

Copy `.env.example` to `.env` for local settings:

```bash
cp .env.example .env
```

WarTable loads the nearest `.env` at startup, walking up from the current
directory and the application directory. Process environment variables win, so
shell/CI overrides are not replaced. `WARTABLE_SKIP_DOTENV=1` disables loading;
`WARTABLE_ENV_FILE=/path/.env` selects a specific file. `.env` is gitignored.
Parsing and interpolation are handled by the `DotNetEnv` package; WarTable only
selects the file and preserves precedence.

Semantic search is optional. Set `Embeddings__Enabled=true` and start an
OpenAI-compatible embedding service; `.env.example` contains the model and
dimension defaults. In Docker Compose, use `host.docker.internal` rather than
`localhost` for a service running on the host; the compose file already supplies
that default and the Linux host-gateway mapping.

## Tests

```bash
dotnet test WarTable.Api.Tests/WarTable.Api.Tests.csproj
```

The test project is not part of `WarTable.slnx`, so point the command at the
test project explicitly. Tests cover status derivation, dependency handling,
alias resolution, plan import, briefing bounds, and related service behavior.

## Format and lint

Root dev Dependencies include Prettier, ESLint, Stylelint, and html-validate.
The frontend uses its own configs in `WarTable.Client/`, invoked from the root:

```bash
npm run format:frontend          # write formatting for Svelte, JS, CSS, HTML, JSON
npm run format:frontend/backend  # frontend write + dotnet format
npm run format-check:css/html/js # verify all frontend formatting
npm run lint:js                  # ESLint for JS and Svelte
npm run lint:css                 # Stylelint for CSS and Svelte styles
npm run lint:js/css              # both linters
npm run validate:html            # html-validate
npm run validate                 # format check + both linters + HTML validation
```

`npm run format:frontend/backend` also reformats the C# source. That is
deliberate, but it is a broad write; run it only when the backend formatting
change is wanted. `npm run validate` does not modify files.

## Frontend dev loop

```bash
npm run dev
```

This starts the Vite dev server and the API together with `concurrently`. Vite
serves the Svelte app with hot reload and proxies `/api` to
`http://localhost:5110`. Open the Vite URL it prints, not the API port.

The two halves can also be run separately:

```bash
npm run dev:frontend
npm run dev:backend
```

## Local container build

The base [`docker-compose.yml`](../docker-compose.yml) always pulls a published
image. Compose the build override to build from the checkout instead:

```bash
cp .env.example .env
mkdir -p data
sudo chown -R 10001:10001 data   # Linux host; the container runs as UID/GID 10001
docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build
```

The override tags the result `wartable:${WARTABLE_TAG}`. A direct build is also
supported:

```bash
docker build --platform linux/amd64 -t wartable:local .
```

The build has three stages: Node 22 builds the Svelte client, the .NET 10 SDK
publishes `WarTable.Api`, and the ASP.NET 10 runtime image runs as non-root user
`10001:10001`.

## Changelog and release

Release notes are generated from Conventional Commits by
[git-cliff](https://git-cliff.org) using [`cliff.toml`](../cliff.toml). A local
preview writes an ignored `CHANGELOG.md`:

```bash
npm run changelog
```

`npm run release` asks git-cliff for the next version, prints the notes that
will ship, and — only after confirmation — creates an annotated tag and pushes
it:

```bash
npm run release              # interactive
npm run release -- --dry-run # preview only
npm run release -- --yes     # no prompt
```

It refuses to run on a dirty tree, off `main`, or when the tag already exists.
The version bump follows the commits: `feat:` bumps minor, `fix:` bumps patch,
and a `!` or `BREAKING CHANGE` bumps major.

Pushing a `vX.Y.Z` tag runs [`release.yml`](../.github/workflows/release.yml).
That workflow:

1. Generates release notes with git-cliff.
2. Builds `win-x64` and `linux-x64` self-contained binaries as
   `wartable-<version>-<rid>.zip` / `.tar.gz` with `.sha256` sidecars.
3. Builds and pushes the `linux/amd64` image to `ghcr.io/<owner>/wartable` with
   `X.Y.Z`, `X.Y`, and `latest` tags. Pre-releases do not move `latest`.
4. Appends the image reference and digest, checksums, schema version, schema
   change warning, and a compare link to the release body.

A manual `release.yml` run builds the image without pushing it, which is useful
for exercising the workflow without publishing.

## GitHub Actions

| Workflow | Trigger | Purpose |
|---|---|---|
| [`ci.yml`](../.github/workflows/ci.yml) | push to `main`, pull request | Builds the frontend and backend |
| [`release.yml`](../.github/workflows/release.yml) | `v*` tag push, manual | Builds binaries, publishes the image, creates the GitHub release |
| [`smoke-image.yml`](../.github/workflows/smoke-image.yml) | manual | Pulls a published image and verifies health, REST, MCP, restart, and persistence |
| [`update-image.yml`](../.github/workflows/update-image.yml) | manual | Runs `scripts/update.sh` between two published versions and verifies rollback |

Run the image checks after publishing:

```bash
gh workflow run smoke-image.yml -f image=ghcr.io/alex98235/wartable:0.1.0
gh workflow run update-image.yml -f from_version=<from> -f to_version=<to>
gh run watch
```

`smoke-image.yml` accepts any published image reference. `update-image.yml`
requires two published version tags. Both accept a private fork package when the
workflow token has package read access.

## Repository layout

```text
WarTable.Api/          ASP.NET minimal API, HTTP endpoints, MCP tools, EF Core/SQLite
WarTable.Api.Tests/    xUnit test project
WarTable.Client/       Svelte 5 + Vite frontend
docs/                  Deployment contract, container operations, design, and this guide
quickstart/            One-file Compose deployment and first-run guide
scripts/               update.sh (manual update/rollback) and release.mjs (tagging)
.github/workflows/     CI, release, image smoke test, image update test
docker-compose.yml     Production Compose: pinned image + bind mount
docker-compose.build.yml  Local-build override; use it with the base file
Dockerfile             Three-stage frontend/backend/runtime image
cliff.toml             git-cliff release-note configuration
```
