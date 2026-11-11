# Contributing to WarTable

WarTable is a human-AI planning workspace: a .NET API, a Svelte client, a SQLite
database, and an MCP server that all ship as one container image. This file is
the collaboration contract. It covers branches, commit messages, pull requests,
the gates a change must pass, schema rules, releases, and which document owns
which topic.

If you only read one section, read [Commit messages](#commit-messages) and
[Pull requests](#pull-requests).

## Branch model

| Branch | Role |
| --- | --- |
| `main` | Protected and release-ready. Every commit has passed CI and is deployable as-is. |
| `feat/*`, `fix/*`, `docs/*`, `chore/*` | Short-lived work branches opened against `main`. |

Rules:

- `main` is protected. Do not push directly to it, force-push it, or delete it.
  Every change reaches `main` through a pull request.
- Start feature branches from an up-to-date `main`, and rebase them on `main`
  before merging. Open the pull request against `main`.
- Merge with **Squash and merge** by default. **Rebase and merge** is acceptable
  when every commit is independently valid and follows the commit format. Do
  not create merge commits on `main`; keep its history linear.
- With squash merge, the pull request title becomes the commit title, so it must
  follow the commit format below. CI enforces this with the `pr-title` check.
  With rebase merge, every commit is validated by the same convention.
- Delete the feature branch after it merges.
- Release tags are created from `main`. See [Release policy](#release-policy).
- Keep a branch focused. One concern per pull request is easier to review,
  revert, and describe in the changelog.

## Commit messages

Every commit follows [Conventional Commits](https://www.conventionalcommits.org):

```text
<type>(<scope>): <summary>

<body>

<footer>
```

The grammar:

- `<type>` is one of the types below.
- `<scope>` is optional but encouraged. Use one of the suggested scopes or a
  similarly short, lowercase area name.
- `<summary>` is imperative mood ("add", not "added" or "adds"), lowercase
  except for proper nouns, has no trailing period, and is at most 72 characters.
- Separate the summary from the body with one blank line.
- The body explains **why** the change exists, not what the diff shows. Wrap it
  at 72 characters.
- The footer carries references (`Refs: GH-12`, `Closes: GH-34`) and
  breaking-change notices (`BREAKING CHANGE: ...`).

### Types

| Type | Use for |
| --- | --- |
| `feat` | A user-visible capability |
| `fix` | A bug fix |
| `refactor` | A behavior-preserving restructure |
| `docs` | Documentation only |
| `test` | Test-only changes |
| `build` | Build system, dependencies, project files |
| `chore` | Maintenance that fits nothing else |
| `perf` | Performance work |
| `ci` | Workflows and automation |
| `style` | Formatting only, no behavior change |

### Suggested scopes

`api`, `client`, `mcp`, `knowledge`, `docs`, `release`, `deploy`, `db`.

Prefer an existing scope over inventing a new one. A missing scope is better
than a misleading one.

### Examples

```text
feat(mcp): attach campaign briefing to session start
```

```text
fix(api): keep alias allocation stable after objective delete

Aliases were re-derived from the remaining objectives, so deleting WarTable
alias 3 could shift alias 4 to alias 3 and break references stored in notes.
Aliases are now allocated from a persisted counter that only moves forward.

Refs: GH-41
```

```text
docs(release): document tag signing in the development guide
```

```text
feat(api)!: reject databases written by an older schema

Schema changes are no longer tolerated silently at startup.

BREAKING CHANGE: a database whose schema version is not current is backed up
to `<name>.bak-<timestamp>` and recreated. Operators must restore or export
data before upgrading across a schema bump.
```

A breaking change is either `!` after the type/scope or a `BREAKING CHANGE:`
footer, or both. `!` alone is easy to miss in a changelog, so state the impact
in the footer as well when data or the API contract is affected.

GitHub renders commit messages as Markdown and autolinks bare `#N` to an issue
or pull request. Do not use `#N` in a commit message for a WarTable alias. Write
`WarTable alias 3`, `WarTable object 3`, or `WarTable objective 3` instead. Use
`GH-41` or a full URL for a GitHub issue reference.

## Pull requests

1. Branch from an up-to-date `main`.
2. Make the change, including tests and docs.
3. Run the gates below locally.
4. Open the pull request against `main`. Use a Conventional Commit title; CI
   validates it, and it becomes the commit title when squashed.
5. Keep the pull request description about intent and risk, not a file-by-file
   narration.

Automated checks are the review checklist:

- `build` runs the frontend build, the Release backend build, backend formatting
  verification, `npm run validate`, and the backend tests.
- `pr-title` verifies the Conventional Commit title.
- Branch protection should require both checks before merge, require a pull
  request, and keep history linear. These are repository rules, not files.

PRs do not (currently) require a reviewer's approval. You are the judge of your
own work. If anything about your implementation feels uncertain: do not merge it.

## Gates

Run these before opening a pull request:

```sh
# Backend tests
dotnet test WarTable.slnx

# Backend formatting check (does not write)
npm run format-check:backend

# Formatting, lint, and HTML validation for the client
npm run validate

# Frontend production build (writes into WarTable.Api/wwwroot)
npm run build:frontend
```

Notes:

- `npm run validate` runs Prettier checks, ESLint, Stylelint, and
  `html-validate` over the client source. It does not format anything.
- `npm run format` rewrites source. `format:frontend` is scoped to the client.
  `format:backend` runs `dotnet format` over the solution, including the test
  project. The backend tree is currently clean under that formatter, so
  `format-check:backend` should pass; if `format:backend` produces changes, run
  it deliberately and commit the formatting separately from behavior changes.
- The frontend build must run before the backend build. The API embeds the built
  client from `WarTable.Api/wwwroot`.

CI (`.github/workflows/ci.yml`) runs on every pull request and on manual
dispatch. It installs both Node trees, builds the frontend, builds the backend in
Release, checks backend formatting, then runs `npm run validate` and the backend
tests. A separate `pr-title` job validates the Conventional Commit title. A red
check is a blocker, not a suggestion.

## Schema and data

WarTable uses SQLite through EF Core with `EnsureCreated`, not EF migrations.
There is no migration history and no `dotnet ef database update` step.

- Changing the EF model shape requires bumping `SchemaVersion` in
  `WarTable.Api/Data/DatabaseInitializer.cs`.
- At startup `DatabaseInitializer` reads `PRAGMA user_version`. If it does not
  match `SchemaVersion`, the existing database file is copied to
  `<name>.bak-<timestamp>`, the database is deleted, recreated, and restamped.
- That recreation is a clean slate. It preserves a backup, but it does not move
  rows forward. Treat any schema bump as a data event and say so in the pull
  request and the release notes.
- Test compatibility with a database written by the previous schema before
  merging. Do not merge a schema bump that has only been exercised on a fresh
  database.
- No silent data loss: never drop or rewrite user data without a backup path and
  a documented recovery step.

## Release policy

- Releases are cut from `main`, from a clean working tree.
- `npm run release` asks git-cliff for the next version, shows the commits, and
  creates and pushes the tag after confirmation. Pushing the tag triggers the
  release workflow.
- Conventional commits drive the semantic version bump. `feat` bumps minor,
  `fix` bumps patch, and a breaking change bumps major. This is why the commit
  grammar in this file is not decorative.
- `CHANGELOG.md` is generated by git-cliff at release time, is gitignored, and
  is **not** canonical. Do not hand-edit it and do not commit it. If release
  notes are wrong, fix the commits they were generated from.
- Do not tag a commit that has not passed CI on `main`.

## Documentation ownership

Each document has one job. Put information where it belongs instead of
duplicating it.

| Document | Owns |
| --- | --- |
| `README.md` | Front door: what WarTable is, the fastest path to a running instance, links |
| `docs/design.md` | Current product and domain design: concepts, behavior, decisions |
| `docs/architecture.md` | Code map: projects, layers, request flow, where things live |
| `docs/configuration.md` | Environment variables and configuration precedence |
| `docs/deployment.md` | Production contract: pinned images, volumes, backup, updates |
| `docs/container.md` | Container operations: tags, smoke and update tests |
| `docs/development.md` | Local development loop, build, test, release mechanics |
| `CONTRIBUTING.md` | Collaboration: branches, commits, pull requests, gates |

If a change alters observable behavior, update `docs/design.md`. If it adds or
renames configuration, update `docs/configuration.md` and `.env.example`. If it
changes the deployment contract, update `docs/deployment.md`. A pull request
that changes behavior without a documentation change should say why.
