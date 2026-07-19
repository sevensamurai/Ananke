<!-- topic: cli-overview, tags: nnke, nnke-platform, dotnet-tool, cli, scaffold, validate, mermaid, run, serve, mcp, local-execution -->
# CLI Overview

Ananke ships two CLI tools because design-time workflow work and platform operations are different jobs.

`nnke` exists to make the inner loop faster: scaffold projects, validate manifests, inspect topology, search docs, and work well with MCP-enabled editors. `nnke-platform` exists for the outer loop: deploy remote workflows, inspect live health, compare platforms, and manage federation operations.

→ **Full reference:** [nnke CLI](nnke-tool.md) · [nnke-platform CLI](nnke-platform-tool.md)

---

## Why These Tools Exist

Starting with an agentic framework is usually not blocked by runtime code first. The friction is earlier: choosing a pattern, shaping a workflow, validating topology, and understanding what the framework expects.

That is why `nnke` exists. It shortens the path from idea to runnable project and gives both humans and AI coding tools a machine-readable way to inspect and validate Ananke workflows.

`nnke-platform` solves a different problem. Once workflows leave the local process, you need deployment status, platform credentials, health checks, metrics, teardown, and a federated operational view. Those concerns should not bloat the local design-time tool, so they live in a separate CLI.

---

## Which Tool Does What

At a high level:
- **Ananke** is the runtime framework.
- **`nnke`** is the design-time and diagnostics CLI.
- **`nnke-platform`** is the federation and deployment operations CLI.
- **Copilot / Claude / other MCP clients** become more useful when they can call `nnke` directly instead of guessing.

| Concern | Tool |
|---|---|
| Scaffold projects and manifests | `nnke` |
| Validate topology and inspect project shape | `nnke` |
| Search docs, patterns, and diagnostics | `nnke` |
| Run a manifest locally | `nnke` |
| Use MCP for agentic editing support | `nnke` |
| Deploy to cloud platforms | `nnke-platform` |
| Inspect live deployment health and status | `nnke-platform` |
| Compare platforms, trends, and federation state | `nnke-platform` |
| Manage deployment credentials and teardown | `nnke-platform` |

If you are still shaping a workflow, start with `nnke`. If you are operating a remote workflow across platforms, reach for `nnke-platform`.

---

## Install the tools

```bash
dotnet tool install -g nnke
dotnet tool install -g nnke-platform   # optional: needed only for cloud deployment
```

Verify:

```bash
nnke --version
nnke-platform --version
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

---

## Deploy with `nnke-platform`

```bash
nnke-platform login --platform azure
nnke-platform deploy my-agent.ananke.yml --platform azure-ai
nnke-platform status --workflow my-agent
```

`nnke-platform` handles credentials, cloud-native deployments, replica scaling, rollbacks, and federation mesh management.

→ [Full nnke-platform reference](nnke-platform-tool.md)
