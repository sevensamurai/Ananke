<!-- topic: nnke, tags: nnke, dotnet-tool, copilot, claude, design-tooling, workflow-dsl, mcp -->
# nnke Tool Companion

`nnke` is the design-time CLI for Ananke: scaffold projects, validate manifests, inspect topology, browse docs, and expose those capabilities to MCP-enabled editors.

This is infrastructure for the inner loop — the same philosophy as the framework itself. Validate topology at build time, run without credentials, and give AI coding tools a machine-readable inspection surface so they can help rather than guess.

For the shared rationale behind the CLI split and the high-level difference between `nnke` and `nnke-platform`, start with [CLI Overview](overview.md).

---

## What `nnke` Covers

Use `nnke` when you are still shaping or validating a workflow locally.

Core responsibilities:
- scaffold a new workflow, quickstart, chatbox, or named-pattern project
- validate `.ananke.yml` topology files
- export Mermaid diagrams
- inspect an existing Ananke project for health and dependency issues, or an organic mesh/kernel's runtime state
- browse Ananke docs and patterns from the terminal
- expose those capabilities as MCP tools for AI clients

---

## Installation

### Prerequisites

`nnke` currently targets `.NET 10`, so install a .NET 10 SDK before installing the tool.

Check your SDK:

```bash
dotnet --version
```

### Install Globally

Install the tool globally so `nnke` is available anywhere:

```bash
dotnet tool install -g nnke
```

Update later with:

```bash
dotnet tool update -g nnke
```

### Install Per-Repo

If you prefer a local tool manifest instead of a global install:

```bash
dotnet new tool-manifest
dotnet tool install nnke
dotnet tool run nnke --help
```

For local tool installs, the safest invocation is:

```bash
dotnet tool run nnke <command>
```

### Verify The Install

```bash
nnke --help
```

If you installed locally rather than globally:

```bash
dotnet tool run nnke --help
```

---

## Core Commands

These are the primary commands available today:

| Command | What it does |
|---|---|
| `nnke new quickstart <name>` | Scaffold a minimal single-agent console project (Guide 01) |
| `nnke new workflow <name>` | Scaffold a complete workflow project (manifest-driven patterns also emit `.ananke.yml`) |
| `nnke new chatbox <name>` | Scaffold a streaming conversational agent (ASP.NET Minimal API + SSE) |
| `nnke new pattern <name> --pattern <key>` | Scaffold a named agentic-design-pattern project |
| `nnke validate <file>` | Validate a manifest and its topology |
| `nnke serve <file> [--port N]` | Serve a manifest over HTTP locally (NDJSON event stream) |
| `nnke diagram <file>` | Export a Mermaid flowchart from a manifest |
| `nnke inspect [dir]` | Analyze an existing Ananke project directory |
| `nnke mesh` | Inspect organic mesh state — cells, lineage, memory, signals |
| `nnke kernel` | Inspect organic workflow kernels — active cells, domains, division history |
| `nnke docs --list` | List available documentation topics |
| `nnke docs <topic>` | Read a specific doc topic |
| `nnke docs --search "<query>"` | Search the docs from the terminal |
| `nnke explain <code>` | Explain a diagnostic code |
| `nnke patterns` | List or describe available workflow patterns |
| `nnke schema` | Emit the command catalog for self-discovery |
| `nnke mcp-server` | Run `nnke` as an MCP stdio server |

Most commands also support:

```bash
--json
```

That matters because AI tools can consume structured output directly instead of scraping human-formatted terminal text.

---

## First-Time Usage

### Scaffold A Workflow Project

Create a new workflow project using the default provider and pattern:

```bash
nnke new workflow MyWorkflow
```

Choose a provider and pattern explicitly:

```bash
nnke new workflow SupportTriage --provider anthropic --pattern fan-out
```

You can also generate machine-readable output:

```bash
nnke new workflow SupportTriage --provider anthropic --pattern fan-out --json
```

The scaffolded project includes a `.csproj`, `Program.cs`, state type, `secrets.json`,
and for manifest-driven patterns a `.ananke.yml` file.

### Manifest-Driven Patterns Also Emit YAML

There is no separate "manifest only" command. For manifest-driven patterns
(`etl`, `fan-out`, `sequential`, `sub-workflow`), `nnke new workflow` emits a
`.ananke.yml` alongside the project automatically:

```bash
nnke new workflow ticket-pipeline --provider openai --pattern etl
```

### Validate A Manifest

```bash
nnke validate ticket-pipeline.ananke.yml
```

For structured diagnostics:

```bash
nnke validate ticket-pipeline.ananke.yml --json
```

### Export A Diagram

Print Mermaid to stdout:

```bash
nnke diagram ticket-pipeline.ananke.yml
```

Write it to a file:

```bash
nnke diagram ticket-pipeline.ananke.yml --output ticket-pipeline.mmd
```

### Inspect A Project

From inside a project directory:

```bash
nnke inspect .
```

For agent-friendly output:

```bash
nnke inspect . --json
```

This is useful for checking:
- manifests found in the directory
- detected topology pattern
- missing or invalid model references
- package references and project health suggestions

---

## How It Fits With Copilot And Claude

The strongest workflow is usually:

