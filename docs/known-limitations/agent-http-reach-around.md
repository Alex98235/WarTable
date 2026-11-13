# An agent can reach the HTTP API

## What happens

Serve mode mounts the MCP endpoint and the REST API on the same Kestrel origin
with no authentication:

- `Program.cs:164` — `app.MapMcp("/mcp")`, configured at `:112` with
  `WithHttpTransport`.
- The same application maps `/api/campaigns`, `/api/objectives`, and the rest at
  `Program.cs:155-163`.

The MCP surface has no hard delete — `delete_campaign` does not exist, and
`McpToolSurfaceTests.Campaign_HasArchiveButNoHardDelete` asserts its absence. The
HTTP route does: `CampaignEndpoints.cs:89-109` calls `DeleteCampaignAsync`, which
removes the campaign and cascades to its objectives, steps and notes.

An agent that receives `http://<host>:5110/mcp` and can read its own client
configuration knows the origin. One request to
`DELETE http://<host>:5110/api/campaigns/{id}` reaches what the tool list
withheld. Nothing about the request identifies the caller.

## Why it is this way

WarTable has no accounts. That is not an omission to be fixed later; it is the
model (§2 principle 9, and [`../designs/single-human-boundary.md`](../designs/single-human-boundary.md)):
one server, one human's data, many clients. Identity is the thing the system
deliberately does not have, and a listener cannot distinguish callers whose
identity it does not model.

Principle 11 already states the limit: *a tool list is a contract, not a
sandbox.*

## What it costs

A careless prompt — "delete the X campaign" — combined with an agent that
decides `curl` is a reasonable way to satisfy a request it has no tool for. The
consequence is a hard delete, which is irreversible.

The damage is smaller than it looks: aliases are campaign-scoped
([`../design.md`](../design.md) §4, *Aliases and addressing*), so nothing outside
the deleted campaign dangles. What is lost is the campaign's own history, and
only a restore from the `wartable.db.bak-*` copy recovers it.

What holds the line in practice: the missing tool, the instruction clause (*no
tool deletes a campaign: archive it and say so*), and the fact that a model
selecting a tool maps "delete" to the nearest available verb and reports the
substitution. None of those is enforcement.

## Rejected

- **Split the origin** — serve `/mcp` on a second port and 404 everything else
  there. Defeats only an agent that has the URL and no shell; a shell-capable
  agent guesses the other port. Adds a listener and two configuration paths for
  a boundary that does not exist.
- **CAPTCHA on destructive routes** — the token is verified server-side against
  a third party's secret, so it needs the internet, and WarTable is an offline
  single binary. It also fails at the thing it looks like it does: audio
  challenges are speech-to-text today, and solving services sell passes by the
  thousand.
- **Windows Hello / WebAuthn** — the only genuine capability boundary
  considered: a TPM-backed key needs a PIN or fingerprint the caller cannot
  supply. Rejected on portability. The platform authenticators differ per OS,
  Linux user verification is not dependable, and losing the device or resetting
  the TPM strands the delete path with no way back.
- **Tokens on the MCP endpoint** — the browser UI has no auth either, so any
  token the delete route demands must be handed to the page that calls it, from
  the same origin, in the same JavaScript. It obstructs nothing.
- **A scheduled delete with a cancel window** — converts the failure into "you
  have ten minutes". Its protection *is* an attentive human, which is the thing
  principle 13 says the server may not rely on. Legitimate as a client-side
  undo, never as a control.

## Why there is no fourth option

Four constraints hold at once, and together they exclude every server-side test
for "is this a human":

1. **Offline** — no third party can verify anything.
2. **OS-agnostic** — no platform gesture is available everywhere.
3. **No human required** — anything whose protection is human attention is out.
4. **Same user, same machine** — the caller can read every file the server
   reads and call every route the UI calls.

Remove any one and a solution exists. With all four, the tool list is the only
boundary the server can actually draw.

## What would change this

- Authentication that gives the server an identity it did not receive from the
  caller — accounts, mTLS, or a reverse proxy that terminates identity. Then the
  delete route can require a human-authenticated principal and the contract
  becomes a boundary.
- A deployment where the agent has the MCP endpoint but no shell and no network
  egress, which makes the split-origin boundary real for that deployment only.

## References

- `WarTable.Api/Program.cs:112`, `:155-164`
- `WarTable.Api/Endpoints/CampaignEndpoints.cs:89-109`
- `WarTable.Api.Tests/McpToolSurfaceTests.cs` — `Campaign_HasArchiveButNoHardDelete`
- [`../design.md`](../design.md) §2 principles 9, 11, 13
