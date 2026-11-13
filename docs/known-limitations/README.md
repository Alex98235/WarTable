# Known limitations

Things WarTable does that we know are wrong, with the reason they are still
wrong and the conditions that would change the verdict. One file per
limitation, named for the failure rather than the fix.

This is not a backlog. A backlog entry says *we intend to do this*. An entry
here says *we have decided not to, and here is what that costs*, so that the
next person — human or agent — reads the reasoning instead of rediscovering the
problem and proposing the idea we already rejected.

Not the same as [`../designs/`](../designs/), which holds long-form arguments
for the *current* design: each file expands a section of `../design.md` and
describes what WarTable is. A limitation entry describes something WarTable does
badly and will keep doing badly. One argues for the design, the other argues
about its cost.

The temporary counterpart to both is `.designs/` at the repository root, which
is gitignored and holds notes for work that has not shipped yet.

## What belongs here

- A defect that cannot be fixed without breaking a stated design goal.
- A risk we accept knowingly, where the alternative was considered and refused.
- A property a reader will assume holds and be surprised to find does not.

What does not belong here: a bug with a fix (fix it), a missing feature (write
it down as a GitHub issue, or as a seed in WarTable if you already work there),
or a limitation that exists only because nobody has looked yet.

## Format

- **What happens** — the observable behaviour, with file and line references.
- **Why it is this way** — the design goal that forces it.
- **What it costs** — the realistic failure, not the worst case.
- **Rejected** — each alternative and the specific reason it fails.
- **What would change this** — the condition that reopens the decision.
- **References** — the code and the principles that pin it.

## The list

| Limitation | Costs |
| --- | --- |
| [An agent can reach the HTTP API](agent-http-reach-around.md) | A caller holding the URL can call any route, including the delete agents are not given |
| [An actor is a claim, not an identity](unverified-actor-identity.md) | Provenance records what a caller said about itself; nothing verifies it |
