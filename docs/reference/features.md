# Ananke — Feature Index

A quick-reference of every feature in the framework. Each entry links to the
relevant documentation guide, package README, and demo (where available).

→ For a guided walkthrough, see the [Learning Path](../learning.md).

---

## Workflow Orchestration

| Feature | Description | Guide | Package | Demo |
|---|---|---|---|---|
| **Fluent graph builder** | Define workflows as code with `.Job()`, `.Then()`, `.Chain()` | [02 — Workflows](../guides/02-workflows.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | [ExtendedFlowDemo](../../src/demos/ExtendedFlowDemo/) |
| **Conditional routing** | Route between jobs with lambdas via `Workflow.Decide()` | [02 — Workflows](../guides/02-workflows.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | [ExtendedFlowDemo](../../src/demos/ExtendedFlowDemo/) |
| **LLM-driven routing** | Let the model pick the next step via `DecideWithAgent()` | [02 — Workflows](../guides/02-workflows.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |
| **Fork / Join** | Fan-out to parallel branches, fan-in with a merge function | [02 — Workflows](../guides/02-workflows.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | [ExtendedFlowDemo](../../src/demos/ExtendedFlowDemo/) |
| **Sub-workflows** | Nest a workflow inside another with `SubFlow()` | [02 — Workflows](../guides/02-workflows.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | [ExtendedFlowDemo](../../src/demos/ExtendedFlowDemo/) |
| **Workflow streaming** | Stream workflow events as `IAsyncEnumerable<WorkflowEvent>` | [02 — Workflows](../guides/02-workflows.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | [ExtendedFlowDemo](../../src/demos/ExtendedFlowDemo/) |
| **Type-safe state** | Workflow state is generic (`TState`), validated at compile time | [02 — Workflows](../guides/02-workflows.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |
| **Graph validation** | Invalid topologies fail at build time, not at runtime | [02 — Workflows](../guides/02-workflows.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |

## AI Agents

| Feature | Description | Guide | Package | Demo |
|---|---|---|---|---|
| **AgentJob** | Drop an LLM into a workflow job with tool calling and structured output | [03 — Agents](../guides/03-agents.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | [BasicAgentDemo](../../src/demos/BasicAgentDemo/) |
| **OpenAI provider** | `OpenAIChatAgentModel` — GPT-4.1, GPT-4o, o-series, and any compatible endpoint | [03 — Agents](../guides/03-agents.md) | [Ananke.Orchestration.OpenAI](../../src/Ananke.Orchestration.OpenAI/README.md) | [BasicAgentDemo](../../src/demos/BasicAgentDemo/) |
| **Anthropic provider** | `AnthropicAgentModel` — Claude Sonnet, Haiku, Opus | [03 — Agents](../guides/03-agents.md) | [Ananke.Orchestration.Anthropic](../../src/Ananke.Orchestration.Anthropic/README.md) | — |
| **Google Gemini provider** | `GoogleAgentModel` — Gemini 2.5 Pro, Flash | [03 — Agents](../guides/03-agents.md) | [Ananke.Orchestration.Google](../../src/Ananke.Orchestration.Google/README.md) | — |
| **Local / custom endpoints** | Ollama, LM Studio, vLLM, Azure OpenAI, Groq, Deepseek, Together AI | [11 — Advanced Agents](../guides/11-advanced-agents.md) | [Ananke.Orchestration.OpenAI](../../src/Ananke.Orchestration.OpenAI/README.md) | — |
| **Structured output** | Typed response deserialization via `AgentJob<TState, TResponse>` | [03 — Agents](../guides/03-agents.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |
| **Token-level streaming** | Stream individual tokens via `IStreamingAgentModel.GenerateStreamAsync` | [05 — Streaming Chat](../guides/05-streaming-chat.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | [AgenticWebDemo](../../src/demos/AgenticWebDemo/) |
| **StreamingChatWorkflow** | Pre-built workflow for chat UIs with tool calling and SSE | [05 — Streaming Chat](../guides/05-streaming-chat.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | [AgenticWebDemo](../../src/demos/AgenticWebDemo/) |
| **Model routing** | `CapabilityModelRouter` — route requests to models based on capabilities, cost, context size | [03 — Agents](../guides/03-agents.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |
| **Conversation memory** | `IConversationMemory` — persist and load chat history per conversation | [05 — Streaming Chat](../guides/05-streaming-chat.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | [AgenticWebDemo](../../src/demos/AgenticWebDemo/) |
| **A2A protocol** | Call remote A2A agents as `IAgentModel`; expose workflows as A2A endpoints | [12 — MCP & Interop](../guides/12-mcp-and-interop.md) | [Ananke.A2A](../../src/Ananke.A2A/README.md) | — |

## Tools

| Feature | Description | Guide | Package | Demo |
|---|---|---|---|---|
| **ToolKit** | Register tools with names, descriptions, and typed parameters for LLM function calling | [04 — Tools](../guides/04-tools.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | [BasicAgentDemo](../../src/demos/BasicAgentDemo/) |
| **Typed parameters** | `AddTool<T>` overloads — JSON Schema `type` inferred from `int`, `bool`, `double`, etc. | [04 — Tools](../guides/04-tools.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |
| **ToolResult.Ok / Error** | Explicit success/failure signaling with framework observability | [04 — Tools](../guides/04-tools.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |
| **Async tools** | Tool lambdas can be `async` — `Task<ToolResult>` return type | [04 — Tools](../guides/04-tools.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |
| **MCP tool import** | `AddMcpServerToolsAsync` — import tools from any MCP server into a `ToolKit` | [12 — MCP & Interop](../guides/12-mcp-and-interop.md) | [Ananke.MCP](../../src/Ananke.MCP/README.md) | [McpServerDemo](../../src/demos/McpServerDemo/) |
| **MCP tool export** | `WithAnankeTools` — expose any `ToolKit` as MCP server capabilities | [12 — MCP & Interop](../guides/12-mcp-and-interop.md) | [Ananke.MCP](../../src/Ananke.MCP/README.md) | [McpServerDemo](../../src/demos/McpServerDemo/) |

## Long-Term Memory & Knowledge

| Feature | Description | Guide | Package | Demo |
|---|---|---|---|---|
| **Document processor** | Extract → chunk → embed → store pipeline for any document format | [06 — Memory](../guides/06-memory.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | [LongTermMemoryDemo](../../src/demos/LongTermMemoryDemo/) |
| **PDF extractor** | Extract text from PDFs preserving headings, links, and structure as Markdown | [06 — Memory](../guides/06-memory.md) | [Ananke.Documents](../../src/Ananke.Documents/README.md) | [LongTermMemoryDemo](../../src/demos/LongTermMemoryDemo/) |
| **Markdown extractor** | Parse Markdown structure into normalized sections for chunking | [06 — Memory](../guides/06-memory.md) | [Ananke.Documents](../../src/Ananke.Documents/README.md) | — |
| **Sliding window chunker** | Heading-aware chunking with configurable overlap | [06 — Memory](../guides/06-memory.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |
| **Knowledge store** | `IKnowledgeStore` — vector-indexed storage with semantic search and metadata filtering | [06 — Memory](../guides/06-memory.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | [LongTermMemoryDemo](../../src/demos/LongTermMemoryDemo/) |
| **In-memory knowledge store** | Zero-config vector store for dev/test | [06 — Memory](../guides/06-memory.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | [LongTermMemoryDemo](../../src/demos/LongTermMemoryDemo/) |
| **Qdrant knowledge store** | Persistent, distributed vector store via Qdrant | [06 — Memory](../guides/06-memory.md) | [Ananke.Qdrant](../../src/Ananke.Qdrant/README.md) | — |
| **Knowledge catalog** | Document-level metadata with LLM-enriched keywords, categories, and summaries | [06 — Memory](../guides/06-memory.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |
| **Time-decay reranking** | Configurable half-life + floor weight to deprioritize older documents | [06 — Memory](../guides/06-memory.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |
| **Agent-driven ingestion** | `KnowledgeTools` — agents can index documents and search them in the same chat | [06 — Memory](../guides/06-memory.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | [LongTermMemoryDemo](../../src/demos/LongTermMemoryDemo/) |
| **Knowledge search tool** | `KnowledgeSearchTool` — auto-generated search tool for agent integration | [06 — Memory](../guides/06-memory.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | [LongTermMemoryDemo](../../src/demos/LongTermMemoryDemo/) |
| **Catalog discovery tools** | `KnowledgeCatalogTools` — agent tools for browsing sources in the catalog | [06 — Memory](../guides/06-memory.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |
| **Embedding model** | `IEmbeddingModel` abstraction with OpenAI implementation (text-embedding-3-*) | [06 — Memory](../guides/06-memory.md) | [Ananke.Orchestration.OpenAI](../../src/Ananke.Orchestration.OpenAI/README.md) | — |

## Human-in-the-Loop

| Feature | Description | Guide | Package | Demo |
|---|---|---|---|---|
| **Interrupt before / after** | Pause workflow execution at any job for human review | [07 — Human-in-the-Loop](../guides/07-human-in-the-loop.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | [AgenticWebDemo](../../src/demos/AgenticWebDemo/) |
| **Resume with state** | Resume a paused workflow with optionally modified state | [07 — Human-in-the-Loop](../guides/07-human-in-the-loop.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | [AgenticWebDemo](../../src/demos/AgenticWebDemo/) |
| **Checkpointing** | `ICheckpointStore` — persist full workflow state for resume across restarts | [07 — Human-in-the-Loop](../guides/07-human-in-the-loop.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |

## State Machine

| Feature | Description | Guide | Package | Demo |
|---|---|---|---|---|
| **AbstractStateMachine** | Distributed FSM with typed states, transitions, and events | [08 — State Machine](../guides/08-state-machine.md) | [Ananke.StateMachine](../../src/Ananke.StateMachine/README.md) | [StateMachineDemo](../../src/demos/StateMachineDemo/) |
| **Distributed locking** | Safe coordination across instances via `IDistributedLock` | [08 — State Machine](../guides/08-state-machine.md) | [Ananke.StateMachine](../../src/Ananke.StateMachine/README.md) | [DistributedServicesDemo](../../src/demos/DistributedServicesDemo/) |
| **Middleware pipeline** | `IJobMiddleware<T>` — intercept every transition for logging, metrics, validation | [08 — State Machine](../guides/08-state-machine.md) | [Ananke.StateMachine](../../src/Ananke.StateMachine/README.md) | — |
| **Guard conditions** | Block transitions based on runtime state | [08 — State Machine](../guides/08-state-machine.md) | [Ananke.StateMachine](../../src/Ananke.StateMachine/README.md) | — |
| **Circuit breaking** | `OperationalStatus.Faulted` blocks all transitions until `ResetAsync` | [08 — State Machine](../guides/08-state-machine.md) | [Ananke.StateMachine](../../src/Ananke.StateMachine/README.md) | — |
| **Lifecycle hooks** | `OnEnter` / `OnExit` per state | [08 — State Machine](../guides/08-state-machine.md) | [Ananke.StateMachine](../../src/Ananke.StateMachine/README.md) | — |

## Production Resilience

| Feature | Description | Guide | Package | Demo |
|---|---|---|---|---|
| **ResilientAgentModel** | Automatic 429 retry with exponential backoff, jitter, and OTel event reporting | [11 — Advanced Agents](../guides/11-advanced-agents.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |
| **CachingAgentModel** | LLM response caching via any `IKeyValueDataAdapter` (e.g. Redis) | [11 — Advanced Agents](../guides/11-advanced-agents.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |
| **Decorator composition** | Stack resilience + caching (or any `IStreamingAgentModel` decorator) | [11 — Advanced Agents](../guides/11-advanced-agents.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |
| **Polly integration** | Pass custom `ResiliencePipeline` for circuit breaker, timeout, etc. | [11 — Advanced Agents](../guides/11-advanced-agents.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |
| **Job retry** | Polly-based retry built into the workflow runner | [02 — Workflows](../guides/02-workflows.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |
| **Job timeout** | Per-job `TimeSpan` timeout | [02 — Workflows](../guides/02-workflows.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |

## Infrastructure & Integration

| Feature | Description | Guide | Package | Demo |
|---|---|---|---|---|
| **Redis distributed lock** | `RedisDistributedLock` via RedLock.net | [09 — Distributed](../guides/09-distributed.md) | [Ananke.Redis](../../src/Ananke.Redis/README.md) | [DistributedServicesDemo](../../src/demos/DistributedServicesDemo/) |
| **Redis key-value store** | `RedisDataAdapter` — `IKeyValueDataAdapter` for caching, state, etc. | [09 — Distributed](../guides/09-distributed.md) | [Ananke.Redis](../../src/Ananke.Redis/README.md) | — |
| **MQTT pub/sub** | `IChannelReader` / `IChannelWriter` via MQTTnet with MessagePack serialization | [09 — Distributed](../guides/09-distributed.md) | [Ananke.MQTT](../../src/Ananke.MQTT/README.md) | [DistributedServicesDemo](../../src/demos/DistributedServicesDemo/) |
| **Agent handoff** | `HandoffJob` — request/response handoff between agents across processes | [09 — Distributed](../guides/09-distributed.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | [DistributedServicesDemo](../../src/demos/DistributedServicesDemo/) |
| **MCP server** | Expose `ToolKit` and `Workflow` as MCP server capabilities (stdio + HTTP) | [12 — MCP & Interop](../guides/12-mcp-and-interop.md) | [Ananke.MCP](../../src/Ananke.MCP/README.md) | [McpServerDemo](../../src/demos/McpServerDemo/) |
| **MCP client** | Import tools from external MCP servers into `ToolKit` | [12 — MCP & Interop](../guides/12-mcp-and-interop.md) | [Ananke.MCP](../../src/Ananke.MCP/README.md) | [McpServerDemo](../../src/demos/McpServerDemo/) |
| **A2A client** | Call remote A2A agents as `IAgentModel` | [12 — MCP & Interop](../guides/12-mcp-and-interop.md) | [Ananke.A2A](../../src/Ananke.A2A/README.md) | — |
| **A2A server** | Expose Ananke workflows as A2A-compliant endpoints | [12 — MCP & Interop](../guides/12-mcp-and-interop.md) | [Ananke.A2A](../../src/Ananke.A2A/README.md) | — |

## Observability

| Feature | Description | Guide | Package | Demo |
|---|---|---|---|---|
| **OpenTelemetry tracing** | Distributed tracing with OTLP export (BetterStack, Jaeger, Grafana Tempo) | [10 — Observability](../guides/10-observability.md) | [Ananke.OpenTelemetry](../../src/Ananke.OpenTelemetry/README.md) | — |
| **Workflow spans** | `IWorkflowTracer` — automatic spans for job start/end and state transitions | [10 — Observability](../guides/10-observability.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |
| **State machine spans** | Built-in `ActivitySource` for transition spans | [10 — Observability](../guides/10-observability.md) | [Ananke.StateMachine](../../src/Ananke.StateMachine/README.md) | — |
| **Retry event reporting** | `llm.rate_limit_retry` events with attempt count and delay on the active span | [11 — Advanced Agents](../guides/11-advanced-agents.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |
| **Tool span attributes** | `output_length` and `tool.error` on tool execution spans | [04 — Tools](../guides/04-tools.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |

## Design Tooling

| Feature | Description | Guide | Package | Demo |
|---|---|---|---|---|
| **Text DSL** | Define workflow topology in plain text, parse with `WorkflowScaffold.Parse` | [13 — Design Tooling](../guides/13-design-tooling.md) | [Ananke.Design](../../src/Ananke.Design/README.md) | [DesignPipelineDemo](../../src/demos/DesignPipelineDemo/) |
| **YAML manifests** | Declare models, agent jobs, and connections in `.ananke.yml` files | [13 — Design Tooling](../guides/13-design-tooling.md) | [Ananke.Design](../../src/Ananke.Design/README.md) | [DesignPipelineDemo](../../src/demos/DesignPipelineDemo/) |
| **ModelResolver** | Resolve model instances from YAML manifest + configuration | [13 — Design Tooling](../guides/13-design-tooling.md) | [Ananke.Design](../../src/Ananke.Design/README.md) | — |
| **Runtime binding** | `Bind()` job implementations to a scaffold at runtime | [13 — Design Tooling](../guides/13-design-tooling.md) | [Ananke.Design](../../src/Ananke.Design/README.md) | [DesignPipelineDemo](../../src/demos/DesignPipelineDemo/) |
| **Mermaid export** | `workflow.ToMermaid()` — generate diagrams from any validated workflow | [13 — Design Tooling](../guides/13-design-tooling.md) | [Ananke.Design](../../src/Ananke.Design/README.md) | [DesignPipelineDemo](../../src/demos/DesignPipelineDemo/) |

## Testing

| Feature | Description | Guide | Package | Demo |
|---|---|---|---|---|
| **In-memory distributed lock** | `InMemoryDistributedLock` — zero-config replacement for Redis in tests | [14 — Testing](../guides/14-testing.md) | [Ananke.Abstractions](../../src/Ananke.Abstractions/README.md) | — |
| **In-memory knowledge store** | `InMemoryKnowledgeStore` — vector store that runs without external services | [14 — Testing](../guides/14-testing.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |
| **In-memory handoff channel** | `InMemoryHandoffChannel` — test agent handoff without MQTT | [14 — Testing](../guides/14-testing.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |
| **In-memory checkpoint store** | `InMemoryCheckpointStore` — test checkpointing without a filesystem | [14 — Testing](../guides/14-testing.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |
| **File checkpoint store** | `FileCheckpointStore` — persist checkpoints to disk for local dev | [14 — Testing](../guides/14-testing.md) | [Ananke.Orchestration](../../src/Ananke.Orchestration/README.md) | — |

---

← [Documentation Hub](../learning.md) · [Back to README](../../README.md)