1. Use Copilot or Claude to describe the system you want.
2. Use `nnke` to scaffold, shape, or validate the workflow representation.
3. Move into Ananke code when you are ready to bind jobs, routers, merges, and runtime policies.
4. Use docs reference pages to verify exact framework concepts and APIs.

This works especially well for teams that want:
- AI help without giving up typed .NET runtime code
- a clean separation between workflow topology and job behavior
- reviewable workflow artifacts such as DSL, YAML, or Mermaid

The important design choice is that `nnke` can also emit structured JSON.
That makes it a better companion for AI tooling than a CLI that only prints prose.

Examples:

```bash
nnke validate pipeline.ananke.yml --json
nnke inspect . --json
nnke docs --search "fork join topology" --json
nnke explain ANANKE_TOPO_003 --json
```

---

## Using `nnke` With MCP

`nnke` can also run as an MCP stdio server:

```bash
nnke mcp-server
```

That exposes the CLI capabilities as MCP tools for clients such as VS Code Copilot,
Claude Desktop, Cursor, or any other MCP-compatible host.

This is the cleanest integration when you want AI tools to:
- inspect an Ananke project
- validate manifests
- search or read the docs
- explain diagnostics
- query available patterns

For the broader protocol background, see [12 — MCP & Interop](../guides/12-mcp-and-interop.md).

---

## Docs And Pattern Discovery

Two commands are especially useful when you are still learning the framework:

List patterns:

```bash
nnke patterns
```

Describe one pattern:

```bash
nnke patterns review-critique
```

Search the docs:

```bash
nnke docs --search "state machine interrupts"
```

Read a topic directly:

```bash
nnke docs workflows
```

Note: the docs command reads from the repository documentation, so it is most useful when run from inside an Ananke repo checkout.

---

## Typical Workflow

### 1. Start With An Intent

Write down the system in plain English first.

Example:

> Build a workflow that classifies inbound support tickets, enriches missing context,
> routes urgent cases for human approval, and drafts a customer reply.

### 2. Turn Intent Into A Topology

Move the intent into a workflow representation such as the Ananke DSL or YAML manifest.
That is the point where `nnke` becomes especially useful.

For example:

```bash
nnke new workflow support-triage --pattern fan-out
nnke validate support-triage.ananke.yml
nnke diagram support-triage.ananke.yml
```

Related docs:
- [13 — Design Tooling](../guides/13-design-tooling.md)
- [Workflow DSL](../reference/workflow-dsl.md)

### 3. Bind Behavior In .NET

Once the shape is right, implement the actual work in Ananke:
- jobs
- routers
- merge functions
- agent prompts
- toolkits
- persistence and observability

Start with:
- [01 — Getting Started](../guides/01-getting-started.md)
- [02 — Workflows](../guides/02-workflows.md)
- [03 — Agents](../guides/03-agents.md)
- [04 — Tools](../guides/04-tools.md)

### 4. Review And Iterate

Use diagrams, manifests, and prompt-driven refinement loops to improve the topology before
or alongside implementation.

This is where `nnke` helps reduce churn in application code.

---

## When To Reach For `nnke`

Use it when:
- you are still designing the graph
- you need to scaffold a new workflow quickly
- you want AI help producing a first draft of a workflow
- you want something reviewable before coding every job
- you are iterating on topology faster than on implementation details
- you want machine-readable diagnostics or project inspection

Skip it when:
- you already know the workflow shape and just need to code it directly
- you are debugging runtime behavior in production
- you are working mainly on provider configuration, storage, or hosting

---

## A Good Documentation Path For `nnke` Users

If your main entry point is the tool rather than the framework itself, read in this order:

1. [01 — Getting Started](../guides/01-getting-started.md)
2. [13 — Design Tooling](../guides/13-design-tooling.md)
3. [Workflow DSL](../reference/workflow-dsl.md)
4. [12 — MCP & Interop](../guides/12-mcp-and-interop.md)
5. [02 — Workflows](../guides/02-workflows.md)
6. [14 — Testing](../guides/14-testing.md)

That sequence helps you move from CLI-assisted design into executable implementation without losing track
of how the final workflow behaves at runtime.

---

## Quick Reference

```bash
# install
dotnet tool install -g nnke

# scaffold a minimal single-agent project
nnke new quickstart MyAgent

# scaffold a workflow project
nnke new workflow MyWorkflow --provider openai --pattern etl

# validate topology
nnke validate MyWorkflow.ananke.yml --json

# serve over local HTTP (NDJSON stream, default port 5000)
nnke serve MyWorkflow.ananke.yml --port 5000

# inspect the current project
nnke inspect . --json

# export Mermaid
nnke diagram MyWorkflow.ananke.yml --output workflow.mmd

# search docs
nnke docs --search "fork join" --json

# list patterns
nnke patterns --json

# run as MCP server
nnke mcp-server
```

---

## Related Pages

- [Welcome](../welcome.md)
- [Learning Path](../learning-path.md)
- [Demos](../demos.md)
- [nnke Tools Overview](nnke-tools.md)
- [nnke-platform Tool Companion](nnke-platform-tool.md)
- [01 — Getting Started](../guides/01-getting-started.md)
- [13 — Design Tooling](../guides/13-design-tooling.md)
- [Workflow DSL](../reference/workflow-dsl.md)
- [12 — MCP & Interop](../guides/12-mcp-and-interop.md)

---

← [Back to Welcome](../welcome.md)
