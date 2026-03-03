<p align="center">
  <img src="docs/ananke-creation.png" alt="Ananke — Stability Before Creation" width="680" />
</p>

<p align="center">
  <em>"Even the gods bowed to Ananke, for she alone could not be moved."</em><br/>
  — Adapted from Aeschylus & Plato
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/Ananke"><img src="https://img.shields.io/nuget/v/Ananke.svg?label=Ananke&color=5B4FCF" alt="NuGet" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="License" /></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10" />
</p>

---

**Ananke** is a vendor-agnostic workflow orchestration framework for .NET.
It gives your AI agents and automated pipelines a production-grade backbone:
typed state, distributed coordination, checkpointing, resilience, long-term memory
and first-class human-in-the-loop support — from a single streaming chat agent
to distributed, state-machine-coordinated multi-service pipelines.

---

## Why Ananke?

The AI agent ecosystem is predominantly Python.
For .NET teams shipping to production, that means either adopting a foreign ecosystem
or assembling production infrastructure from scratch.
Even within mature frameworks, capabilities that .NET developers take for granted —
strong typing, real parallelism, dependency injection, structured concurrency —
often require additional libraries, schema definitions, or workarounds.

The landscape is also moving fast.
Frameworks rebrand, merge, or shift direction between releases,
making it risky to couple production systems to a single vendor's roadmap.

Ananke starts from a different question:

> *What does the infrastructure need to look like so that building any agentic system — at any scale — is straightforward for .NET developers?*

The answer is a typed, testable, composable foundation where the infrastructure comes first and LLM providers are pluggable:

### 🔀 Workflow orchestration
Fluent graph-as-code builder · conditional & LLM-driven routing · fork/join parallelism · nested sub-workflows · human-in-the-loop interrupts · typed `IAsyncEnumerable` event streaming

### 🤖 AI agents
`AgentJob` with tool calling + structured output · token-level streaming · multi-provider (OpenAI, Anthropic, Google Gemini + any OpenAI-compatible endpoint) · capability-based model routing · production decorators (429 retry with OTel, LLM response caching)

### 🧠 Long-term memory
Agents build knowledge through conversation — "index this PDF" → indexed → searchable immediately · batch import via `DocumentProcessor` (extract → chunk → embed → store) · knowledge catalog with LLM-enriched metadata + time-decay reranking · in-memory and Qdrant backends

### 🏭 Distributed state machine
Production FSM with RedLock coordination · composable middleware pipeline · guard conditions · circuit breaking (fault/reset)

### 🗄️ Infrastructure
Checkpointing (InMemory / File) · distributed locking · MQTT pub/sub · OpenTelemetry tracing · MCP server integration

### 🧑‍💻 Developer experience
Idiomatic C# (async/await, DI, generics) · design-time DSL with Mermaid export · full in-memory test mode for every infrastructure contract · 14 focused NuGet packages

→ **[How does Ananke compare to LangGraph, Agent Framework, CrewAI, and others?](docs/framework-comparison.md)**

---

## Features

### 🔀 Workflow Orchestration

A fluent, type-safe builder that reads like English.

```csharp
var workflow = new Workflow<ResearchState>("research-pipeline")
    .Job("plan",       planJob)
    .Job("search_web", searchWebJob)
    .Job("search_db",  searchDbJob)
    .Job("synthesize", synthesizeJob)
    .Then("plan", Workflow.Fork("search_web", "search_db"))
    .Join(["search_web", "search_db"], "synthesize", Merge)
    .Then("synthesize", Workflow.End);

var result = await workflow.RunAsync(new ResearchState { Query = "distributed systems" });
```

**Routing primitives**

| Primitive | What it does |
|---|---|
| `.Then("a", "b")` | Direct edge: job `a` routes to `b` |
| `.Then("a", Workflow.Decide<S>(s => ...))` | Conditional routing via lambda |
| `.Then("a", Workflow.DecideWithAgent<S>(model).Build())` | LLM-driven routing |
| `.Then("a", Workflow.Fork("b", "c"))` | Fan-out to parallel branches |
| `.Join(["b", "c"], "d", merge)` | Fan-in with explicit merge function |
| `.SubFlow("name", inner, mapIn, mapOut)` | Nest a workflow inside another |

---

### 🤖 AI Agent Integration

