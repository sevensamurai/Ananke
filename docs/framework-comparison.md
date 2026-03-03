# Ananke — Framework Comparison

> Last updated: March 2026.
> Frameworks compared: LangChain / LangGraph / Deep Agents, Microsoft Agent Framework (successor to AutoGen), Semantic Kernel (Process Framework), CrewAI, Smolagents, Agno (AgentOS), Ananke.

---

## At a Glance

| | LangChain / LangGraph / Deep Agents | Agent Framework | Semantic Kernel | CrewAI | Smolagents | Agno | **Ananke** |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **Primary language** | Python / TypeScript | Python / C# | C# / Python / Java | Python | Python | Python | **C#** |
| **Core abstraction** | Chain + Graph + Deep Agent | Agent + Graph | Plugin / process | Crew + Flows | Code agent | Agent + OS | **Workflow + FSM** |
| **License** | MIT | MIT | MIT | MIT | Apache 2.0 | Apache 2.0 | **Apache 2.0** |

---

## Workflow & Orchestration

| Capability | LangChain / LangGraph / Deep Agents | Agent Framework | Semantic Kernel | CrewAI | Smolagents | Agno | **Ananke** |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **Workflow-as-code** (no YAML required) | ✅ | ✅ graph workflows | ⚠️ verbose event wiring | ✅ Flows | ❌ | ✅ | ✅ Fluent builder |
| **Conditional routing** | ✅ lambdas | ✅ graph edges | ⚠️ event-based | ✅ `@router` decorator | ❌ | ⚠️ agent-driven | ✅ `Decide()` lambdas |
| **Parallel execution** (fork / join) | ✅ fan-out/fan-in | ✅ graph fan-out | ❌ | ✅ parallel tasks | ✅ multi-agent | ✅ teams | ✅ `Fork()` / `Join()` |
| **Sub-workflow composition** | ✅ subgraphs | ✅ subgraphs | ✅ process-in-process | ⚠️ nested crews | ❌ | ⚠️ nested workflows | ✅ `SubFlow()` |
| **Human-in-the-loop** | ✅ interrupt | ✅ built-in | ❌ | ✅ human input | ✅ human approval | ✅ approval flows | ✅ `InterruptBefore/After` |
| **Type-safe state** | ❌ TypedDict / string keys | ⚠️ C# side only | ✅ C# types | ❌ | ❌ | ❌ | ✅ `TState` generic |
| **Graph validation** (design-time errors) | ⚠️ runtime only | ⚠️ runtime only | ❌ | ❌ | ❌ | ❌ | ✅ Build-time validation |
| **DSL / scaffold tooling** | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ Text DSL + `Bind()` |
| **Diagram export** | ✅ LangGraph Studio | ✅ DevUI | ❌ | ❌ | ❌ | ❌ | ✅ Mermaid / Markdown |

---

## Agent & LLM Integration

| Capability | LangChain / LangGraph / Deep Agents | Agent Framework | Semantic Kernel | CrewAI | Smolagents | Agno | **Ananke** |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **Agents as workflow nodes** | ✅ | ✅ graph nodes | ⚠️ steps with SK functions | ⚠️ tasks in a crew | ❌ agents run solo | ✅ teams | ✅ `AgentJob` |
| **Tool calling** | ✅ via LangChain | ✅ function calling | ✅ plugins / functions | ✅ tools per agent | ✅ code-based | ✅ MCP + tools | ✅ `ToolKit` / `ToolDefinition` |
| **Structured output** (typed responses) | ⚠️ via Pydantic | ⚠️ | ✅ | ⚠️ Pydantic | ❌ | ⚠️ Pydantic | ✅ `AgentJob<T,R>` generics |
| **Multi-model support** | ✅ via LangChain | ✅ providers | ✅ connectors | ✅ LiteLLM | ✅ any LLM | ✅ multi-provider | ✅ `IStreamingAgentModel` + any OpenAI-compatible endpoint |
| **Model routing** (per-request) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ `ModelRouter` / `CapabilityModelRouter` |
| **Token-level streaming** | ✅ | ✅ | ✅ | ❌ | ✅ | ✅ SSE | ✅ `IStreamingAgentModel` |
| **Workflow-level streaming** | ⚠️ event-based | ✅ workflow streaming | ❌ | ❌ | ❌ | ⚠️ SSE events | ✅ `IAsyncEnumerable<WorkflowEvent>` |
| **Conversation memory** | ✅ via checkpointer | ✅ | ✅ chat history | ✅ built-in | ✅ | ✅ DB-backed | ✅ `IConversationMemory` |
| **Agent-to-agent handoff** | ⚠️ supervisor / swarm patterns | ✅ multi-agent | ❌ | ✅ delegation | ✅ transfer | ✅ teams | ✅ `HandoffJob` + MQTT |
| **LLM-driven routing** | ✅ | ⚠️ | ❌ | ❌ | ❌ | ⚠️ team routing | ✅ `DecideWithAgent` |

