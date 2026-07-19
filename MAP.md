# MAP.md — Concept → Doc → Source Routing Table

> Read this before opening any other architecture or doc file. It is the index over the
> doc systems below — it does not duplicate their content, only points into it.

## Which file answers which question

| You need | Read |
|---|---|
| Exact symbol location, callers/callees, "does X exist" | **codegraph** (`codegraph_explore` / `codegraph_search`) — generated from source, always fresh |
| Process-level layer map, full project dependency graph, testing strategy | [`ARCHITECTURE.md`](ARCHITECTURE.md) (repo root) |
| Deep narrative + Mermaid + key types for one vertical | `architecture/*.md` (linked per row below) |
| Solution/package inventory — NuGet IDs, per-package deps, CLI tooling layer, cross-project abstraction trees with per-type source paths | [`src/ARCHITECTURE.md`](src/ARCHITECTURE.md) |
| One project's full type inventory | `src/<Project>/ARCHITECTURE.md` |
| Task-oriented how-to with runnable samples, FAQ, CLI reference | `docs/guides/`, `docs/faq/`, `docs/reference/`, `docs/cli/` |

`ARCHITECTURE.md` and `src/ARCHITECTURE.md` are **not duplicates** despite the similar name: the
root file is the conceptual/vertical map (layer map, verticals, testing strategy, links out to
`architecture/*.md`); `src/ARCHITECTURE.md` is the solution-structure map (every package, its
NuGet ID and dependencies, the CLI tooling layer, and abstraction trees annotated with the
`.cs` file that defines each type). Both are authoritative; the table below tells you which one
(or both) to open for a given concept.

## Concept routing table

