# Why WarTable is single-human

Expands [§9 Non-goals](../design.md#9-non-goals). WarTable is single-human by
design, not by omission. This page records the reasoning so the boundary is not
re-derived from first principles every time a shared deployment looks attractive.

## The boundary is the human; the tenancy unit is the campaign

A campaign is already a tenancy boundary. Aliases are campaign-scoped, knowledge
is campaign-scoped, and preferences are the only cross-campaign memory. Adding
accounts would place a second boundary above the campaign — membership — so every
read and write must satisfy two overlapping scopes instead of one. Overlapping
boundaries are where authorization bugs live.

## Deployment gravity

The supported deployment is one container, one SQLite file, and one
credential-free MCP endpoint. Accounts require a domain, TLS with renewal, an
identity provider — or password reset, which means outbound email deliverability —
backups for other people's data, and an abuse policy. A one-line install becomes
an operations job.

The work is the smaller cost. The larger one is gravity: once a tenant boundary
exists, every later feature is designed multi-tenant-first, and the single-human
deployment becomes the mode nobody tests. A product does not lose its reason to
exist through one decision; it loses it through a thousand subsequent ones that
all assume the other mode.

## Integration cost lands on MCP

MCP is the primary write surface, and the agent loop is deliberately many small
calls. Per-user credentials must be issued, scoped, rotated, and revoked, and
every contributor configures their own agent against someone else's server.
Co-location is lost as well: an agent sitting next to its human assembles context
at LAN latency during a planning conversation, not at internet latency.

## Data locality

Campaigns on a shared server are governed by the operator's competence — their
retention policy, their backup integrity, their availability. A "private
campaign" would need membership-of-one: new authorization surface created
specifically for the projects a person least wants to share.

## What single-human still allows

- Several agents per human, writing as `agent:<name>`.
- Several browser clients; multiple devices are the same human surface.
- Subagents spawned by one agent, coordinated through dependencies.

## Where shared knowledge goes instead

If a team needs a common knowledge base, knowledge travels rather than gathers. It
lives in the repository where agents already read it — `AGENTS.md`, `docs/`, and
decision records — or moves as exported notes. Retrieval does not require a shared
server, which keeps the single-human deployment the default.