Drop any LLM into a workflow job with tool calling, structured output, and token-level streaming.

```csharp
var agentJob = AgentJobFactory
    .Create<MyState, MyResponse>("analyze", model)
    .WithSystemPrompt("You are a research analyst.")
    .WithTools(searchTools)
    .WithPrompt(state => $"Analyze: {state.Query}")
    .MapResult((state, response) => state with { Analysis = response.Text })
    .Build();
```

Providers: **OpenAI** (`Ananke.Orchestration.OpenAI`), **Anthropic / Claude** (`Ananke.Orchestration.Anthropic`), and **Google Gemini** (`Ananke.Orchestration.Google`). Any OpenAI-compatible endpoint works too — Ollama, LM Studio, vLLM, Azure OpenAI, Groq, and others — via the `endpoint` parameter. Bring your own provider by implementing `IStreamingAgentModel`. → [Advanced Agent Features](docs/advanced-agent-features.md)

**Production decorators** — wrap any model with `ResilientAgentModel` for automatic 429 retry with OTel reporting, and `CachingAgentModel` for LLM response caching via any `IKeyValueDataAdapter` (e.g. Redis). Both compose and require no additional packages. → [Advanced Agent Features](docs/advanced-agent-features.md)

---

### 🧠 Long-Term Memory

Knowledge in Ananke works two ways: **agents build it through conversation**, or you **import it in bulk** from code.

**Conversational** — give an agent `KnowledgeTools` and it can index documents and search them in the same chat session. A user says *"index this PDF"*, the agent processes it, and it's immediately searchable — no admin panel, no batch job, no separate workflow.

```csharp
// One toolkit with both process_document and search_knowledge tools
// Pass describeModel to auto-generate LLM summaries on ingest
var tools = KnowledgeTools.Create(processor, knowledgeStore,
    searchDescription: "Search indexed engineering reference materials.",
    describeModel: model);

await StreamingChatWorkflow.Create("chat", model)
    .WithSystemPrompt("You can index documents and search them for the user.")
    .WithTools(tools)
    .OnTextDelta(async delta => Console.Write(delta))
    .RunAsync([AgentMessage.User("Index https://example.com/design-patterns.pdf and tell me about the factory pattern")]);
```

**Programmatic** — the same `DocumentProcessor` works from admin endpoints, batch scripts, or background jobs.

```csharp
var embeddingModel = OpenAIEmbeddingModel.Create(apiKey);
var knowledgeStore = new InMemoryKnowledgeStore(embeddingModel);
var processor = new DocumentProcessor(
    new HttpClient(), [new PdfExtractor(), new MarkdownExtractor()], new SlidingWindowChunker(), knowledgeStore);

await using var pdf = File.OpenRead("onboarding-policy.pdf");
var result = await processor.ProcessAsync(pdf, "application/pdf", "onboarding-policy");
// → "12 sections, 34 chunks stored"

// Give the agent a search-only tool — it decides when to use it
var tools = KnowledgeSearchTool.Create("knowledge", knowledgeStore);

await StreamingChatWorkflow.Create("chat", model)
    .WithSystemPrompt("Use search_knowledge to find information from indexed documents.")
    .WithTools(tools)
    .OnTextDelta(async delta => Console.Write(delta))
    .RunAsync([AgentMessage.User("What's the onboarding process for new engineers?")]);
```

**The pipeline is composable:** same extract → chunk → embed → store path whether triggered by an agent tool call, an admin endpoint, or a batch script.

| Component | What it does |
|---|---|
| `IDocumentExtractor` | Pluggable per-format extraction (PDF, HTML, etc.) → Markdown |
| `IDocumentChunker` | Splits documents at heading boundaries with configurable overlap |
| `IEmbeddingModel` | Abstraction over any embedding provider (OpenAI, Google, local) |
| `IKnowledgeStore` | Vector-indexed storage with semantic search and metadata filtering |
| `DocumentProcessor` | Orchestrates the full pipeline: fetch/stream → extract → chunk → store |
| `KnowledgeSearchTool` / `KnowledgeTools` | Ready-made `ToolKit` factories for agent integration |
| `IKnowledgeCatalog` | Document-level catalog with LLM-enriched metadata for cross-document discovery |
| `CatalogAwareKnowledgeStore` | `IKnowledgeStore` decorator that auto-maintains the catalog + time-decay reranking |
| `KnowledgeCatalogTools` | Agent tools for browsing and discovering sources in the catalog |

