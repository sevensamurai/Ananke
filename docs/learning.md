# Ananke Documentation

Welcome to the Ananke documentation. You can follow the guides in order as a
**progressive learning path**, or jump directly to any topic that interests you.

Each guide is self-contained with working code examples.
The [demos/](../src/demos/) directory has runnable projects that correspond to the
concepts in each guide.

→ Looking for a specific feature? See the **[Feature Index](reference/features.md)** — every
capability in one scannable table.

---

## Learning Path

The guides are numbered so you can work through them sequentially. Each one
builds on concepts from the previous ones, but includes enough context to
stand alone.

### Foundation

| # | Guide | What you'll learn | Demo |
|---|---|---|---|
| 01 | [Getting Started](guides/01-getting-started.md) | Install Ananke, build your first workflow, make your first LLM call | [SimpleWorkflowDemo](../src/demos/SimpleWorkflowDemo/) |
| 02 | [Workflows](guides/02-workflows.md) | Workflow builder, conditional routing, fork/join parallelism, sub-workflows, event streaming | [ExtendedFlowDemo](../src/demos/ExtendedFlowDemo/) |
| 03 | [Agents](guides/03-agents.md) | `AgentJob`, LLM providers (OpenAI, Anthropic, Google, local), structured output, model routing, multimodal messages (text, image, audio), `ModelCapability` flags | [BasicAgentDemo](../src/demos/BasicAgentDemo/) |
| 04 | [Tools](guides/04-tools.md) | `ToolKit`, typed parameters, `ToolResult.Ok`/`Error`, writing effective descriptions | [BasicAgentDemo](../src/demos/BasicAgentDemo/) |

### Building Real Applications

| # | Guide | What you'll learn | Demo |
|---|---|---|---|
| 05 | [Streaming Chat](guides/05-streaming-chat.md) | `StreamingChatWorkflow`, `ChatSessionEvent` / `BuildStream()`, `ChatSession<S,T>`, `InMemorySessionStore`, SSE endpoints, web UI integration | [AgenticWebDemo](../src/demos/AgenticWebDemo/) · [PetAdoptionDemo](../src/demos/PetAdoptionDemo/) |
| 06 | [Long-Term Memory](guides/06-memory.md) | Knowledge pipeline (extract → chunk → embed → store), `KnowledgeBase` multi-section grouping, catalog, time-decay reranking, agent-driven ingestion | [LongTermMemoryDemo](../src/demos/LongTermMemoryDemo/) |
| 07 | [Human-in-the-Loop](guides/07-human-in-the-loop.md) | Interrupt before/after, checkpointing, resume with modified state | [AgenticWebDemo](../src/demos/AgenticWebDemo/) |

### Production Infrastructure

| # | Guide | What you'll learn | Demo |
|---|---|---|---|
| 08 | [State Machine](guides/08-state-machine.md) | `IStateMachine<S,T>` (simplified, in-process, interrupt stack), `AbstractStateMachine` (distributed), transitions, guard conditions, middleware pipeline, circuit breaking | [StateMachineDemo](../src/demos/StateMachineDemo/) · [PetAdoptionDemo](../src/demos/PetAdoptionDemo/) · [Connect4Demo](../src/demos/Connect4Demo/) |
| 09 | [Distributed Systems](guides/09-distributed.md) | Redis locking, MQTT pub/sub, agent handoff across processes, Bridge layer | [PetAdoptionDemo](../src/demos/PetAdoptionDemo/) |
| 10 | [Observability](guides/10-observability.md) | OpenTelemetry tracing, OTLP export, span attributes, retry event reporting | — |

### Advanced Topics

| # | Guide | What you'll learn | Demo |
|---|---|---|---|
| 11 | [Advanced Agent Features](guides/11-advanced-agents.md) | Response caching, resilient retries, decorator composition, local/custom LLM endpoints | — |
| 12 | [MCP & Interop](guides/12-mcp-and-interop.md) | Expose tools/workflows as MCP server, consume external MCP tools, A2A protocol | [McpServerDemo](../src/demos/McpServerDemo/) |
| 13 | [Design Tooling](guides/13-design-tooling.md) | Text DSL, YAML manifests, `Bind()` at runtime, Mermaid diagram export | [DesignPipelineDemo](../src/demos/DesignPipelineDemo/) |
| 14 | [Testing](guides/14-testing.md) | In-memory implementations for every contract, zero-config integration tests, test patterns | — |
| 15 | [Empirical Memory](guides/15-empirical-memory.md) | `IEmpiricalMemory`, `EmpiricalMemoryTools` (`recall_empirical`, `commit_insight`, `reinforce_empirical`), `EmpiricalKind` (Pattern / Skill / Heuristic), confidence tracking, dedup, `InMemoryEmpiricalMemory` and `QdrantEmpiricalMemory` | [Connect4Demo](../src/demos/Connect4Demo/) |

