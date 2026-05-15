<!-- topic: features, tags: feature-index, reference, capabilities -->
# Ananke — Feature Index

A quick-reference of every feature in the framework. Each entry links to the
relevant documentation guide and demo (where available).

→ For a guided walkthrough, see the [Learning Path](../learning-path.md).

---

## Workflow Orchestration

| Feature | Description | Guide | Demo |
|---|---|---|---|
| **Fluent graph builder** | Define workflows as code with `.Job()`, `.Then()`, `.Chain()` | [02 — Workflows](../guides/02-workflows.md) | — |
| **Conditional routing** | Route between jobs with lambdas via `Workflow.Decide()` | [02 — Workflows](../guides/02-workflows.md) | — |
| **LLM-driven routing** | Let the model pick the next step via `DecideWithAgent()` | [02 — Workflows](../guides/02-workflows.md) | — |
| **Fork / Join** | Fan-out to parallel branches, fan-in with a merge function | [02 — Workflows](../guides/02-workflows.md) | — |
| **Sub-workflows** | Nest a workflow inside another with `SubFlow()` | [02 — Workflows](../guides/02-workflows.md) | — |
| **Workflow streaming** | Stream workflow events as `IAsyncEnumerable<WorkflowEvent>` | [02 — Workflows](../guides/02-workflows.md) | — |
| **Type-safe state** | Workflow state is generic (`TState`), validated at compile time | [02 — Workflows](../guides/02-workflows.md) | — |
| **Graph validation** | Invalid topologies fail at build time, not at runtime | [02 — Workflows](../guides/02-workflows.md) | — |

## AI Agents

| Feature | Description | Guide | Demo |
|---|---|---|---|
| **AgentJob** | Drop an LLM into a workflow job with tool calling and structured output | [03 — Agents](../guides/03-agents.md) | [BasicAgentDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/01-foundations/BasicAgentDemo) |
| **OpenAI provider** | `OpenAIChatAgentModel` — GPT-4.1, GPT-4o, o-series, and any compatible endpoint | [03 — Agents](../guides/03-agents.md) | [BasicAgentDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/01-foundations/BasicAgentDemo) |
| **Anthropic provider** | `AnthropicAgentModel` — Claude Sonnet, Haiku, Opus | [03 — Agents](../guides/03-agents.md) | — |
| **Google Gemini provider** | `GoogleAgentModel` — Gemini 2.5 Pro, Flash | [03 — Agents](../guides/03-agents.md) | — |
| **Local / custom endpoints** | Ollama, LM Studio, vLLM, Azure OpenAI, Groq, Deepseek, Together AI | [11 — Advanced Agents](../guides/11-advanced-agents.md) | — |
| **Structured output** | Typed response deserialization via `AgentJob<TState, TResponse>` | [03 — Agents](../guides/03-agents.md) | — |
| **Token-level streaming** | Stream individual tokens via `IStreamingAgentModel.GenerateStreamAsync` | [05 — Streaming Chat](../guides/05-streaming-chat.md) | [AgenticWebDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/05-applications/AgenticWebDemo) |
| **StreamingChatWorkflow** | Pre-built workflow for chat UIs with tool calling and SSE | [05 — Streaming Chat](../guides/05-streaming-chat.md) | [AgenticWebDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/05-applications/AgenticWebDemo) |
| **Model routing** | `CapabilityModelRouter` — route requests to models based on capabilities, cost, context size | [03 — Agents](../guides/03-agents.md) | — |
| **Conversation memory** | `IConversationMemory` — persist and load chat history per conversation | [05 — Streaming Chat](../guides/05-streaming-chat.md) | [AgenticWebDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/05-applications/AgenticWebDemo) |
| **A2A protocol** | Call remote A2A agents as `IAgentModel`; expose workflows as A2A endpoints | [12 — MCP & Interop](../guides/12-mcp-and-interop.md) | — |

## Tools