Built-in implementations: `InMemoryKnowledgeStore` (dev/test), `QdrantKnowledgeStore` (persistent, distributed — via `Ananke.Qdrant`), `OpenAIEmbeddingModel` (text-embedding-3-*), `PdfExtractor` (PDF → Markdown with heading/link/image detection), `MarkdownExtractor` (Markdown structural parsing), `SlidingWindowChunker` (Markdown-heading-aware).

#### Knowledge Catalog

Wrap any knowledge store with a catalog layer — document-level metadata (keywords, categories, timestamps) is maintained automatically as documents are ingested. Agents get two-phase discovery: find relevant sources first, then deep-search within them. Older documents are gradually deprioritized via configurable time decay.

```csharp
// Wrap the store with catalog + time decay
var catalog = new InMemoryKnowledgeCatalog(embeddingModel);     // or QdrantKnowledgeCatalog
var extractor = new CatalogKeywordExtractor(chatModel);          // LLM extracts keywords/category/summary
var catalogStore = new CatalogAwareKnowledgeStore(
    knowledgeStore, catalog, extractor,
    new TimeDecayOptions { HalfLifeDays = 90, FloorWeight = 0.3f });

// Upserts now auto-maintain the catalog. Searches apply time-decay reranking.
// Give agents catalog discovery tools alongside chunk search:
var tools = KnowledgeSearchTool.Create("knowledge", catalogStore, description: "...")
    .Merge(KnowledgeCatalogTools.Create(catalog));
```

---

### 🛑 Human-in-the-Loop

Pause execution at any step, checkpoint the full state, and resume with optional human input.

```csharp
var workflow = new Workflow<ApprovalState>("trade-approval")
    .Chain("analyze", "review", "execute")
    .Then("execute", Workflow.End)
    .InterruptBefore("execute")        // pause here for human approval
    .UseCheckpointing(checkpointStore);

// First run: pauses before "execute"
var execution = await workflow.RunAsync(initialState);
// execution.Status == Interrupted

// After the human approves:
var resumed = await workflow.ResumeAsync(execution.Id,
    state => state with { Approved = true });
```

---

### 🏭 Distributed State Machine

A production-grade FSM with distributed locking, composable middleware, and built-in circuit breaking (`OperationalStatus.Fault` / `Reset`). Designed for long-running services where multiple instances must coordinate safely.

```csharp
public class OrderMachine : AbstractStateMachine<OrderContext, OrderState, OrderTransition, OrderEvent>
{
    protected override void Transitions(ITransitionBuilder<OrderState, OrderTransition> builder)
    {
        builder
            .From(OrderState.Pending)
                .On(OrderTransition.Reserve).GoTo(OrderState.Reserved)
                .On(OrderTransition.Cancel).GoTo(OrderState.Cancelled)
            .From(OrderState.Reserved)
                .On(OrderTransition.Confirm).GoTo(OrderState.Confirmed)
                .On(OrderTransition.Cancel).GoTo(OrderState.Cancelled);
    }
}
```

---

### 🔌 MCP Server Integration

