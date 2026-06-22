<!-- topic: nnke-tools, tags: nnke, nnke-platform, dotnet-tool, cli, tooling, design, federation, deployment -->
# nnke Tools

Ananke ships two focused .NET global tools. They are intentionally separate: `nnke` stays lean
for inner-loop development; `nnke-platform` carries cloud SDK weight for deployment and
federation operations.

Both tools exist for the same reason the framework does: the friction in building production AI systems is rarely in the runtime code first. It is in validating topology before deployment, running without credentials during development, and having a stable inspection surface that AI coding tools can rely on.

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

## nnke-platform — federation & deployment ops

```bash
dotnet tool install -g nnke-platform
```

| Command | What it does |
|---|---|
| `nnke-platform validate <file> --platform <p>` | Validate a manifest's deployability to a target platform |
| `nnke-platform capabilities [--platform <p>]` | List known platform-native tool capabilities |
| `nnke-platform profiles <file> [profile]` | List or inspect deployment profiles in a manifest |
| `nnke-platform deploy <file> --platform <p>` | Deploy a workflow to a target platform |
| `nnke-platform eval [<file>] [--candidates <p,p>] [--format text\|json\|markdown]` | Score a manifest against candidate platforms and rank them |
| `nnke-platform status [--deployment-id <id>]` | Show live deployment status |
| `nnke-platform teardown --deployment-id <id>` | Tear down a deployed workflow |
| `nnke-platform trends [--deployment-id <id>]` | Show metrics trends for deployments |
| `nnke-platform analyze <file>` | Analyze manifest complexity and runtime trends |
| `nnke-platform lineage <cell> <file>` | Show federated ancestor/descendant tree for a cell |
| `nnke-platform mesh <file>` | Mesh status with platform, deployment ID, and remote health |
| `nnke-platform apoptosis [--auto] <file>` | Identify (and optionally teardown) idle or aged cells |
| `nnke-platform compare <cell> --across <p,p>` | Compare token/latency metrics across platforms |
| `nnke-platform events [--follow]` | Stream mesh and deployment events in real time |
| `nnke-platform login --platform <p>` | Configure credentials for a platform |
| `nnke-platform whoami` | Show configured platform identities |
| `nnke-platform adapters list` | List installed adapters with status and version |
| `nnke-platform adapters doctor` | Report unhealthy adapters with remediation hints |

> **Best for:** cloud deployments, federation, platform observability, production ops.

→ [Full nnke-platform Tool Companion guide](nnke-platform-tool.md)

---

## Choosing the right tool

| Scenario | Use |
|---|---|
| Scaffold a new workflow, quickstart, or chatbox project | `nnke` |
| Validate a manifest before committing | `nnke validate` |
| Serve a manifest over HTTP for local testing | `nnke serve` |
| Generate a Mermaid diagram for a PR | `nnke diagram` |
| Use AI editor tool-calls to design workflows | `nnke mcp-server` |
| Check which adapters are installed and healthy | `nnke-platform adapters list / doctor` |
| Deploy to Azure / Google / Anthropic | `nnke-platform deploy` |
| Monitor and tear down a live deployment | `nnke-platform status / teardown` |
| Cross-platform metrics and apoptosis | `nnke-platform trends / compare / apoptosis` |
| Emulate platform-native tools locally *(v-next)* | `nnke-platform up --emulate` |

Both tools can be installed simultaneously — they do not conflict.

