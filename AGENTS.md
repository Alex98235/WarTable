# Agent onboarding

Short orientation for coding assistants working in this repository.

## Read first

- [`CONTRIBUTING.md`](CONTRIBUTING.md) — branches, commit grammar, pull request
  gates, schema rules, release policy. Follow it; it applies to agents too.
- [`docs/design.md`](docs/design.md) — what WarTable is and how the domain works.
- [`docs/architecture.md`](docs/architecture.md) — where code lives and how a
  request flows through the API, services, and client.
- [`docs/configuration.md`](docs/configuration.md) — environment variables and
  configuration precedence.

`docs/development.md` covers the local loop, `docs/deployment.md` the production
contract, and `docs/container.md` container operations.

## Build and test

```sh
npm run build:frontend                                        # must run before the backend build
npm run build:backend
dotnet test WarTable.slnx
npm run validate
```

The solution includes both the API and test projects.
The frontend build output is embedded by the API; building the backend alone
serves whatever `wwwroot` already contains.

## Invariants

- SQLite with EF Core `EnsureCreated`, no migrations. Any EF model change needs a
  `SchemaVersion` bump in `DatabaseInitializer`; startup backs up and recreates a
  mismatched database rather than migrating it. No silent data loss.
- Keep MCP tools and HTTP endpoints compatible unless the change is explicitly
  breaking and documented as such.
- Conventional Commits drive the changelog and semantic version.
- Backend formatting is clean under `dotnet format`. `npm run format-check:backend`
  verifies it; `format:backend` writes, so commit formatting separately from
  behavior changes.

## WarTable MCP is optional

If a WarTable MCP server is configured, it provides campaign and planning tools.
It is not required to work here, and the repository documentation is
authoritative. Do not treat an MCP session as the source of truth for how the
code builds, tests, or ships.