| Feature | Description | Guide | Demo |
|---|---|---|---|
| **ToolKit** | Register tools with names, descriptions, and typed parameters for LLM function calling | [04 — Tools](../guides/04-tools.md) | [BasicAgentDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/01-foundations/BasicAgentDemo) |
| **Typed parameters** | `AddTool<T>` overloads — JSON Schema `type` inferred from `int`, `bool`, `double`, etc. | [04 — Tools](../guides/04-tools.md) | — |
| **ToolResult.Ok / Error** | Explicit success/failure signaling with framework observability | [04 — Tools](../guides/04-tools.md) | — |
| **Async tools** | Tool lambdas can be `async` — `Task<ToolResult>` return type | [04 — Tools](../guides/04-tools.md) | — |
| **MCP tool import** | `AddMcpServerToolsAsync` — import tools from any MCP server into a `ToolKit` | [12 — MCP & Interop](../guides/12-mcp-and-interop.md) | [McpServerDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/06-interop-and-channels/McpServerDemo) |
| **MCP tool export** | `WithAnankeTools` — expose any `ToolKit` as MCP server capabilities | [12 — MCP & Interop](../guides/12-mcp-and-interop.md) | [McpServerDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/06-interop-and-channels/McpServerDemo) |

## Long-Term Memory & Knowledge

| Feature | Description | Guide | Demo |
|---|---|---|---|
| **Document processor** | Extract → chunk → embed → store pipeline for any document format | [06 — Memory](../guides/06-memory.md) | [LongTermMemoryDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/03-memory-and-knowledge/LongTermMemoryDemo) |
| **PDF extractor** | Extract text from PDFs preserving headings, links, and structure as Markdown | [06 — Memory](../guides/06-memory.md) | [LongTermMemoryDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/03-memory-and-knowledge/LongTermMemoryDemo) |
| **Markdown extractor** | Parse Markdown structure into normalized sections for chunking | [06 — Memory](../guides/06-memory.md) | — |
| **Sliding window chunker** | Heading-aware chunking with configurable overlap | [06 — Memory](../guides/06-memory.md) | — |
| **Knowledge store** | `IKnowledgeStore` — vector-indexed storage with semantic search and metadata filtering | [06 — Memory](../guides/06-memory.md) | [LongTermMemoryDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/03-memory-and-knowledge/LongTermMemoryDemo) |
| **In-memory knowledge store** | Zero-config vector store for dev/test | [06 — Memory](../guides/06-memory.md) | [LongTermMemoryDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/03-memory-and-knowledge/LongTermMemoryDemo) |
| **Qdrant knowledge store** | Persistent, distributed vector store via Qdrant | [06 — Memory](../guides/06-memory.md) | — |
| **Knowledge catalog** | Document-level metadata with LLM-enriched keywords, categories, and summaries | [06 — Memory](../guides/06-memory.md) | — |
| **Time-decay reranking** | Configurable half-life + floor weight to deprioritize older documents | [06 — Memory](../guides/06-memory.md) | — |
| **Agent-driven ingestion** | `KnowledgeTools` — agents can index documents and search them in the same chat | [06 — Memory](../guides/06-memory.md) | [LongTermMemoryDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/03-memory-and-knowledge/LongTermMemoryDemo) |
| **Knowledge search tool** | `KnowledgeSearchTool` — auto-generated search tool for agent integration | [06 — Memory](../guides/06-memory.md) | [LongTermMemoryDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/03-memory-and-knowledge/LongTermMemoryDemo) |
| **Catalog discovery tools** | `KnowledgeCatalogTools` — agent tools for browsing sources in the catalog | [06 — Memory](../guides/06-memory.md) | — |
| **Embedding model** | `IEmbeddingModel` abstraction with OpenAI implementation (text-embedding-3-*) | [06 — Memory](../guides/06-memory.md) | — |

## Human-in-the-Loop