Expose any workflow or tool kit as an [MCP](https://modelcontextprotocol.io/) server capability with a single call.

```csharp
builder.Services.AddMcpServer(o => { ... })
    .WithAnankeTools(myToolKit)
    .WithAnankeWorkflow(
        name:         "run_pipeline",
        description:  "Runs the ETL pipeline and returns results",
        workflow:     etlWorkflow,
        stateFactory: args => new PipelineState { Input = args["input"].GetString()! });
```

---

### 🎨 Design Tooling

Define workflow topologies in a plain-text DSL, then bind your code to each job at runtime. Export any validated workflow as a [Mermaid](https://mermaid.js.org/) diagram for documentation or visual debugging.

```
plan -> fork(fetch_a, fetch_b)
join(fetch_a, fetch_b) -> combine
combine -> End
```

```csharp
var scaffold = WorkflowScaffold.Parse<MyState>("etl-pipeline", dsl);
var workflow  = scaffold
    .Bind("plan",    planJob)
    .Bind("fetch_a", fetchAJob)
    .Bind("fetch_b", fetchBJob)
    .Bind("combine", combineJob)
    .BindMerge("combine", branches => Merge(branches))
    .Build();

// Visualize the validated graph
Console.WriteLine(workflow.ToMermaid());
```

→ [Workflow DSL Reference](docs/workflow-dsl.md)

---

### 🗄️ Infrastructure

| Package | What it provides |
|---|---|
| `Ananke.Redis` | `IDistributedLock` via RedLock.net · `IKeyValueDataAdapter` via StackExchange.Redis |
| `Ananke.MQTT` | `IChannelReader` / `IChannelWriter` via MQTTnet · MessagePack serialization |

```csharp
services.AddRedis(o => { o.Host = "localhost"; o.Port = 6379; });
services.AddMqtt<MyContext, MyAction>(o => { o.Host = "localhost"; });
```

---

### 📡 OpenTelemetry Tracing

One call wires up distributed tracing with OTLP export to BetterStack, Jaeger, Grafana Tempo, or any compatible backend.

```csharp
services.AddTracingPipeline(o =>
{
    o.ServiceName    = "my-service";
    o.ServiceVersion = "1.0.0";
    o.UseOtlp(endpoint, $"Authorization=Bearer {token}");
});
```

---

## Getting Started

Install the meta-package to get everything:

```bash
dotnet add package Ananke
```

Or install only what you need:

```bash
dotnet add package Ananke.Orchestration          # core: workflows, agents, knowledge pipeline
dotnet add package Ananke.Orchestration.OpenAI     # OpenAI chat + embeddings
dotnet add package Ananke.Documents              # PDF + Markdown extraction for knowledge ingestion
dotnet add package Ananke.OpenTelemetry            # distributed tracing
```

---

## Packages

| Package | Description | NuGet |
|---|---|---|
| [`Ananke`](Ananke/) | Meta-package — install once, get everything | [![NuGet](https://img.shields.io/nuget/v/Ananke.svg)](https://www.nuget.org/packages/Ananke) |
| [`Ananke.Abstractions`](Ananke.Abstractions/) | Shared interfaces and contracts (`IDistributedLock`, `IChannelReader/Writer`, etc.) | [![NuGet](https://img.shields.io/nuget/v/Ananke.Abstractions.svg)](https://www.nuget.org/packages/Ananke.Abstractions) |
| [`Ananke.StateMachine`](Ananke.StateMachine/) | Distributed FSM engine with middleware pipeline | [![NuGet](https://img.shields.io/nuget/v/Ananke.StateMachine.svg)](https://www.nuget.org/packages/Ananke.StateMachine) |
| [`Ananke.Orchestration`](Ananke.Orchestration/) | Workflow builder, runner, agents, checkpointing | [![NuGet](https://img.shields.io/nuget/v/Ananke.Orchestration.svg)](https://www.nuget.org/packages/Ananke.Orchestration) |
| [`Ananke.Orchestration.OpenAI`](Ananke.Orchestration.OpenAI/) | OpenAI provider (`IStreamingAgentModel`) | [![NuGet](https://img.shields.io/nuget/v/Ananke.Orchestration.OpenAI.svg)](https://www.nuget.org/packages/Ananke.Orchestration.OpenAI) |
| [`Ananke.Orchestration.Anthropic`](Ananke.Orchestration.Anthropic/) | Anthropic / Claude provider (`IStreamingAgentModel`) | [![NuGet](https://img.shields.io/nuget/v/Ananke.Orchestration.Anthropic.svg)](https://www.nuget.org/packages/Ananke.Orchestration.Anthropic) |
| [`Ananke.Orchestration.Google`](Ananke.Orchestration.Google/) | Google Gemini provider (`IStreamingAgentModel`) | [![NuGet](https://img.shields.io/nuget/v/Ananke.Orchestration.Google.svg)](https://www.nuget.org/packages/Ananke.Orchestration.Google) |
| [`Ananke.MCP`](Ananke.MCP/) | Expose workflows and tools as MCP server capabilities | [![NuGet](https://img.shields.io/nuget/v/Ananke.MCP.svg)](https://www.nuget.org/packages/Ananke.MCP) |
| [`Ananke.Documents`](Ananke.Documents/) | Document extractors for the knowledge pipeline (PDF, Markdown) | [![NuGet](https://img.shields.io/nuget/v/Ananke.Documents.svg)](https://www.nuget.org/packages/Ananke.Documents) |
| [`Ananke.Qdrant`](Ananke.Qdrant/) | Qdrant vector database provider for `IKnowledgeStore` + `IKnowledgeCatalog` | [![NuGet](https://img.shields.io/nuget/v/Ananke.Qdrant.svg)](https://www.nuget.org/packages/Ananke.Qdrant) |
| [`Ananke.Redis`](Ananke.Redis/) | Distributed lock and key-value store via Redis | [![NuGet](https://img.shields.io/nuget/v/Ananke.Redis.svg)](https://www.nuget.org/packages/Ananke.Redis) |
| [`Ananke.MQTT`](Ananke.MQTT/) | Pub/sub channels via MQTTnet | [![NuGet](https://img.shields.io/nuget/v/Ananke.MQTT.svg)](https://www.nuget.org/packages/Ananke.MQTT) |
| [`Ananke.OpenTelemetry`](Ananke.OpenTelemetry/) | One-liner OTLP tracing export | [![NuGet](https://img.shields.io/nuget/v/Ananke.OpenTelemetry.svg)](https://www.nuget.org/packages/Ananke.OpenTelemetry) |
| [`Ananke.Design`](Ananke.Design/) | YAML manifest import and Mermaid diagram export | [![NuGet](https://img.shields.io/nuget/v/Ananke.Design.svg)](https://www.nuget.org/packages/Ananke.Design) |

---

## Demos

| Demo | What it shows |
|---|---|
| [`BasicAgentDemo`](demos/BasicAgentDemo/) | Direct model calls, capability-based model routing, and routed `AgentJob`s in a workflow |
| [`SimpleWorkflowDemo`](demos/SimpleWorkflowDemo/) | Interactive streaming chat agent with tool calling and OpenTelemetry tracing |
| [`AgenticWebDemo`](demos/AgenticWebDemo/) | HTTP SSE streaming with human-in-the-loop trade approval (analyze → interrupt → resume) |
| [`ExtendedFlowDemo`](demos/ExtendedFlowDemo/) | Fork/Join, SubFlow, Interrupt, streaming — all advanced routing patterns in one console app |
| [`DesignPipelineDemo`](demos/DesignPipelineDemo/) | YAML-defined workflow topology bound to OpenAI and Anthropic agents at runtime |
| [`LongTermMemoryDemo`](demos/LongTermMemoryDemo/) | PDF ingestion → vector store → knowledge catalog → agent Q&A with time-decay reranking |
| [`DistributedServicesDemo`](demos/DistributedServicesDemo/) | State machine + MQTT pub/sub + handoff channels + conversation memory in one pipeline |
| [`StateMachineDemo`](demos/StateMachineDemo/) | Standalone `AbstractStateMachine` walkthrough with guard conditions and middleware |
| [`McpServerDemo`](demos/McpServerDemo/) | Expose Ananke tools and a workflow as an MCP server for VS Code Copilot and Claude Desktop |

---

## Documentation

| Guide | What it covers |
|---|---|
| [Advanced Agent Features](docs/advanced-agent-features.md) | Local/custom endpoints (Ollama, LM Studio, vLLM, Azure OpenAI), response caching, resilient retries, decorator composition |
| [Workflow DSL Reference](docs/workflow-dsl.md) | Text DSL syntax, scaffold binding, router/fork/join patterns, Mermaid export |
| [Framework Comparison](docs/framework-comparison.md) | Side-by-side comparison with LangGraph, Agent Framework, Semantic Kernel, CrewAI, Smolagents, and Agno |
| [Design Decisions](docs/design-decisions.md) | Architecture Decision Records — `IAgentModel` vs `IChatClient`, and other trade-offs |
| [Background & Philosophy](docs/background.md) | The story and design philosophy behind Ananke |

---

## Philosophy

Ananke takes its name from the Greek primordial goddess of necessity — the force that fixed the laws of the cosmos before creation could begin. Before time could flow and matter could form, something unchanging had to exist first.

Software is no different. Before agents can act, before workflows can run, the rules must be stable.

→ [Read the full backstory and philosophy](docs/background.md)

---

## License

Licensed under the [Apache 2.0 License](LICENSE).

---

<p align="center">
  Made with ❤️ in Melbourne, Australia
</p>
