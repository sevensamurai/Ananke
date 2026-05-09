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

# Ananke — AI Agent Orchestration Framework for .NET

---

**Ananke** is a vendor-agnostic, production-ready .NET framework for building AI agents and automated multi-step pipelines. It provides typed workflow orchestration, LLM tool calling, multi-provider AI model support, long-term memory (RAG + empirical learning), agentic design patterns, human-in-the-loop approval, distributed coordination, an external skill catalog, a design-time CLI (`nnke`), and OpenTelemetry observability — all with idiomatic C# and no external services required for testing.

Supports **OpenAI** (GPT-4.1, o-series), **Anthropic Claude**, **Google Gemini**, and any **OpenAI-compatible endpoint** including Ollama, Azure OpenAI, LM Studio, Groq, Deepseek, and Together AI.

Beyond orchestration, Ananke makes agents *smarter over time*: they accumulate knowledge from documents, recognize patterns across interactions, and build reusable skills and heuristics — turning raw LLM capability into compounding operational intelligence.

From a streaming chat agent to state-machine-coordinated multi-service pipelines:
`dotnet add package Ananke` and [start building](#getting-started).

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

---

## Capabilities

### ?? Workflow Orchestration
Fluent graph-as-code builder · conditional & LLM-driven routing · fork/join parallelism · nested sub-workflows · human-in-the-loop interrupts · typed `IAsyncEnumerable` event streaming

### ?? AI Agents
`AgentJob` with tool calling + structured output · token-level streaming · multimodal messages (text, image, audio) · `ChatSessionEvent` async stream · multi-provider (OpenAI, Anthropic, Google Gemini + any OpenAI-compatible endpoint) · capability-based model routing · production decorators (429 retry with OTel, LLM response caching)

### ?? Agentic Patterns
`AgenticPattern.ReviewCritique<T>()` — generator ? critic ? loop until approved · `AgenticPattern.IterativeRefinement<T>()` — single-agent refinement loop · pre-wired builders for recognized design patterns on top of `Workflow<T>` primitives

### ?? Long-Term Memory
Document ingestion pipeline (extract ? chunk ? embed ? store) · vector-indexed semantic search · knowledge catalog with LLM-enriched metadata and time-decay reranking · empirical memory (patterns, skills, heuristics) · episode store with temporal trajectories · Monte Carlo reward propagation · tag importance tracking · **skill package export/import** — portable bundles of learned knowledge with quality gates and trust scaling

### ?? Human-in-the-Loop
Pause execution at any step · checkpoint full workflow state · resume with optional human input · interrupt stack for state machines

### ?? State Machine
Simplified `IStateMachine<S,T>` with interrupt stack for in-process scenarios · production `AbstractStateMachine` with RedLock coordination · composable middleware pipeline · guard conditions · circuit breaking (fault/reset)

### ?? ASP.NET Core
SSE streaming · state-machine-driven chat sessions · in-memory session management · provider configuration helpers

### ?? MCP Server
Expose any workflow or tool kit as an [MCP](https://modelcontextprotocol.io/) server capability

### ?? Design Tooling
Plain-text DSL for workflow topology · runtime binding · Mermaid diagram export

### ??? nnke Design CLI
Scaffold new workflow projects · validate `.ananke.yml` topology files · export Mermaid diagrams · inspect project health · browse docs from the terminal · `nnke mcp` exposes all capabilities as MCP tools for AI coding assistants (GitHub Copilot, Claude, etc.)

### ??? Infrastructure
Checkpointing (InMemory / File) · distributed locking (Redis) · MQTT pub/sub · OpenTelemetry tracing

### ?? External Skill Catalog
Discover CLI tools from the [OpenClaw/ClawHub](https://clawhub.io) registry · local JSON cache for offline search · `ToolKit.AddFromCatalogAsync()` for natural-language skill discovery · local skill voting and reliability scoring · **runs Python (PyPI) tools via `uvx`, Node.js tools via `npx`, Docker containers, or any shell command — no Python installation required**

### ????? Developer Experience
Idiomatic C# (async/await, DI, generics) · full in-memory test mode for every infrastructure contract · 18 focused NuGet packages

---

## Quick Example

Build a typed multi-step pipeline in minutes:

```csharp
using Ananke.Orchestration;

record ContentState
{
    public string Topic { get; init; } = "";
    public string Draft  { get; init; } = "";
    public string Final  { get; init; } = "";
}

var workflow = new Workflow<ContentState>("content-pipeline")
    .Job("draft",  async (state, ct) => state with { Draft = $"Draft on: {state.Topic}" })
    .Job("polish", async (state, ct) => state with { Final = state.Draft.ToUpperInvariant() })
    .Chain("draft", "polish")
    .Then("polish", Workflow.End);

var result = await workflow.RunAsync(new ContentState { Topic = "AI in .NET" });
Console.WriteLine(result.State.Final);  // "DRAFT ON: AI IN .NET"
Console.WriteLine(result.Status);        // Completed
```

Drop an LLM agent into any job with the same API — add `Ananke.Orchestration.OpenAI` and replace the delegate with an `AgentJob`. See [Getting Started](docs/guides/01-getting-started.md) for a full walkthrough.

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
| [`AgenticWebDemo`](demos/AgenticWebDemo/) | HTTP SSE streaming with human-in-the-loop trade approval (analyze ? interrupt ? resume) |
| [`PetAdoptionDemo`](demos/PetAdoptionDemo/) | Multi-phase state-machine chat with `KnowledgeBase`, payment interrupts, and SSE streaming — full JS frontend included |
| [`Connect4Demo`](demos/Connect4Demo/) | Two `StreamingChatWorkflow` agents play Connect 4; a third agent provides live commentary |
| [`ExtendedFlowDemo`](demos/ExtendedFlowDemo/) | Fork/Join, SubFlow, Interrupt, streaming — all advanced routing patterns in one console app |
| [`DesignPipelineDemo`](demos/DesignPipelineDemo/) | YAML-defined workflow topology bound to OpenAI and Anthropic agents at runtime |
| [`LongTermMemoryDemo`](demos/LongTermMemoryDemo/) | PDF ingestion ? vector store ? knowledge catalog ? agent Q&A with time-decay reranking |
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
| [`Ananke.A2A`](Ananke.A2A/) | Agent-to-Agent (A2A) protocol — call remote agents as `IAgentModel`, expose workflows as A2A endpoints | [![NuGet](https://img.shields.io/nuget/v/Ananke.A2A.svg)](https://www.nuget.org/packages/Ananke.A2A) |
| [`Ananke.Learning`](Ananke.Learning/) | Empirical memory, offline learning, episode store, Monte Carlo reward propagation, skill package export/import | [![NuGet](https://img.shields.io/nuget/v/Ananke.Learning.svg)](https://www.nuget.org/packages/Ananke.Learning) |
| [`Ananke.Skills`](Ananke.Skills/) | External skill catalog — discover and run CLI tools from the OpenClaw registry via `ToolKit` | [![NuGet](https://img.shields.io/nuget/v/Ananke.Skills.svg)](https://www.nuget.org/packages/Ananke.Skills) |
| [`Ananke.Documents`](Ananke.Documents/) | Document extractors for the knowledge pipeline (PDF, Markdown) | [![NuGet](https://img.shields.io/nuget/v/Ananke.Documents.svg)](https://www.nuget.org/packages/Ananke.Documents) |
| [`Ananke.Qdrant`](Ananke.Qdrant/) | Qdrant vector database provider for `IKnowledgeStore`, `IKnowledgeCatalog`, and `IEmpiricalMemory` | [![NuGet](https://img.shields.io/nuget/v/Ananke.Qdrant.svg)](https://www.nuget.org/packages/Ananke.Qdrant) |
| [`Ananke.Redis`](Ananke.Redis/) | Distributed lock and key-value store via Redis | [![NuGet](https://img.shields.io/nuget/v/Ananke.Redis.svg)](https://www.nuget.org/packages/Ananke.Redis) |
| [`Ananke.MQTT`](Ananke.MQTT/) | Pub/sub channels via MQTTnet | [![NuGet](https://img.shields.io/nuget/v/Ananke.MQTT.svg)](https://www.nuget.org/packages/Ananke.MQTT) |
| [`Ananke.OpenTelemetry`](Ananke.OpenTelemetry/) | One-liner OTLP tracing export | [![NuGet](https://img.shields.io/nuget/v/Ananke.OpenTelemetry.svg)](https://www.nuget.org/packages/Ananke.OpenTelemetry) |
| [`Ananke.AspNetCore`](Ananke.AspNetCore/) | SSE streaming, provider configuration, and session management for ASP.NET Core | [![NuGet](https://img.shields.io/nuget/v/Ananke.AspNetCore.svg)](https://www.nuget.org/packages/Ananke.AspNetCore) |
| [`Ananke.Design`](Ananke.Design/) | YAML manifest import and Mermaid diagram export | [![NuGet](https://img.shields.io/nuget/v/Ananke.Design.svg)](https://www.nuget.org/packages/Ananke.Design) |

---

## Documentation & Guides

The full documentation hub and progressive learning path are at **[docs/learning.md](docs/learning.md)** and the **[Feature Index](docs/reference/features.md)**.

### Core Guides

| Guide | What it covers |
|---|---|
| [Getting Started](docs/guides/01-getting-started.md) | Install Ananke, build your first workflow, make your first LLM call |
| [Workflows](docs/guides/02-workflows.md) | Workflow builder, conditional routing, fork/join parallelism, sub-workflows, event streaming |
| [Agents & LLM Providers](docs/guides/03-agents.md) | OpenAI, Anthropic, Google Gemini, local models (Ollama, Azure OpenAI, LM Studio), model routing, multimodal |
| [Tools & ToolKit](docs/guides/04-tools.md) | Typed tool parameters, `ToolResult`, async tools, JSON Schema inference |
| [Streaming Chat](docs/guides/05-streaming-chat.md) | `StreamingChatWorkflow`, SSE endpoints, web UI integration, conversation memory |
| [Long-Term Memory](docs/guides/06-memory.md) | Document ingestion, RAG pipeline, knowledge catalog, semantic search, time-decay reranking |
| [Human-in-the-Loop](docs/guides/07-human-in-the-loop.md) | Interrupt before/after, checkpointing, resume with modified state |
| [State Machine](docs/guides/08-state-machine.md) | Distributed FSM, guard conditions, middleware pipeline, circuit breaking |
| [Distributed Systems](docs/guides/09-distributed.md) | Redis locking, MQTT pub/sub, agent handoff across processes |
| [Observability](docs/guides/10-observability.md) | OpenTelemetry tracing, OTLP export, span attributes, retry event reporting |
| [Advanced Agent Features](docs/guides/11-advanced-agents.md) | Local/custom endpoints, response caching, resilient retries, decorator composition |
| [MCP & A2A Interop](docs/guides/12-mcp-and-interop.md) | Expose as MCP server, consume MCP tools, A2A agent-to-agent protocol |
| [Design Tooling](docs/guides/13-design-tooling.md) | Visual workflow design, YAML manifests, Mermaid diagram export |
| [Agentic Patterns](docs/guides/16-agentic-patterns.md) | Review & Critique, Iterative Refinement — pre-wired pattern builders |
| [nnke Tool Companion](docs/guides/00-nnke-tool.md) | Design-time CLI — scaffold, validate, diagram, MCP companion for AI tools |
| [nnke-platform Tool](docs/guides/nnke-platform-tool.md) | Federation CLI — install adapters, deploy workflows, monitor and manage cloud deployments |
| [Empirical Memory & Skill Packaging](docs/guides/15-empirical-memory.md) | Patterns, skills, heuristics, confidence tracking, offline learning, episode store, skill export/import |
| [Empirical Memory Tuning](docs/guides/15a-empirical-memory-tuning.md) | `AffectOptions`, `OfflineLearnerOptions`, domain-specific recipes (game agents, incident response) |
| [Testing](docs/guides/14-testing.md) | In-memory implementations, zero-config integration tests |
| [uv & uvx Setup for .NET Developers](docs/guides/uv-setup-for-dotnet-developers.md) | Run Python-based OpenClaw skills from C# with one tool installed — no Python knowledge required |

### Reference

| Reference | What it covers |
|---|---|
| [Tools & ToolKit Reference](docs/reference/tools-reference.md) | `ToolDefinition`, `ToolParameter`, `ToolKit` API, parameter examples for LLM accuracy |
| [Workflow DSL Reference](docs/reference/workflow-dsl.md) | Text DSL syntax, scaffold binding, router/fork/join patterns, Mermaid export |
| [Full Feature Index](docs/reference/features.md) | Every capability in one scannable table |
| [Skill Catalog (Ananke.Skills)](Ananke.Skills/README.md) | External skill registry integration, `ISkillCatalog`, `OpenClawCatalog`, scoring |
| [Design Decisions](docs/reference/design-decisions.md) | Architecture Decision Records — `IAgentModel` vs `IChatClient`, and other trade-offs |
| [FAQ](docs/faq.md) | Frequently asked questions |
| [Background & Philosophy](docs/about/background.md) | The story and design philosophy behind Ananke |

---

## Frequently Asked Questions

Common questions are answered in the **[FAQ](docs/faq.md)**. Quick answers:

- **Which LLM providers are supported?** OpenAI (GPT-4.1, o-series), Anthropic Claude, Google Gemini, and any OpenAI-compatible endpoint — Ollama, Azure OpenAI, LM Studio, Groq, Deepseek, Together AI, vLLM.
- **Can I test without a real LLM or external services?** Yes — every infrastructure contract ships with an in-memory implementation. Integration tests run in milliseconds with no API keys.
- **Is Ananke only for chat agents?** No — it supports batch pipelines, state-machine workflows, distributed multi-service coordination, document-ingestion pipelines, and agentic design patterns alongside interactive chat.
- **Can agents learn and improve over time?** Yes — `IEmpiricalMemory` accumulates patterns, skills, and heuristics from interactions. `IOfflineLearner` runs background sweeps that decay stale beliefs, explore hypotheses, and promote stable knowledge to the permanent store.
- **Does Ananke support MCP (Model Context Protocol)?** Yes — expose any `ToolKit` or `Workflow` as an MCP server (compatible with VS Code Copilot, Claude Desktop, and any MCP client) and consume tools from external MCP servers.
- **Does it support the A2A agent protocol?** Yes — call remote A2A agents as drop-in `IAgentModel` implementations and expose Ananke workflows as A2A endpoints.
- **Can learned knowledge be transferred between agents?** Yes — `ISkillPackager` exports empirical entries and linked episodes as a portable JSON package (quality gates: min confidence, min strength, min observations). Import into any other agent with configurable trust scaling. Tag importance weights are bundled so the receiving agent inherits feature correlations too.
- **What is the OpenClaw skill catalog?** `Ananke.Skills` connects to the [OpenClaw/ClawHub](https://clawhub.io) registry of CLI-based tools. One call to `toolkit.AddFromCatalogAsync("airbnb search lodging")` discovers, caches, and resolves matching tools as `ToolDefinition` entries that any agent can call.
- **What is nnke?** A .NET CLI tool (`dotnet tool install -g nnke`) for design-time workflow tasks: scaffold projects, validate topology files, export Mermaid diagrams, inspect project health, and browse docs. `nnke mcp` runs as an MCP server so AI coding tools (GitHub Copilot, Claude) can call these capabilities directly.
- **What is nnke-platform?** A companion CLI tool (`dotnet tool install -g nnke-platform`) for federation — deploy, monitor, and manage workflow deployments across cloud platforms (Azure AI, Vertex AI, Claude). Platform adapters are installed as separate companion tools (`nnke-platform-azure`, `nnke-platform-google`, `nnke-platform-anthropic`) or all at once via `nnke-platform-all`. Each adapter is an independently published .NET tool that registers itself with `nnke-platform` via module initializers at runtime.
- **What is the difference between a Workflow and a State Machine?** A workflow runs a directed pipeline end to end (best for task pipelines, document processing, batch jobs). A state machine models long-lived entities with stable states and event-driven transitions (best for conversation sessions, order lifecycle, device management). Both compose: state machines can invoke workflows, and workflows can interact with state machines.

? **[Full FAQ ?](docs/faq.md)**

---

## Philosophy

Ananke takes its name from the Greek primordial goddess of necessity — the force that fixed the laws of the cosmos before creation could begin. Before time could flow and matter could form, something unchanging had to exist first.

Software is no different. Before agents can act, before workflows can run, the rules must be stable.

? [Read the full backstory and philosophy](docs/about/background.md)

---

## License

Licensed under the [Apache 2.0 License](LICENSE).

---

<p align="center">
  Made with ?? in Melbourne, Australia
</p>
