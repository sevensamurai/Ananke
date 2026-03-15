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

**Ananke** is a vendor-agnostic .NET framework that gives AI agents and automated pipelines
everything they need to run in production — typed state, workflow orchestration, tool calling,
long-term memory, human-in-the-loop approval, distributed coordination, and observability —
so you can focus on agent logic instead of infrastructure.

Beyond orchestration, Ananke makes agents *smarter over time*:
they accumulate knowledge from documents, recognize patterns across interactions,
and build reusable skills and heuristics — turning raw LLM capability into
compounding operational intelligence.

From a streaming chat agent to state-machine-coordinated multi-service pipelines:
`dotnet add package Ananke` and [start building](#demos).

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

The answer is a typed, testable, composable foundation where the infrastructure comes first and LLM providers are pluggable.

→ **[How does Ananke compare to LangGraph, Agent Framework, CrewAI, and others?](docs/about/framework-comparison.md)**

---

## Capabilities

### 🔀 Workflow Orchestration
Fluent graph-as-code builder · conditional & LLM-driven routing · fork/join parallelism · nested sub-workflows · human-in-the-loop interrupts · typed `IAsyncEnumerable` event streaming

### 🤖 AI Agents
`AgentJob` with tool calling + structured output · token-level streaming · multimodal messages (text, image, audio) · `ChatSessionEvent` async stream · multi-provider (OpenAI, Anthropic, Google Gemini + any OpenAI-compatible endpoint) · capability-based model routing · production decorators (429 retry with OTel, LLM response caching)

### 🧠 Long-Term Memory
Document ingestion pipeline (extract → chunk → embed → store) · vector-indexed semantic search · knowledge catalog with LLM-enriched metadata and time-decay reranking · empirical memory (patterns, skills, heuristics learned over time)

### 🛑 Human-in-the-Loop
Pause execution at any step · checkpoint full workflow state · resume with optional human input · interrupt stack for state machines

### 🏭 State Machine
Simplified `IStateMachine<S,T>` with interrupt stack for in-process scenarios · production `AbstractStateMachine` with RedLock coordination · composable middleware pipeline · guard conditions · circuit breaking (fault/reset)

### 🌐 ASP.NET Core
SSE streaming · state-machine-driven chat sessions · in-memory session management · provider configuration helpers

### 🔌 MCP Server
Expose any workflow or tool kit as an [MCP](https://modelcontextprotocol.io/) server capability

### 🎨 Design Tooling
Plain-text DSL for workflow topology · runtime binding · Mermaid diagram export

### 🗄️ Infrastructure
Checkpointing (InMemory / File) · distributed locking (Redis) · MQTT pub/sub · OpenTelemetry tracing

### 🧑‍💻 Developer Experience
Idiomatic C# (async/await, DI, generics) · full in-memory test mode for every infrastructure contract · 15 focused NuGet packages

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

Then explore the [demos](#demos) to see each capability in action.

---

## Demos

Each demo is a self-contained project you can run to see a specific capability end to end.

| Demo | What it shows |
|---|---|
| [`BasicAgentDemo`](demos/BasicAgentDemo/) | Direct model calls, capability-based model routing, and routed `AgentJob`s in a workflow |
| [`SimpleWorkflowDemo`](demos/SimpleWorkflowDemo/) | Interactive streaming chat agent with tool calling and OpenTelemetry tracing |
| [`AgenticWebDemo`](demos/AgenticWebDemo/) | HTTP SSE streaming with human-in-the-loop trade approval (analyze → interrupt → resume) |
| [`PetAdoptionDemo`](demos/PetAdoptionDemo/) | Multi-phase state-machine chat with `KnowledgeBase`, payment interrupts, and SSE streaming — full JS frontend included |
| [`Connect4Demo`](demos/Connect4Demo/) | Two `StreamingChatWorkflow` agents play Connect 4; a third agent provides live commentary |
| [`ExtendedFlowDemo`](demos/ExtendedFlowDemo/) | Fork/Join, SubFlow, Interrupt, streaming — all advanced routing patterns in one console app |
| [`DesignPipelineDemo`](demos/DesignPipelineDemo/) | YAML-defined workflow topology bound to OpenAI and Anthropic agents at runtime |
| [`LongTermMemoryDemo`](demos/LongTermMemoryDemo/) | PDF ingestion → vector store → knowledge catalog → agent Q&A with time-decay reranking |
| [`DistributedServicesDemo`](demos/DistributedServicesDemo/) | State machine + MQTT pub/sub + handoff channels + conversation memory in one pipeline |
| [`StateMachineDemo`](demos/StateMachineDemo/) | Standalone `AbstractStateMachine` walkthrough with guard conditions and middleware |
| [`McpServerDemo`](demos/McpServerDemo/) | Expose Ananke tools and a workflow as an MCP server for VS Code Copilot and Claude Desktop |

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
| [`Ananke.Qdrant`](Ananke.Qdrant/) | Qdrant vector database provider for `IKnowledgeStore`, `IKnowledgeCatalog`, and `IEmpiricalMemory` | [![NuGet](https://img.shields.io/nuget/v/Ananke.Qdrant.svg)](https://www.nuget.org/packages/Ananke.Qdrant) |
| [`Ananke.Redis`](Ananke.Redis/) | Distributed lock and key-value store via Redis | [![NuGet](https://img.shields.io/nuget/v/Ananke.Redis.svg)](https://www.nuget.org/packages/Ananke.Redis) |
| [`Ananke.MQTT`](Ananke.MQTT/) | Pub/sub channels via MQTTnet | [![NuGet](https://img.shields.io/nuget/v/Ananke.MQTT.svg)](https://www.nuget.org/packages/Ananke.MQTT) |
| [`Ananke.OpenTelemetry`](Ananke.OpenTelemetry/) | One-liner OTLP tracing export | [![NuGet](https://img.shields.io/nuget/v/Ananke.OpenTelemetry.svg)](https://www.nuget.org/packages/Ananke.OpenTelemetry) |
| [`Ananke.AspNetCore`](Ananke.AspNetCore/) | SSE streaming, provider configuration, and session management for ASP.NET Core | [![NuGet](https://img.shields.io/nuget/v/Ananke.AspNetCore.svg)](https://www.nuget.org/packages/Ananke.AspNetCore) |
| [`Ananke.Design`](Ananke.Design/) | YAML manifest import and Mermaid diagram export | [![NuGet](https://img.shields.io/nuget/v/Ananke.Design.svg)](https://www.nuget.org/packages/Ananke.Design) |

---

## Documentation

| Guide | What it covers |
|---|---|
| [Advanced Agent Features](docs/reference/advanced-agent-features.md) | Local/custom endpoints (Ollama, LM Studio, vLLM, Azure OpenAI), response caching, resilient retries, decorator composition |
| [Tools & ToolKit Reference](docs/reference/tools-reference.md) | ToolDefinition, ToolParameter, ToolKit API, parameter examples for LLM accuracy, MCP/A2A integration |
| [Workflow DSL Reference](docs/reference/workflow-dsl.md) | Text DSL syntax, scaffold binding, router/fork/join patterns, Mermaid export |
| [Framework Comparison](docs/about/framework-comparison.md) | Side-by-side comparison with LangGraph, Agent Framework, Semantic Kernel, CrewAI, Smolagents, and Agno |
| [Design Decisions](docs/reference/design-decisions.md) | Architecture Decision Records — `IAgentModel` vs `IChatClient`, and other trade-offs |
| [Background & Philosophy](docs/about/background.md) | The story and design philosophy behind Ananke |

---

## Philosophy

Ananke takes its name from the Greek primordial goddess of necessity — the force that fixed the laws of the cosmos before creation could begin. Before time could flow and matter could form, something unchanging had to exist first.

Software is no different. Before agents can act, before workflows can run, the rules must be stable.

→ [Read the full backstory and philosophy](docs/about/background.md)

---

## License

Licensed under the [Apache 2.0 License](LICENSE).

---

<p align="center">
  Made with ❤️ in Melbourne, Australia
</p>
