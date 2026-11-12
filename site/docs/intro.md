---
slug: /
sidebar_position: 1
title: Introduction
---

# Introduction

WarTable is a self-hosted planning and knowledge server for a solo developer and
their agents. It runs on your own machine or a small server on your LAN, keeps
everything in one SQLite database, and gives you and your agents the same view of
the work.

The plan is made of three things:

- **Campaigns** are folders for a body of work. A campaign has a charter but no
  status.
- **Objectives** are durable outcomes inside a campaign. Each one has a
  campaign-scoped alias such as `#4` and a plan state: seed, draft, or active.
- **Steps** are atomic, verifiable work with a stable reference such as `#4.2`,
  explicit dependencies, and an open, in-progress, or done status.

Around the plan, WarTable stores knowledge as Markdown notes:

- **Bunker notes** are campaign-specific directives, decisions, gotchas, and
  context.
- **Preferences** are advisory rules that apply to you across campaigns.

Agents connect over the Model Context Protocol (MCP). They can read the plan,
update steps, and record notes, so the plan stays current without you copying
state between tools. The server also sends live updates to the browser, so an
agent's change appears in the UI as it happens.

WarTable is LAN-only by default. It has no accounts, login, API keys, or TLS.
Anyone who can reach the port can read and change the data, so run it on a
trusted network and do not expose it to the internet.

Start with [Setup](./setup.md) to run the server, then
[Embedding WarTable in an agent workflow](./embedding.md) to connect an agent.
