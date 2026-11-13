# Agent instructions

The canonical text is
[`WarTable.Api/Mcp/instructions.txt`](../WarTable.Api/Mcp/instructions.txt).
That file is the whole instruction block and the only copy of it. The server
embeds it as a resource and `WarTableMcpInstructions.Text` loads it, so what a
client receives during the MCP `initialize` handshake is that file, verbatim.

The documentation site keeps no second copy. `site/docs/embedding.md` imports
`_instructions.generated.mdx`, which `site/scripts/sync-instructions.mjs`
generates from the same file on `prebuild` and `prestart`; the fragment is
gitignored. Nothing is maintained by hand and there is nothing to keep in sync.

Commands, the guards that refuse a truncated or missing block, and why the tool
descriptions matter as much as the block: see
[`development.md`](development.md#agent-instructions).