| Feature | Description | Guide | Demo |
|---|---|---|---|
| **Interrupt before / after** | Pause workflow execution at any job for human review | [07 — Human-in-the-Loop](../guides/07-human-in-the-loop.md) | [AgenticWebDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/05-applications/AgenticWebDemo) |
| **Resume with state** | Resume a paused workflow with optionally modified state | [07 — Human-in-the-Loop](../guides/07-human-in-the-loop.md) | [AgenticWebDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/05-applications/AgenticWebDemo) |
| **Checkpointing** | `ICheckpointStore` — persist full workflow state for resume across restarts | [07 — Human-in-the-Loop](../guides/07-human-in-the-loop.md) | — |

## State Machine

| Feature | Description | Guide | Demo |
|---|---|---|---|
| **AbstractStateMachine** | Distributed FSM with typed states, transitions, and events | [08 — State Machine](../guides/08-state-machine.md) | [StateMachineDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/01-foundations/StateMachineDemo) |
| **Distributed locking** | Safe coordination across instances via `IDistributedLock` | [08 — State Machine](../guides/08-state-machine.md) | — |
| **Middleware pipeline** | `IJobMiddleware<T>` — intercept every transition for logging, metrics, validation | [08 — State Machine](../guides/08-state-machine.md) | — |
| **Guard conditions** | Block transitions based on runtime state | [08 — State Machine](../guides/08-state-machine.md) | — |
| **Circuit breaking** | `OperationalStatus.Faulted` blocks all transitions until `ResetAsync` | [08 — State Machine](../guides/08-state-machine.md) | — |
| **Lifecycle hooks** | `OnEnter` / `OnExit` per state | [08 — State Machine](../guides/08-state-machine.md) | — |

## Production Resilience

| Feature | Description | Guide | Demo |
|---|---|---|---|
| **ResilientAgentModel** | Automatic 429 retry with exponential backoff, jitter, and OTel event reporting | [11 — Advanced Agents](../guides/11-advanced-agents.md) | — |
| **CachingAgentModel** | LLM response caching via any `IKeyValueDataAdapter` (e.g. Redis) | [11 — Advanced Agents](../guides/11-advanced-agents.md) | — |
| **Decorator composition** | Stack resilience + caching (or any `IStreamingAgentModel` decorator) | [11 — Advanced Agents](../guides/11-advanced-agents.md) | — |
| **Polly integration** | Pass custom `ResiliencePipeline` for circuit breaker, timeout, etc. | [11 — Advanced Agents](../guides/11-advanced-agents.md) | — |
| **Job retry** | Polly-based retry built into the workflow runner | [02 — Workflows](../guides/02-workflows.md) | — |
| **Job timeout** | Per-job `TimeSpan` timeout | [02 — Workflows](../guides/02-workflows.md) | — |

## Infrastructure & Integration

| Feature | Description | Guide | Demo |
|---|---|---|---|
| **Redis distributed lock** | `RedisDistributedLock` via RedLock.net | [09 — Distributed](../guides/09-distributed.md) | — |
| **Redis key-value store** | `RedisDataAdapter` — `IKeyValueDataAdapter` for caching, state, etc. | [09 — Distributed](../guides/09-distributed.md) | — |
| **MQTT pub/sub** | `IChannelReader` / `IChannelWriter` via MQTTnet with MessagePack serialization | [09 — Distributed](../guides/09-distributed.md) | — |
| **Agent handoff** | `HandoffJob` — request/response handoff between agents across processes | [09 — Distributed](../guides/09-distributed.md) | — |
| **MCP server** | Expose `ToolKit` and `Workflow` as MCP server capabilities (stdio + HTTP) | [12 — MCP & Interop](../guides/12-mcp-and-interop.md) | [McpServerDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/06-interop-and-channels/McpServerDemo) |
| **MCP client** | Import tools from external MCP servers into `ToolKit` | [12 — MCP & Interop](../guides/12-mcp-and-interop.md) | [McpServerDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/06-interop-and-channels/McpServerDemo) |
| **A2A client** | Call remote A2A agents as `IAgentModel` | [12 — MCP & Interop](../guides/12-mcp-and-interop.md) | — |
| **A2A server** | Expose Ananke workflows as A2A-compliant endpoints | [12 — MCP & Interop](../guides/12-mcp-and-interop.md) | — |