---

## Memory & Knowledge

| Capability | LangChain / LangGraph / Deep Agents | Agent Framework | Semantic Kernel | CrewAI | Smolagents | Agno | **Ananke** |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **Long-term memory** (vector store) | ⚠️ assembled: vector stores (LangChain) + file persistence (Deep Agents) — separate products | ⚠️ external | ✅ Memory connectors | ✅ built-in | ❌ | ✅ DB-backed | ✅ `IKnowledgeStore` pipeline |
| **Document ingestion pipeline** | ⚠️ loaders + splitters (separate) | ❌ | ❌ | ❌ | ❌ | ✅ knowledge base | ✅ `DocumentProcessor` (extract → chunk → embed → store) |
| **Agent-driven ingestion** (index during chat) | ⚠️ Deep Agents file-based (no embed/search) | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ `KnowledgeTools` — ingest + search in one agent toolkit |
| **Pluggable extractors** (PDF, Markdown, etc.) | ✅ document loaders | ❌ | ❌ | ❌ | ❌ | ⚠️ built-in formats | ✅ `IDocumentExtractor` interface |
| **Knowledge catalog** (document-level discovery) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ `IKnowledgeCatalog` + `CatalogAwareKnowledgeStore` |
| **Time-decay reranking** | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ Configurable half-life + floor weight |
| **Agent search tools** (auto-generated) | ❌ | ❌ | ❌ | ❌ | ❌ | ⚠️ knowledge search | ✅ `KnowledgeSearchTool` / `KnowledgeCatalogTools` |
| **Conversation memory** | ✅ via checkpointer | ✅ | ✅ chat history | ✅ built-in | ✅ | ✅ DB-backed | ✅ `IConversationMemory` |

---

## Persistence & Resilience

| Capability | LangChain / LangGraph / Deep Agents | Agent Framework | Semantic Kernel | CrewAI | Smolagents | Agno | **Ananke** |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **Checkpointing / resume** | ✅ sqlite / postgres | ✅ checkpointing | ❌ | ❌ | ❌ | ⚠️ session resume | ✅ InMemory / File |
| **Distributed locking** | ❌ single-process | ❌ | ⚠️ via Dapr | ❌ | ❌ | ❌ | ✅ RedLock native |
| **State persistence** | ✅ checkpointer | ✅ checkpointing | ⚠️ Dapr state store | ❌ | ❌ | ✅ SQLite / PostgreSQL | ✅ `IKeyValueDataAdapter` |
| **Job retry** (resilience) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ Polly integration |
| **Job timeout** | ❌ | ❌ | ❌ | ⚠️ per-task | ❌ | ❌ | ✅ Per-job `TimeSpan` |
| **Circuit breaking** (fault / reset) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ `OperationalStatus` |
| **Middleware pipeline** | ❌ | ✅ middleware | ⚠️ SK filters | ❌ | ❌ | ✅ AgentOS middleware | ✅ `IJobMiddleware<T>` |

---

## Infrastructure & Integration

