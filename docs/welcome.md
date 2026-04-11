<!-- topic: welcome, tags: landing, overview, start, navigation, docs, nnke -->

## What Makes Ananke Different

Most AI frameworks treat the LLM as the foundation and bolt infrastructure on top.
Ananke inverts that: **typed state, distributed coordination, and production observability
come first**. The LLM is a pluggable component.

- **Swap providers without touching business logic** — OpenAI, Anthropic, Google, or local
  models through the same `IStreamingAgentModel` interface
- **Test without API keys** — every infrastructure contract ships with a zero-config
  in-memory implementation
- **Agents that learn** — three-layer memory (RAG, episodic, empirical) with offline learning, skill packages, and Monte Carlo reward propagation
- **Agentic patterns** — Review & Critique, Iterative Refinement — recognized patterns as first-class workflow builders
- **External skill catalog** — discover and run Python, Node.js, and Docker tools from registries directly through ToolKit
- **nnke design CLI** — scaffold projects, validate topologies, export Mermaid diagrams, and serve as an MCP companion for AI coding tools
- **Production-ready from day one** — distributed locking, checkpointing, circuit breaking,
  and OpenTelemetry tracing built in

---

## Quick Start

```bash
dotnet add package Ananke
```

```csharp
var workflow = new Workflow<MyState>("hello")
    .Job("greet", async (state, ct) => state with { Message = "Hello from Ananke!" })
    .Then("greet", Workflow.End);

var result = await workflow.RunAsync(new MyState());
Console.WriteLine(result.State.Message);
```

-> [Full getting started guide](guides/01-getting-started.md) | [nnke design companion](guides/00-nnke-tool.md)

---

## Choose Your Path

| Goal | Start here |
|---|---|
| Build a streaming chatbot | [Guides 01, 03, 04, 05](learning.md) |
| Document Q&A (RAG) | [Guides 01, 03, 04, 06](learning.md) |
| Agentic workflow with human approval | [Guides 01, 02, 03, 04, 07](learning.md) |
| Agents that learn from experience | [Guides 01, 03, 04, 06, 15](learning.md) |
| Distributed multi-service coordination | [Guides 01, 02, 08, 09](learning.md) |
| Use the nnke design CLI | [Guide 00](guides/00-nnke-tool.md) |
| Agentic patterns (review, refine) | [Guide 16](guides/16-agentic-patterns.md) |

-> [Complete learning path and topic index](learning.md)

---

## Explore

- [Feature Index](reference/features.md) — every capability in one scannable table
- [FAQ](faq.md) — answers to common questions by topic
- [Background & Philosophy](about/background.md) — why infrastructure first and why the name Ananke