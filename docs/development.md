# Development

This page covers the local loop: prerequisites, build, tests, formatting and
linting, the frontend dev server, the local container build, release commands,
CI, and the repository layout.

It does not cover how the code is organized — see
[`architecture.md`](architecture.md) — or how to run a real deployment — see
[`deployment.md`](deployment.md) for the production contract and
[`container.md`](container.md) for container operations. Configuration
variables and precedence are in [`configuration.md`](configuration.md).
Commit, branch, and pull-request policy, including the rules agents must follow,
is in [`CONTRIBUTING.md`](../CONTRIBUTING.md).

## Prerequisites

- .NET 10 SDK
- Node.js 22 and npm
- Docker Engine with Compose v2, for the local image build
- `git-cliff` on `PATH`, for changelog and release commands

## Build

Install the frontend and root tooling once:

```bash
npm run init
```

Build both sides:

```bash
npm run build            # frontend, then backend
npm run build:frontend   # Svelte bundle into WarTable.Api/wwwroot
npm run build:backend    # dotnet build WarTable.Api/WarTable.Api.csproj
```

Run the backend directly:

```bash
dotnet run --project WarTable.Api -- --mode serve
dotnet run --project WarTable.Api -- --mode mcp
```

`serve` is the default and listens on `http://localhost:5110`. Use
`--host 0.0.0.0` to accept LAN connections. The database defaults to
`~/.wartable/wartable.db`; override it with `--db`. See
[`architecture.md`](architecture.md#programcs-modes) for what each mode wires up.

## Local environment

Copy `.env.example` to `.env` for local settings:

```bash
cp .env.example .env
```

WarTable loads the nearest `.env` at startup, walking up from the current
directory and the application directory. Process environment variables win, so
shell/CI overrides are not replaced. `WARTABLE_SKIP_DOTENV=1` disables loading;
`WARTABLE_ENV_FILE=/path/.env` selects a specific file. `.env` is gitignored.
[`configuration.md`](configuration.md) is the authoritative list of variables
and defaults.

Semantic search is optional. Set `Embeddings__Enabled=true` and start an
OpenAI-compatible embedding service; `.env.example` contains the model and
dimension defaults. In Docker Compose, use `host.docker.internal` rather than
`localhost` for a service running on the host.

## Tests

```bash
dotnet test WarTable.slnx
```

Tests cover status derivation, dependency handling, alias resolution, plan
import, briefing bounds, and related service behavior.

## Format and lint

Root dev dependencies include Prettier, ESLint, Stylelint, and html-validate.
The frontend uses its own configs in `WarTable.Client/`, invoked from the root:

```bash
npm run format:frontend       # write formatting for Svelte, JS, CSS, HTML, JSON
npm run format:frontend/backend # frontend + backend formatting
npm run format-check:backend  # verify C# formatting without writing
npm run format-check:css      # verify CSS formatting
npm run format-check:html     # verify HTML formatting
npm run format-check:js       # verify JS, MJS, and Svelte formatting
npm run format-check:css/html/js # all three format checks
npm run lint:js               # ESLint for JS and Svelte
npm run lint:css              # Stylelint for CSS and Svelte styles
npm run lint:js/css           # both linters
npm run validate:html         # html-validate
npm run validate              # format checks + both linters + HTML validation
```

`npm run validate` is the gate: it runs the format checks, both linters, and
HTML validation, and it never modifies files. Run it before opening a pull
request. `format-check` and `lint` are aggregate aliases for the same checks;
the slash-suffixed names are convenience aliases for the exact commands above.

`npm run format:backend` runs `dotnet format` over the solution. The backend tree
currently passes `format-check:backend`; if the formatter produces changes, run
it deliberately and commit formatting separately from behavior changes.
`format:frontend/backend` includes it.

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
docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build
```

The override tags the result `wartable:${WARTABLE_TAG}`. A direct build is also
supported:

```bash
docker build --platform linux/amd64 -t wartable:local .
```

The build has three stages: Node 22 builds the Svelte client, the .NET 10 SDK
publishes `wartable`, and the ASP.NET 10 runtime image runs as non-root user
`10001:10001`. [`container.md#image-build`](container.md#image-build) is the
container-side reference.

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
for exercising the workflow without publishing. Release artifacts and the
production pinning rules are part of the deployment contract; see
[`deployment.md` section 10](deployment.md#10-versioning-and-release-artifacts).

## GitHub Actions

| Workflow | Trigger | Purpose |
| --- | --- | --- |
| [`ci.yml`](../.github/workflows/ci.yml) | pull request, manual dispatch | Builds frontend and backend, checks backend formatting, runs `npm run validate` and backend tests; `pr-title` validates the Conventional Commit PR title |
| [`release.yml`](../.github/workflows/release.yml) | `v*` tag push, manual | Builds binaries, publishes the image, creates the GitHub release |
| [`smoke-image.yml`](../.github/workflows/smoke-image.yml) | manual | Pulls a published image and verifies health, REST, MCP, restart, and persistence |

Run the image smoke test after publishing:

```bash
gh workflow run smoke-image.yml -f image=ghcr.io/alex98235/wartable:<X.Y.Z>
gh run watch
```

`smoke-image.yml` accepts any published image reference. It accepts a private
fork package when the workflow token has package read access. The workflow is
documented in [`container.md`](container.md#smoke-test-the-published-image).

## Repository layout

```text
WarTable.Api/             ASP.NET minimal API, HTTP endpoints, MCP tools, EF Core/SQLite
WarTable.Api.Tests/       xUnit test project
WarTable.Client/          Svelte 5 + Vite frontend
docs/                     Architecture, design, configuration, deployment, container, this guide
scripts/                  release.mjs (tagging)
.github/workflows/        CI, release, image smoke test
docker-compose.yml        Production Compose: pinned image + named volume
docker-compose.build.yml  Local-build override; use it with the base file
Dockerfile                Three-stage frontend/backend/runtime image
cliff.toml                git-cliff release-note configuration
```

Code-level ownership for each project is in
[`architecture.md`](architecture.md).
