<!-- topic: cli-overview, tags: nnke, dotnet-tool, cli, scaffold, validate, mermaid, run, serve, mcp, local-execution -->
# CLI Overview

Ananke ships one CLI tool, `nnke`, for design-time workflow work.

`nnke` exists to make the inner loop faster: scaffold projects, validate manifests, inspect topology, search docs, and work well with MCP-enabled editors.

→ **Full reference:** [nnke CLI](nnke-tool.md)

---

## Why These Tools Exist

Starting with an agentic framework is usually not blocked by runtime code first. The friction is earlier: choosing a pattern, shaping a workflow, validating topology, and understanding what the framework expects.

That is why `nnke` exists. It shortens the path from idea to runnable project and gives both humans and AI coding tools a machine-readable way to inspect and validate Ananke workflows.

---

## Which Tool Does What

At a high level:
- **Ananke** is the runtime framework.
- **`nnke`** is the design-time and diagnostics CLI.
- **Copilot / Claude / other MCP clients** become more useful when they can call `nnke` directly instead of guessing.

| Concern | Tool |
|---|---|
| Scaffold projects and manifests | `nnke` |
| Validate topology and inspect project shape | `nnke` |
| Search docs, patterns, and diagnostics | `nnke` |
| Run a manifest locally | `nnke` |
| Use MCP for agentic editing support | `nnke` |

Ananke has no deployment CLI. A workflow runs in your own process.

---

## Install the tool

```bash
dotnet tool install -g nnke
```

Verify:

```bash
nnke --version
```

---

## Scaffold a new project

```bash
nnke new workflow my-agent --pattern streaming-chat
cd my-agent
```

This creates a minimal Ananke project with a `Program.cs`, `my-agent.ananke.yml` manifest, and a NuGet reference to `Ananke.Orchestration`.

---

## Validate a topology

```bash
nnke validate my-agent.ananke.yml
```

`nnke` checks the manifest against the Ananke schema, validates that all referenced jobs and edge targets exist, and reports structural errors with line numbers.

---

## Export a Mermaid diagram

```bash
nnke diagram my-agent.ananke.yml --output my-agent.mmd
```

Opens `my-agent.mmd` in any Mermaid viewer (e.g., the VS Code extension or [mermaid.live](https://mermaid.live)) to visualise the workflow graph.

---

## Serve a manifest over local HTTP

```bash
nnke serve my-agent.ananke.yml --port 5100
```

Starts a local HTTP server that accepts workflow trigger requests. Useful for testing client integrations before deploying.

---

## Use `nnke` as an MCP companion

`nnke` exposes all its commands as MCP tools. Add it as an MCP server in your AI coding tool (Copilot, Claude) to let the assistant scaffold manifests, validate topologies, and search the Ananke docs without leaving the editor.

```json
{
  "mcpServers": {
    "nnke": {
      "command": "nnke",
      "args": ["mcp-server"]
    }
  }
}
```