| Concept | Architecture | `docs/` guide | Key source dir |
|---|---|---|---|
| Workflows & job execution | [architecture/orchestration.md](architecture/orchestration.md) | [02-workflows](docs/guides/02-workflows.md) | `src/Ananke.Orchestration/Jobs/`, `src/Ananke.Orchestration/Execution/` |
| Routing (`IRouter`/`AgentRouter`) | [architecture/orchestration.md](architecture/orchestration.md) (Routing) | [02-workflows](docs/guides/02-workflows.md) | `src/Ananke.Orchestration/Routing/` |
| Checkpointing / human-in-the-loop | [architecture/orchestration.md](architecture/orchestration.md) (Checkpointing) | [07-human-in-the-loop](docs/guides/07-human-in-the-loop.md) | `src/Ananke.Orchestration/Checkpointing/` |
| Agentic patterns (Router, Handoff, ReviewCritique) | [architecture/orchestration.md](architecture/orchestration.md) (Agentic Patterns) | [16-agentic-patterns](docs/guides/16-agentic-patterns.md) | `src/Ananke.Orchestration/AgenticPattern.cs`, `src/Ananke.Orchestration/Patterns/` |
| Agents & providers (OpenAI/Anthropic/Google) | [architecture/agents.md](architecture/agents.md) | [03-agents](docs/guides/03-agents.md) | `src/Ananke.Orchestration/Agents/`, `src/Ananke.Orchestration.{OpenAI,Anthropic,Google}/` |
| Agent middleware (cache/retry/guardrail/logging) | [architecture/agents.md](architecture/agents.md) (Middleware Pipeline) | [11-advanced-agents](docs/guides/11-advanced-agents.md) | `src/Ananke.Orchestration/Agents/Middleware/` |
| Model routing (`ModelRouter`/`CapabilityModelRouter`) | [architecture/agents.md](architecture/agents.md) (Model Routing) | [11-advanced-agents](docs/guides/11-advanced-agents.md) | `src/Ananke.Orchestration/Agents/Routing/` |
| Streaming chat sessions | [architecture/agents.md](architecture/agents.md) | [05-streaming-chat](docs/guides/05-streaming-chat.md) | `src/Ananke.Orchestration/Agents/StreamingChatWorkflow.cs` |
| Tools / `ToolKit` | [ARCHITECTURE.md#key-abstractions](ARCHITECTURE.md#key-abstractions) | [04-tools](docs/guides/04-tools.md), [tools-reference](docs/reference/tools-reference.md) | `src/Ananke.Orchestration/Tools/` |
| Knowledge / RAG pipeline | [architecture/knowledge.md](architecture/knowledge.md) | [06-memory](docs/guides/06-memory.md) | `src/Ananke.Orchestration.Knowledge/`, `src/Ananke.Documents/` |
| Vector stores (Qdrant) | [architecture/infrastructure.md](architecture/infrastructure.md) (Qdrant) | [06-memory](docs/guides/06-memory.md) | `src/Ananke.Qdrant/` |
| Knowledge graph | [architecture/learning.md](architecture/learning.md) (Knowledge Graph Analytics) | [memory FAQ](docs/faq/memory.md) | `src/Ananke.Graph.Abstractions/`, `src/Ananke.Graph.Memgraph/` |
| State machine | [architecture/infrastructure.md](architecture/infrastructure.md) (State Machine) | [08-state-machine](docs/guides/08-state-machine.md) | `src/Ananke.StateMachine/` |
| Empirical memory / episodes / offline learning | [architecture/learning.md](architecture/learning.md) | [15-empirical-memory](docs/guides/15-empirical-memory.md), [15a-tuning](docs/guides/15a-empirical-memory-tuning.md) | `src/Ananke.Learning/` |
| Organic colony / cell division | [architecture/organics-federation.md](architecture/organics-federation.md) (Organic Colony Architecture) | [organics FAQ](docs/faq/organics.md) | `src/Ananke.Organics/` |
| Federation / cross-cloud deployment | [architecture/organics-federation.md](architecture/organics-federation.md) (Federation) | [20-platform-recommendation](docs/guides/20-platform-recommendation.md), [nnke-platform-tool](docs/cli/nnke-platform-tool.md) | `src/Ananke.Federation/`, `src/Ananke.Federation.{Anthropic,Google,Azure,LocalEmulators}/` |
| Federation credentials & rotation | [architecture/federation-credentials.md](architecture/federation-credentials.md) | [nnke-platform-tool](docs/cli/nnke-platform-tool.md) | `src/Ananke.Federation/Credentials/` |
| Redis / MQTT / distributed primitives | [architecture/infrastructure.md](architecture/infrastructure.md) (Redis, MQTT) | [09-distributed](docs/guides/09-distributed.md) | `src/Ananke.Redis/`, `src/Ananke.MQTT/` |
| Observability (OpenTelemetry) | [architecture/infrastructure.md](architecture/infrastructure.md) (OpenTelemetry) | [10-observability](docs/guides/10-observability.md) | `src/Ananke.OpenTelemetry/` |
| ASP.NET Core / SSE hosting | [architecture/infrastructure.md](architecture/infrastructure.md) (ASP.NET Core) | [05-streaming-chat](docs/guides/05-streaming-chat.md) | `src/Ananke.AspNetCore/` |
| MCP server / A2A protocol | [architecture/interop.md](architecture/interop.md) | [12-mcp-and-interop](docs/guides/12-mcp-and-interop.md) | `src/Ananke.MCP/`, `src/Ananke.A2A/` |
| External skill catalog (OpenClaw) | [architecture/interop.md](architecture/interop.md) (External Skill Catalog) | [tools-reference](docs/reference/tools-reference.md) | `src/Ananke.Skills/` |
| Messaging platform adapters (Slack/Discord) | [architecture/interop.md](architecture/interop.md) (Messaging Platforms) | — *(no guide yet)* | `src/Ananke.Platforms/`, `src/Ananke.Platforms.{Slack,Discord}/` |
| Roles / studio host / role catalog | — *(not yet in `architecture/*.md`; see `src/Ananke.Roles/ARCHITECTURE.md`)* | — *(no guide yet)* | `src/Ananke.Roles/` |
| Design DSL / YAML manifests | — *(not yet in `architecture/*.md`; see `src/Ananke.Design/ARCHITECTURE.md`)* | [13-design-tooling](docs/guides/13-design-tooling.md), [workflow-dsl](docs/reference/workflow-dsl.md) | `src/Ananke.Design/` |
| Testing helpers / in-memory fakes | [ARCHITECTURE.md#testing-strategy](ARCHITECTURE.md#testing-strategy) | [14-testing](docs/guides/14-testing.md) | per-project `*.Tests/` |
| `nnke` CLI (design-time) | [src/ARCHITECTURE.md](src/ARCHITECTURE.md) (CLI / Tooling Layer) | [nnke-tool](docs/cli/nnke-tool.md), [overview](docs/cli/overview.md) | `src/nnke/` |
| `nnke-platform` CLI (federation ops) + adapters | [src/ARCHITECTURE.md](src/ARCHITECTURE.md) (CLI / Tooling Layer) | [nnke-platform-tool](docs/cli/nnke-platform-tool.md) | `src/nnke-platform/`, `src/nnke-platform-{anthropic,azure,google,all}/` |

## Maintenance

- This table grows by *concept*, not by file. When a new `architecture/*.md` or numbered guide
  ships, extend or add a row instead of starting a parallel list elsewhere — that kind of
  duplication (two routing tables drifting independently) is exactly what this file replaces.
- `scripts/check-docs.ps1` does not scan this file by default (`MAP.md` lives at the repo root,
  outside its `src`/`docs` scan roots) — but it will catch breakage on whatever you touched at
  the other end of a link you add here. Run it after editing a row's targets.
- A `—` entry means the doc genuinely does not exist yet. Do not fill it with a guess; add the
  real file first, then fill in the row.