| Capability | LangChain / LangGraph / Deep Agents | Agent Framework | Semantic Kernel | CrewAI | Smolagents | Agno | **Ananke** |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **MCP client** (consume external tools) | ✅ langchain-mcp-adapters | ⚠️ via extensions | ✅ via plugins | ❌ | ❌ | ✅ native MCP | ✅ `AddMcpServerToolsAsync` |
| **MCP server** (expose tools via MCP) | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ native MCP | ✅ `Ananke.MCP` |
| **OpenTelemetry tracing** | ⚠️ via callbacks / OpenLLMetry | ✅ | ✅ | ❌ | ❌ | ⚠️ own tracing | ✅ `Ananke.OpenTelemetry` |
| **Pub/sub messaging** | ❌ | ❌ | ⚠️ Dapr pub/sub | ❌ | ❌ | ❌ | ✅ MQTT native |
| **Dependency injection** | ❌ | ✅ .NET native | ✅ native | ❌ | ❌ | ❌ | ✅ native |
| **In-memory test mode** (zero-config) | ⚠️ MemorySaver | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ All infrastructure |
| **Lifecycle hooks** (on-enter / on-exit) | ❌ | ⚠️ middleware | ❌ | ⚠️ callbacks | ❌ | ✅ background hooks | ✅ Per-job hooks |
| **Distributed state machine** | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ `AbstractStateMachine` |

---

## Developer Experience

| Capability | LangChain / LangGraph / Deep Agents | Agent Framework | Semantic Kernel | CrewAI | Smolagents | Agno | **Ananke** |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **Readability** (code ≈ diagram) | ✅ graph-as-code | ✅ graph-as-code | ❌ verbose event wiring | ⚠️ role config | ✅ minimal | ✅ minimal | ✅ `Job/Then/Decide` |
| **Type safety** | ❌ string-based routing | ⚠️ C# side only | ✅ | ❌ | ❌ | ❌ | ✅ generics throughout |
| **Visual debugger** | ✅ LangGraph Studio | ✅ DevUI | ❌ | ❌ | ❌ | ✅ control plane | ⚠️ Mermaid diagrams |
| **Modular packaging** | ✅ split packages | ✅ packages | ✅ connectors | ✅ extras | ❌ single package | ⚠️ SDK + OS | ✅ 14 focused NuGet packages |
| **Learning curve** | Medium–High (3 layers) | Medium | Medium–High | Low (roles + Flows) | Low | Low | **Low–Medium** |

---

## Framework Positioning Summary

### LangChain / LangGraph / Deep Agents
**Best for:** Python (or TypeScript) teams that want the broadest ecosystem — LangChain provides low-level building blocks (model integrations, document loaders, text splitters, vector store connectors, tool abstractions), LangGraph adds stateful graph-based orchestration with checkpoint/resume, human-in-the-loop, and multi-agent patterns, and Deep Agents layers on a high-level "just works" agent with sub-agents, sandboxed code execution, and file-based long-term memory. The modular package split (`langchain-core`, `langchain-openai`, etc.) keeps dependency trees lean.
**Trade-offs:** Three conceptual layers to learn (LangChain primitives → LangGraph orchestration → Deep Agents harness); knowledge and memory capabilities are assembled from separate products rather than designed as a unified pipeline — vector stores live in LangChain, conversation state in LangGraph's checkpointer, and long-term memory in Deep Agents' `CompositeBackend` (file-based key-value persistence, not semantic search); no type safety — Python routing relies on string keys and TypedDicts; TypeScript SDK lags behind Python in feature parity; debugging complex graphs without LangGraph Studio is painful; LangSmith (observability/deployment) is a commercial product, not part of the open-source stack.

### Microsoft Agent Framework
**Best for:** Teams (Python or .NET) that want a single Microsoft-supported framework covering agents, graph-based workflows, streaming, checkpointing, and a visual DevUI — especially those migrating from AutoGen or Semantic Kernel.
**Trade-offs:** Large surface area; graph workflows are powerful but add complexity for simple pipelines; type safety only on the .NET side; DevUI is Python-only today.