## Observability

| Feature | Description | Guide | Demo |
|---|---|---|---|
| **OpenTelemetry tracing** | Distributed tracing with OTLP export (BetterStack, Jaeger, Grafana Tempo) | [10 — Observability](../guides/10-observability.md) | — |
| **Workflow spans** | `IWorkflowTracer` — automatic spans for job start/end and state transitions | [10 — Observability](../guides/10-observability.md) | — |
| **State machine spans** | Built-in `ActivitySource` for transition spans | [10 — Observability](../guides/10-observability.md) | — |
| **Retry event reporting** | `llm.rate_limit_retry` events with attempt count and delay on the active span | [11 — Advanced Agents](../guides/11-advanced-agents.md) | — |
| **Tool span attributes** | `output_length` and `tool.error` on tool execution spans | [04 — Tools](../guides/04-tools.md) | — |

## Design Tooling

| Feature | Description | Guide | Demo |
|---|---|---|---|
| **Text DSL** | Define workflow topology in plain text, parse with `WorkflowScaffold.Parse` | [13 — Design Tooling](../guides/13-design-tooling.md) | [DesignPipelineDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/02-workflow-patterns/DesignPipelineDemo) |
| **YAML manifests** | Declare models, agent jobs, and connections in `.ananke.yml` files | [13 — Design Tooling](../guides/13-design-tooling.md) | [DesignPipelineDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/02-workflow-patterns/DesignPipelineDemo) |
| **ModelResolver** | Resolve model instances from YAML manifest + configuration | [13 — Design Tooling](../guides/13-design-tooling.md) | — |
| **Runtime binding** | `Bind()` job implementations to a scaffold at runtime | [13 — Design Tooling](../guides/13-design-tooling.md) | [DesignPipelineDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/02-workflow-patterns/DesignPipelineDemo) |
| **Mermaid export** | `workflow.ToMermaid()` — generate diagrams from any validated workflow | [13 — Design Tooling](../guides/13-design-tooling.md) | [DesignPipelineDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/02-workflow-patterns/DesignPipelineDemo) |

## Testing

| Feature | Description | Guide | Demo |
|---|---|---|---|
| **In-memory distributed lock** | `InMemoryDistributedLock` — zero-config replacement for Redis in tests | [14 — Testing](../guides/14-testing.md) | — |
| **In-memory knowledge store** | `InMemoryKnowledgeStore` — vector store that runs without external services | [14 — Testing](../guides/14-testing.md) | — |
| **In-memory handoff channel** | `InMemoryHandoffChannel` — test agent handoff without MQTT | [14 — Testing](../guides/14-testing.md) | — |
| **In-memory checkpoint store** | `InMemoryCheckpointStore` — test checkpointing without a filesystem | [14 — Testing](../guides/14-testing.md) | — |

## Empirical Memory & Agent Learning

