# An actor is a claim, not an identity

## What happens

Provenance is recorded on every mutation (§2 principle 6), but the actor field
is supplied by the caller as free text:

- `Step.Assignee` and `Step.Assigner` (`Models/Step.cs:29`, `:32`), defaulting
  to `""` and `"orchestrator"`.
- `BunkerNote.CreatedBy` (`Models/BunkerNote.cs:39`) and `Evidence.CreatedBy`
  (`Models/Evidence.cs:21`), both defaulting to `"human"`.
- `StepUpdate.Actor` and `ActivityEvent.Actor`, defaulting to `"human"`.
- Every MCP write takes an optional `actor`, normalised by
  `McpResults.Actor` (`Mcp/McpResults.cs:26`): a blank value becomes
  `"agent:unknown"`.

The convention is documented at [`../design.md`](../design.md) §4 *Provenance*:
`human`, `human:<display name>`, or `agent:<name>`. Nothing checks it. An agent
may write `human`, or another agent's name, and the event log records the claim
as fact.

The honest default is the only mitigation: omit the field and you are
`agent:unknown` rather than something more flattering.

## Why it is this way

WarTable has no accounts (§2 principle 9), so the server has no identity to
attribute to. It could, however, *receive* an identity — which is the proposal
this limitation exists to refuse, because receiving is not verifying.

That is the whole argument in one line: **a self-reported identity is a second
field with the same defect as the first.** Whether the agent says `agent:cli` in
`actor` or in a session token it was handed, the server is still recording what
the caller said about itself.

## What it costs

A reader cannot distinguish "the human assigned this to me" from "the agent
assigned this to itself". In practice the damage is bounded, and the boundary is
worth stating: `when` and `what` are recorded by the server and cannot be
claimed — only `who` is unverified. The plan, the step transitions, and the
ordering all remain true.

This is the same shape as §2 principle 10, and gets the same answer. WarTable
does not try to detect a fabricated rationale or a fabricated author; it refuses
to let an unverified field become load-bearing. That is why evidence must
dereference to a URL or commit, and why a decision's `basis` must resolve to a
step: those checks make a claim falsifiable. Authorship is not falsifiable, so
it stays decorative.

## Rejected

- **A session token or client id in the MCP handshake** — the server would issue
  it, which sounds stronger than it is. The token proves *distinctness* between
  two connected clients, not *identity*: the first client to connect is equally
  able to claim any name, and nothing binds the token to a person. Distinctness
  is worth having for concurrency and debugging; it is not worth adding a field
  whose provenance is the problem it was meant to solve.
- **Per-agent signing keys** — a real identity primitive. Rejected because
  provisioning a key requires a human, which principle 13 rules out as a
  precondition, and because the private key would live in the same configuration
  file the agent already reads.
- **The local OS user** — one human, one account: every agent on the machine
  shares it. Distinguishes nothing.
- **Refusing `human` from a non-human transport** — the same listener serves the
  browser and the MCP endpoint
  ([an agent can reach the HTTP API](agent-http-reach-around.md)), so the
  transport does not imply the caller.

## What would change this

An identity provider: the server accepts a credential it can *verify* — OIDC, a
reverse proxy that terminates identity, or mTLS with per-client certificates it
issued and can revoke. At that point `actor` becomes derived from the principal
rather than read from the request, and the field can be trusted as far as the
provider is trusted.

## References

- `WarTable.Api/Models/Step.cs:29`, `:32`
- `WarTable.Api/Models/BunkerNote.cs:39`, `WarTable.Api/Models/Evidence.cs:21`
- `WarTable.Api/Models/StepUpdate.cs:11-12`, `WarTable.Api/Models/ActivityEvent.cs:17`
- [`../design.md`](../design.md) §2 principles 6, 9, 10, 13, §4 *Provenance*