---

## Topic Index

If you know what you're looking for, jump straight to a topic:

### Concepts
- **Workflows** — [Guide 02](guides/02-workflows.md) · Routing, parallelism, sub-workflows, streaming
- **Agents** — [Guide 03](guides/03-agents.md) · LLM integration, structured output, model routing
- **Multimodal messages** — [Guide 03](guides/03-agents.md) · `ContentPart` (text, image, audio), `ModelCapability` flags, automatic capability inference
- **Tools** — [Guide 04](guides/04-tools.md) · Function calling, typed parameters, error handling
- **State machines** — [Guide 08](guides/08-state-machine.md) · `IStateMachine<S,T>` (in-process) · `AbstractStateMachine` (distributed), interrupt stack, middleware
- **Long-term memory** — [Guide 06](guides/06-memory.md) · Vector search, document ingestion, `KnowledgeBase`, catalog
- **Empirical memory** — [Guide 15](guides/15-empirical-memory.md) · Patterns, skills, heuristics learned from interaction, confidence tracking

### Patterns
- **Streaming chat with web UI** — [Guide 05](guides/05-streaming-chat.md)
- **Stateful multi-phase SSE chatbot** — [Guide 05](guides/05-streaming-chat.md) + [Guide 08](guides/08-state-machine.md) · `ChatSession<S,T>`, `IStateMachine<S,T>`, `InMemorySessionStore`
- **Human-in-the-loop approval** — [Guide 07](guides/07-human-in-the-loop.md)
- **Interrupt and resume mid-conversation** — [Guide 08](guides/08-state-machine.md) · `ToInterrupt` / `ToResume`, interrupt stack
- **Agents that learn from experience** — [Guide 15](guides/15-empirical-memory.md) · `IEmpiricalMemory`, `EmpiricalMemoryTools`
- **Distributed coordination** — [Guide 09](guides/09-distributed.md)
- **Resilience & caching** — [Guide 11](guides/11-advanced-agents.md)
- **MCP server** — [Guide 12](guides/12-mcp-and-interop.md)
- **Testing without LLMs** — [Guide 14](guides/14-testing.md)

### Providers & Infrastructure
- **OpenAI / Ollama / LM Studio / Azure OpenAI** — [Guide 03](guides/03-agents.md) + [Guide 11](guides/11-advanced-agents.md)
- **Anthropic (Claude)** — [Guide 03](guides/03-agents.md)
- **Google Gemini** — [Guide 03](guides/03-agents.md)
- **Ananke.AspNetCore** — [Guide 05](guides/05-streaming-chat.md) · `ChatSession<S,T>`, `InMemorySessionStore`, SSE extensions
- **Redis** — [Guide 09](guides/09-distributed.md)
- **MQTT** — [Guide 09](guides/09-distributed.md)
- **Qdrant** — [Guide 06](guides/06-memory.md) (knowledge store) · [Guide 15](guides/15-empirical-memory.md) (empirical memory)
- **OpenTelemetry** — [Guide 10](guides/10-observability.md)

---

## Other Resources

| Resource | Description |
|---|---|
| [Feature Index](reference/features.md) | Every feature in one table — description, guide, package, and demo links |
| [Background & Philosophy](about/background.md) | Why the framework is named Ananke and what "infrastructure first" means |
| [Framework Comparison](about/framework-comparison.md) | Feature-by-feature comparison with LangGraph, Semantic Kernel, CrewAI, and others |
| [Package READMEs](../README.md#packages) | Per-package API documentation and quick-start |
| [Demo Projects](../src/demos/) | Runnable examples for every major feature |
| [Release Notes](../releases/) | What changed in each version |

---

## Suggested Learning Paths

### "I want to build a chatbot"
1 → 3 → 4 → 5

### "I want to build a stateful multi-phase chatbot"
1 → 3 → 4 → 5 → 8

### "I want to build an agentic workflow"
1 → 2 → 3 → 4 → 7

### "I want RAG / document Q&A"
1 → 3 → 4 → 6

### "I want agents that learn from experience"
1 → 3 → 4 → 6 → 15

### "I want distributed multi-service coordination"
1 → 2 → 8 → 9

### "I want to go to production"
1 → (your feature path) → 10 → 11 → 14

---

← [Back to README](../README.md)
