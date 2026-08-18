# nnke

A .NET global tool for scaffolding Ananke agent projects, and for validating and inspecting them offline.

## Install

```bash
dotnet tool install -g nnke
```

## Usage

```bash
# Scaffold a beginner project that runs immediately — no API key required
nnke new quickstart MyFirstAgent

# Scaffold a streaming chatbox (Minimal API + SSE) — also runs without a key
nnke new chatbox MyChat

# Scaffold a complete workflow project (default: etl pattern, openai provider)
nnke new workflow MyPipeline

# Scaffold with a specific LLM provider and topology pattern
nnke new workflow MyPipeline --provider anthropic --pattern fan-out

# Scaffold a streaming-chat project
nnke new workflow MyChat --pattern streaming-chat

# Scaffold an organic-host project
nnke new workflow MyMesh --pattern organic-host

# Scaffold a project from an agentic design pattern (--list to see them all)
nnke new pattern router MyRouter

# Validate a manifest file
nnke validate my-etl.ananke.yml

# Export a Mermaid diagram from a manifest
nnke diagram my-etl.ananke.yml

# Serve a manifest workflow as a local HTTP endpoint (topology trace, no LLM calls)
nnke serve my-etl.ananke.yml

# Inspect mesh state from a snapshot file
nnke mesh status mesh.snapshot.yml
nnke mesh lineage my-cell mesh.snapshot.yml
nnke mesh trace my-cell mesh.snapshot.yml
nnke mesh inspect memory.json
```

## Commands

| Command | Description |
|---|---|
| `nnke new quickstart <name>` | Scaffold a beginner project that runs immediately — no API key required |
| `nnke new workflow <name>` | Scaffold a runnable workflow project (`.csproj`, `Program.cs`, `.ananke.yml`, state record, secrets template) |
| `nnke new chatbox <name>` | Scaffold a streaming conversational agent (Minimal API + SSE) — no API key required |
| `nnke new pattern <name>` | Scaffold a project from an agentic design pattern (`--list` to see all) |
| `nnke validate <file>` | Parse and validate manifest topology |
| `nnke diagram <file>` | Export Mermaid flowchart from manifest connections |
| `nnke manifest validate <file>` | Validate manifest topology (same checks as `nnke validate`) |
| `nnke manifest diagram <file>` | Export Mermaid flowchart from a manifest |
| `nnke serve <file>` | Serve a manifest workflow as a local HTTP endpoint (topology trace, no LLM calls) |
| `nnke inspect [dir]` | Full project health report |
| `nnke explain [code]` | Explain a diagnostic error code |
| `nnke patterns [pattern]` | List or describe workflow patterns |
| `nnke docs [topic]` | Browse framework documentation |
| `nnke mesh status <file>` | Show alive cells from a mesh snapshot |
| `nnke mesh lineage <cell> <file>` | Show ancestor/descendant tree for a cell |
| `nnke mesh trace <cell> <file>` | Show signal and division history for a cell |
| `nnke mesh inspect <file>` | Browse empirical memory entries |
| `nnke kernel status <file>` | Show kernel snapshot status |
| `nnke kernel history <file>` | Show kernel division history |
| `nnke mcp-server` | Launch as MCP stdio server for AI clients |
| `nnke schema` | Emit JSON schema of all commands |

## Patterns

Pass `--pattern <name>` to `nnke new workflow`. Run `nnke patterns` for full descriptions.

| Pattern | Style | Description |
|---|---|---|
| `etl` | manifest | Extract-Transform-Load with parallel fork/join |
| `fan-out` | manifest | Fan-out to N parallel workers, fan-in to aggregate |
| `sequential` | manifest | Strict linear chain |
| `human-in-the-loop` | manifest | Interrupt-and-resume for human review |
| `review-critique` | code | Generator + critic loop |
| `iterative-refinement` | code | Single-agent self-improvement loop |
| `router` | code | LLM-driven branch selection |
| `handoff` | code | Agent-to-agent delegation via channel |
| `streaming-chat` | code | Token-streaming chat workflow |
| `organic-host` | code | Self-dividing colony host |

## Providers

Pass `--provider <name>` to any `nnke new` scaffold command.

| Provider | Package scaffolded |
|---|---|
| `openai` (default) | `Ananke.Orchestration.OpenAI` |
| `anthropic` | `Ananke.Orchestration.Anthropic` |
| `google` | `Ananke.Orchestration.Google` |

## Architecture boundary

`nnke` is the **developer tool** — zero cloud SDK dependencies, works offline.

| Concern | Tool |
|---|---|
| Scaffold projects & manifests | `nnke` |
| Validate, diagram, inspect | `nnke` |
| Browse docs, patterns, error codes | `nnke` |
| Inspect local mesh snapshots | `nnke` |
| MCP server for AI clients | `nnke mcp-server` |
| Deploy to Azure / Google / Anthropic | `nnke-platform` |
| Live remote deployment status | `nnke-platform` |
| Metrics trends, apoptosis, compare | `nnke-platform` |
| Platform credentials / login | `nnke-platform` |

For federation operations, install `nnke-platform`:
```bash
dotnet tool install -g nnke-platform
```