### Semantic Kernel (Process Framework)
**Best for:** .NET teams already invested in the Semantic Kernel ecosystem who need composable steps and plugin-based AI integration.
**Trade-offs:** Event routing is verbose; the Process Framework surface area overlaps with Agent Framework and the boundary between them remains unclear; tight coupling to the SK plugin model; no visual debugger.

### CrewAI
**Best for:** Rapid prototyping of role-based multi-agent teams. The Flows system adds deterministic workflow control for teams that outgrow pure agent delegation.
**Trade-offs:** Flows and crew/task are separate mental models to learn; no persistence or checkpointing; no distributed coordination; Python-only.

### Smolagents
**Best for:** Lightweight, code-generating agents for simple tool-use tasks with minimal ceremony.
**Trade-offs:** No workflow or orchestration concept; single-agent focus; no persistence, state management, or distributed coordination; Python-only.

### Agno (AgentOS)
**Best for:** Python teams that need a production-ready runtime and control plane — sessions, memory, knowledge, and traces stored in your own database with JWT-based RBAC, 50+ ready-to-use API endpoints, and native MCP tool support.
**Trade-offs:** Python-only; no graph-based workflow orchestration — routing is agent-driven rather than deterministic; no distributed locking or circuit-breaking primitives; observability uses a proprietary trace format rather than OpenTelemetry.

### Ananke
**Best for:** .NET teams building production-grade AI workflows that need distributed coordination, typed state, checkpointing, resilience, and first-class human-in-the-loop — all in idiomatic C#.
**Trade-offs:** .NET-only (by design); newer ecosystem with fewer community integrations; no visual debugger equivalent to LangGraph Studio or Agent Framework DevUI (Mermaid export partially fills this gap).

---

## Unique to Ananke

Capabilities that no compared framework provides out of the box:

| Capability | Why it matters |
|---|---|
| **Distributed state machine** (`AbstractStateMachine`) | Production FSM with RedLock coordination — multiple service instances share state safely |
| **`OperationalStatus` circuit breaking** | Fault / Reset lifecycle built into the state machine — no external library needed |
| **Composable job middleware** (`IJobMiddleware<T>`) | Cross-cutting concerns (logging, auth, metrics) applied per-job without modifying job logic |
| **`ModelRouter` / `CapabilityModelRouter`** | Route LLM requests per-request: predicate-based (`ModelRouter`) or automatic cost-optimised selection via declared `ModelProfile` capabilities, intelligence tiers, and `RoutingStrategy` (`CapabilityModelRouter`) — not available in any compared framework |
| **Text DSL → workflow scaffolding** | Define topology as plain text, bind code at runtime — enables non-developers to define flow structure |
| **Typed workflow-level event streaming** | `IAsyncEnumerable<WorkflowEvent<T>>` exposes orchestration progress (not just LLM tokens) as a typed live stream — Agent Framework streams workflow output but without per-event type discrimination |
| **`HandoffJob` with MQTT** | Correlated request-response agent handoff over a real message broker — not just in-process delegation |
| **Knowledge catalog with time-decay reranking** | `IKnowledgeCatalog` + `CatalogAwareKnowledgeStore` — document-level metadata (keywords, categories, timestamps) maintained automatically on ingest; searches apply configurable time-decay so fresher documents score higher |
| **Conversational knowledge building** | `KnowledgeTools` gives an agent `process_document` + `search_knowledge` in a single toolkit — the knowledge base grows through normal conversation ("index this PDF" → indexed → searchable immediately), no separate admin workflow or batch job required. Contrast with the Lang\* ecosystem where vector stores, document loaders, checkpointers, and Deep Agents file persistence are separate products that must be assembled manually |
| **Production model decorators** | `ResilientAgentModel` (automatic 429 retry with OTel span reporting) and `CachingAgentModel` (LLM response caching via Redis) — both compose around any `IStreamingAgentModel` with no additional packages |
| **Full in-memory test mode** | Lock, state, pub/sub, checkpointing, handoff — all have zero-config in-memory implementations for testing |