| Feature | Description | Guide | Demo |
|---|---|---|---|
| **IEmpiricalMemory** | Three-kind memory store: Pattern, Skill, Heuristic — each with a confidence score | [15 — Empirical Memory](../guides/15-empirical-memory.md) | [Connect4Demo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/04-organics-and-emergence/Connect4Demo) |
| **EmpiricalMemoryTools** | Agent tools: `recall_empirical`, `commit_insight`, `reinforce_empirical`, `contradict_empirical` | [15 — Empirical Memory](../guides/15-empirical-memory.md) | [LearningPrimitivesDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/04-organics-and-emergence/LearningPrimitivesDemo) |
| **Confidence tracking** | Scores increase on reinforcement and decrease on contradiction — without deleting entries | [15 — Empirical Memory](../guides/15-empirical-memory.md) | — |
| **InMemoryEmpiricalMemory** | Zero-config in-memory implementation for dev/test | [15 — Empirical Memory](../guides/15-empirical-memory.md) | [LearningPrimitivesDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/04-organics-and-emergence/LearningPrimitivesDemo) |
| **QdrantEmpiricalMemory** | Persistent, distributed empirical memory via Qdrant | [15 — Empirical Memory](../guides/15-empirical-memory.md) | — |
| **IOfflineLearner** | Background learning cycles: decay, curiosity exploration, consolidation | [15 — Empirical Memory](../guides/15-empirical-memory.md) | [LearningPrimitivesDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/04-organics-and-emergence/LearningPrimitivesDemo) |
| **IConsolidationSummarizer** | Promote confident entries into `IKnowledgeStore` as permanent knowledge | [15 — Empirical Memory](../guides/15-empirical-memory.md) | — |
| **Skill packages** | Export validated skills as portable packages; import across deployments | [15 — Empirical Memory](../guides/15-empirical-memory.md) | — |

## Agentic Patterns

| Feature | Description | Guide | Demo |
|---|---|---|---|
| **AgenticPattern builder** | Pre-wired workflow builders for recognized orchestration patterns; validates at `Build()` | [16 — Agentic Patterns](../guides/16-agentic-patterns.md) | [AgenticDesignPatternsDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/02-workflow-patterns/AgenticDesignPatternsDemo) |
| **Review & Critique** | Generator → critic loop until approval or max iterations | [16 — Agentic Patterns](../guides/16-agentic-patterns.md) | [AgenticDesignPatternsDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/02-workflow-patterns/AgenticDesignPatternsDemo) |
| **Iterative Refinement** | Single-agent refinement loop until quality threshold | [16 — Agentic Patterns](../guides/16-agentic-patterns.md) | [AgenticDesignPatternsDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/02-workflow-patterns/AgenticDesignPatternsDemo) |

## Organics

> These features are working and demonstrable. Multi-generation lineage and closed-loop learning (division outcomes driving the next policy decision end-to-end) are structurally wired but not yet exercised end-to-end. See the [Roadmap](../about/roadmap.md).

| Feature | Description | Guide | Demo |
|---|---|---|---|
| **Cell division** | A generalist workflow detects structural tension, proposes a split, spawns two specialised peers, kills the parent, and records the outcome into empirical memory | — | [OrganicKernelDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/04-organics-and-emergence/OrganicKernelDemo) |
| **Complexity monitors** | Detect generalist ceiling via tool count and routing entropy signals | — | [OrganicKernelDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/04-organics-and-emergence/OrganicKernelDemo) |
| **Division proposal & approval** | Human-gated or automatic approval of proposed splits before execution | — | [OrganicKernelDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/04-organics-and-emergence/OrganicKernelDemo) |
| **Organic mesh inspection** | `nnke mesh` — inspect local Organics mesh snapshots from the CLI | [CLI Overview](../cli/overview.md) | — |

## Federation

| Feature | Description | Guide | Demo |
|---|---|---|---|
| **Cross-cloud federation** | Deploy the same manifest to Azure, Google, or Anthropic; unified telemetry across the cluster | [20 — Platform Recommendation](../guides/20-platform-recommendation.md) | — |
| **Human approval gates** | Cross-cloud workflows with interrupt-and-resume approval gates | [07 — Human-in-the-Loop](../guides/07-human-in-the-loop.md) | — |
| **nnke-platform CLI** | Deploy, observe, compare platforms, and manage federation mesh operations | [CLI Overview](../cli/overview.md) | — |
| **Smart Tool Router** | Route tool calls to the best available provider based on capability, cost, and latency | [20 — Platform Recommendation](../guides/20-platform-recommendation.md) | — |

---

← [Demos](../demos.md) · [Learning Path](../learning-path.md)
