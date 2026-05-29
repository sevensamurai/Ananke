<!-- topic: demos, tags: documentation, demos, examples, runnable-projects, catalog -->
# Demos

Runnable Ananke projects mapped to the documentation. Use this page when you want to see a feature implemented end-to-end instead of reading the guide first.

If you want the self-guided learning path, start with [Learning Path](learning-path.md). If you want a feature lookup, use the [Feature Index](reference/features.md).

---

## Demo Catalog

The tables below map the guide set to the demo projects in the main repository, so you can jump straight from a documentation topic to running code.

### Foundation

| # | Guide | What you'll learn | Demo |
|---|---|---|---|
| 01 | [Getting Started](guides/01-getting-started.md) | Install Ananke, build your first workflow, make your first LLM call | [BasicAgentDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/01-foundations/BasicAgentDemo) |
| 02 | [Workflows](guides/02-workflows.md) | Workflow builder, conditional routing, fork/join parallelism, sub-workflows, event streaming | [AgenticDesignPatternsDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/02-workflow-patterns/AgenticDesignPatternsDemo) |
| 03 | [Agents](guides/03-agents.md) | `AgentJob`, LLM providers (OpenAI, Anthropic, Google, local), structured output, model routing, multimodal messages (text, image, audio), `ModelCapability` flags | [BasicAgentDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/01-foundations/BasicAgentDemo) |
| 04 | [Tools](guides/04-tools.md) | `ToolKit`, typed parameters, `ToolResult.Ok`/`Error`, writing effective descriptions | [BasicAgentDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/01-foundations/BasicAgentDemo) |

### Building Real Applications

| # | Guide | What you'll learn | Demo |
|---|---|---|---|
| 05 | [Streaming Chat](guides/05-streaming-chat.md) | `StreamingChatWorkflow`, `ChatSessionEvent` / `BuildStream()`, `ChatSession<S,T>`, `InMemorySessionStore`, SSE endpoints, web UI integration | [AgenticWebDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/05-applications/AgenticWebDemo) · [PetAdoptionDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/05-applications/PetAdoptionDemo) |
| 06 | [Long-Term Memory](guides/06-memory.md) | Knowledge pipeline (extract → chunk → embed → store), `KnowledgeBase` multi-section grouping, catalog, time-decay reranking, agent-driven ingestion | [LongTermMemoryDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/03-memory-and-knowledge/LongTermMemoryDemo) |
| 07 | [Human-in-the-Loop](guides/07-human-in-the-loop.md) | Interrupt before/after, checkpointing, resume with modified state, work-review gates, async review parking, budget gates | [AgenticWebDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/05-applications/AgenticWebDemo) · [MiniAgencyDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/05-applications/MiniAgencyDemo) |

### Production Infrastructure

| # | Guide | What you'll learn | Demo |
|---|---|---|---|
| 08 | [State Machine](guides/08-state-machine.md) | `IStateMachine<S,T>` (simplified, in-process, interrupt stack), `AbstractStateMachine` (distributed), transitions, guard conditions, middleware pipeline, circuit breaking | [StateMachineDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/01-foundations/StateMachineDemo) · [PetAdoptionDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/05-applications/PetAdoptionDemo) · [Connect4Demo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/04-organics-and-emergence/Connect4Demo) |
| 09 | [Distributed Systems](guides/09-distributed.md) | Redis locking, MQTT pub/sub, agent handoff across processes, Bridge layer | [PetAdoptionDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/05-applications/PetAdoptionDemo) |
| 10 | [Observability](guides/10-observability.md) | OpenTelemetry tracing, OTLP export, span attributes, retry event reporting | — |

### Advanced Topics

