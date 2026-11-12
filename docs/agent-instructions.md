# Agent instructions

WarTable sends baseline usage guidance to every MCP client during the
`initialize` handshake. The server constant is
[`WarTable.Api/Mcp/WarTableMcpInstructions.cs`](../WarTable.Api/Mcp/WarTableMcpInstructions.cs).
Clients that treat server instructions as system context receive the guidance
automatically.

The copyable fallback prompt is user-facing and lives on the documentation site:
[Embedding WarTable in an agent workflow](https://alex98235.github.io/WarTable/embedding#what-the-agent-should-be-told).

## Keeping the copies in sync

The fallback block exists in two places and must stay identical:

1. The `Text` constant in
   [`WarTableMcpInstructions.cs`](../WarTable.Api/Mcp/WarTableMcpInstructions.cs).
2. The instruction block on the
   [site embedding page](https://alex98235.github.io/WarTable/embedding#what-the-agent-should-be-told).

When MCP behavior changes, update both. The server constant is what clients
receive automatically; the site page is what a human copies into a client that
ignores or strips server instructions. This repository file is a pointer, not a
third copy.
