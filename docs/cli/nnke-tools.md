<!-- topic: nnke-tools, tags: nnke, dotnet-tool, cli, tooling, design -->
# nnke Tools

Ananke ships one .NET global tool, `nnke`, for inner-loop development.

It exists for the same reason the framework does: the friction in building production AI systems is rarely in the runtime code first. It is in validating topology before deployment, running without credentials during development, and having a stable inspection surface that AI coding tools can rely on.

---

## nnke — design, local execution & diagnostics

Install as a .NET global tool, then use it as an everyday coding companion and MCP server for
AI editors (Copilot, Claude, Cursor, etc.).

```bash
dotnet tool install -g nnke
```

| Command | What it does |
|---|---|
| `nnke new quickstart <Name>` | Scaffold a minimal single-agent console project |
| `nnke new workflow <Name>` | Scaffold a complete workflow project (manifest-driven patterns also emit `.ananke.yml`) |
| `nnke new chatbox <Name>` | Scaffold a streaming conversational agent (ASP.NET Minimal API + SSE) |
| `nnke new pattern <Name> --pattern <key>` | Scaffold a named agentic-design-pattern project |
| `nnke validate <file>` | Validate manifest topology (dead ends, unreachable nodes, cycles) |
| `nnke serve <file> [--port N]` | Serve a manifest over HTTP locally (NDJSON event stream) |
| `nnke diagram <file>` | Export workflow as a Mermaid flowchart |
| `nnke inspect [dir]` | Analyze an Ananke project for health and dependency issues |
| `nnke explain <code>` | Explain a diagnostic error code |
| `nnke patterns` | List or describe available workflow patterns |
| `nnke docs <topic>` | Read or search documentation from the terminal |
| `nnke schema` | Emit the command catalog for self-discovery |
| `nnke mcp-server` | Run `nnke` as an MCP stdio server for AI editors |
| `nnke mesh` | Inspect local Organics mesh snapshots |
| `nnke kernel` | Inspect the local Organics kernel |

> **Best for:** scaffolding, topology validation, local execution, diagrams, docs, MCP integration.

→ [Full nnke Tool Companion guide](nnke-tool.md)

---

## Choosing a command

| Scenario | Use |
|---|---|
| Scaffold a new workflow, quickstart, or chatbox project | `nnke` |
| Validate a manifest before committing | `nnke validate` |
| Serve a manifest over HTTP for local testing | `nnke serve` |
| Generate a Mermaid diagram for a PR | `nnke diagram` |
| Use AI editor tool-calls to design workflows | `nnke mcp-server` |