| # | Guide | What you'll learn | Demo |
|---|---|---|---|
| 11 | [Advanced Agent Features](guides/11-advanced-agents.md) | Response caching, resilient retries, decorator composition, local/custom LLM endpoints | — |
| 12 | [MCP & Interop](guides/12-mcp-and-interop.md) | Expose tools/workflows as MCP server, consume external MCP tools, A2A protocol | [McpServerDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/06-interop-and-channels/McpServerDemo) |
| 13 | [Design Tooling](guides/13-design-tooling.md) | Text DSL, YAML manifests, `Bind()` at runtime, Mermaid diagram export | [DesignPipelineDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/02-workflow-patterns/DesignPipelineDemo) |
| 14 | [Testing](guides/14-testing.md) | In-memory implementations for every contract, zero-config integration tests, test patterns | — |
| 15 | [Empirical Memory](guides/15-empirical-memory.md) | `IEmpiricalMemory`, `EmpiricalMemoryTools` (`recall_empirical`, `commit_insight`, `reinforce_empirical`), `EmpiricalKind` (Pattern / Skill / Heuristic), confidence tracking, dedup, `InMemoryEmpiricalMemory` and `QdrantEmpiricalMemory` | [Connect4Demo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/04-organics-and-emergence/Connect4Demo) |
| 16 | [Agentic Patterns](guides/16-agentic-patterns.md) | `AgenticPattern` builder, Review & Critique, Iterative Refinement — pre-wired validated topologies on top of `Workflow<TState>` primitives | [AgenticDesignPatternsDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/02-workflow-patterns/AgenticDesignPatternsDemo) |

---

### Emergence & Learning

These demos validate the longer-arc capabilities described in the [Background & Philosophy](about/background.md) — specifically that cell division works end-to-end and that empirical memory accumulates and replays correctly. Run them before building on these layers to confirm the baseline behaviour on your machine.

| Demo | What it validates | Notes |
|---|---|---|
| [OrganicKernelDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/04-organics-and-emergence/OrganicKernelDemo) | Cell division end-to-end: a generalist workflow accumulates tools, structural tension is detected, a division is proposed and approved, two specialised peers are spawned, the parent is killed, and the outcome is recorded into empirical memory. | Runs with no API keys. The flagship demonstration of `Ananke.Organics`. |
| [LearningPrimitivesDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/04-organics-and-emergence/LearningPrimitivesDemo) | `commit`, `recall`, `reinforce`, `contradict`, offline learning sweeps with decay and consolidation — the empirical memory loop in isolation. | Runs with no API keys. Start here before building on `IEmpiricalMemory`. |
| [Connect4Demo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/04-organics-and-emergence/Connect4Demo) | Empirical memory accumulating game strategy over repeated self-play sessions; confidence scores shifting with wins and losses. | Demonstrates the offline learner running between sessions. |

> **Honest status:** Multi-generation lineage (a specialist dividing again) and closed-loop learning (division outcomes driving the next policy decision end-to-end) are structurally wired but not yet exercised in these demos. See the [Roadmap](about/roadmap.md) for what is complete and what is ahead.

---

## Featured Walkthroughs

- [Organic Kernel Demo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/04-organics-and-emergence/OrganicKernelDemo) — the flagship Organics demo: cell division from detection to spawned peers, end-to-end, no API keys.
- [Mini Agency Demo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/05-applications/MiniAgencyDemo) — a Slack-backed draft-and-review application wiring `Ananke.Roles`, work-review gates, async parking, and optional OTel metrics in a realistic end-to-end flow.
- [Multi-Agent Demo](demos/multi-agent.md) — router, worker, reviewer orchestration in one focused walkthrough.
- [Tools Demo](demos/tools.md) — concrete ToolKit registration patterns, typed parameters, and async tools.
- [CLI Overview](cli/overview.md) — scaffold, validate, run, and serve workflows from the terminal.

---

## Other Resources

| Resource | Description |
|---|---|
| [Feature Index](reference/features.md) | Every feature in one table — description, guide, package, and demo links |
| [Learning Path](learning-path.md) | The self-guided path through the numbered guides |
| [Background & Philosophy](about/background.md) | Why the framework is named Ananke and what "infrastructure first" means |
| [Package READMEs](https://github.com/sevensamurai/Ananke#packages) | Per-package API documentation and quick-start |
| [Demo Projects](https://github.com/sevensamurai/Ananke/tree/main/src/demos) | Runnable examples for every major feature |
| [Release Notes](https://github.com/sevensamurai/Ananke/tree/main/releases) | What changed in each version |
