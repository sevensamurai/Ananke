<!-- topic: faq-general, tags: faq, general, installation, setup, install, nuget, requirements -->
# FAQ — General & Installation

← [Back to all FAQs](../faq.md)

---

## General

### What is Ananke?

Ananke is a vendor-agnostic, production-ready .NET framework for building AI agents and
automated multi-step pipelines. It provides:

- **Typed workflow orchestration** — directed graphs of jobs with compile-time state safety
- **LLM agent integration** — tool calling, structured output, token-level streaming
- **Multi-provider AI model support** — OpenAI, Anthropic, Google Gemini, and any
  OpenAI-compatible endpoint
- **Long-term memory** — document ingestion (RAG), knowledge catalog, empirical learning
- **Human-in-the-loop** — pause, checkpoint, and resume workflows with human review
- **Distributed coordination** — Redis distributed locking, MQTT pub/sub, agent handoff
- **OpenTelemetry observability** — automatic spans for workflows, state machines, and LLM calls
- **MCP & A2A interoperability** — expose tools/workflows as MCP servers, consume MCP tools,
  use the A2A agent protocol

### Who is Ananke for?

Ananke is designed for .NET developers (C# 12+, .NET 10) building production AI systems.
It is suitable for:

- Streaming chat agents and AI assistants
- Document Q&A systems (RAG pipelines)
- Multi-step agentic task pipelines
- State-machine-driven conversation flows
- Distributed multi-service agentic architectures
- Any system where AI agents need to call tools, remember context, or coordinate across processes

### What .NET version is required?

Ananke targets **.NET 10**.

### Is Ananke production-ready?

Yes. Ananke is designed with production requirements first:

- All state is typed end-to-end — the compiler enforces correctness
- Every infrastructure contract (`IDistributedLock`, `IKnowledgeStore`, `ICheckpointStore`,
  `IConversationMemory`) has a well-defined interface and a zero-config in-memory implementation
  for testing
- Distributed coordination uses Redis RedLock (via `Ananke.Redis`)
- LLM calls have automatic 429 retry with exponential backoff and OTel reporting
  (`ResilientAgentModel`)
- LLM response caching is built in (`CachingAgentModel`)
- Polly integration provides circuit breakers and custom resilience pipelines
- OpenTelemetry tracing is emitted automatically for workflows, state transitions, and tool calls

### Is Ananke open source?

Yes. Ananke is licensed under the [Apache 2.0 License](LICENSE).

---

## Installation

### How do I install Ananke?

Install the meta-package to get everything:

```bash
dotnet add package Ananke
```

Or install only the packages you need:

```bash
dotnet add package Ananke.Orchestration            # core: workflows, agents, tools, knowledge
dotnet add package Ananke.Orchestration.OpenAI     # OpenAI chat + embeddings provider
dotnet add package Ananke.Documents                # PDF + Markdown document extraction
dotnet add package Ananke.OpenTelemetry            # OTLP distributed tracing
```

### What is the minimal install for a streaming chat agent?

```bash
dotnet add package Ananke.Orchestration
dotnet add package Ananke.Orchestration.OpenAI
```

### What is the minimal install for a document Q&A (RAG) pipeline?

```bash
dotnet add package Ananke.Orchestration
dotnet add package Ananke.Orchestration.OpenAI   # chat + embeddings
dotnet add package Ananke.Documents              # PDF + Markdown extraction
```

### How many packages are there?

Ananke is split into focused NuGet packages so you only take the dependencies you need.
The full list is in the [README packages table](README.md#packages) and the
[Feature Index](../reference/features.md).

---

← [Back to all FAQs](../faq.md) · [Feature Index](../reference/features.md) · [Getting Started](../guides/01-getting-started.md)
