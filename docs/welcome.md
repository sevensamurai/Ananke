<!-- topic: welcome, tags: landing, overview, start, navigation, docs, nnke -->

## What Makes Ananke Different

Most AI frameworks start with the LLM and build infrastructure around it. Ananke starts from the other end: fix the contracts first, type the state, make the vendor layer genuinely pluggable — then let agents, workflows, and state machines do their work on a foundation that won't shift. The goal isn't a better on-ramp to a hosted runtime. It's infrastructure you own, that stays yours as requirements grow, and that gets smarter the longer it runs.

- **Swap providers without touching business logic** — OpenAI, Anthropic, Google, or local
  models through the same `IStreamingAgentModel` interface
- **Test without API keys** — every infrastructure contract ships with a zero-config
  in-memory implementation
- **Agents that learn** — three-layer memory (RAG, episodic, empirical) with offline learning, skill packages, and Monte Carlo reward propagation
- **Organics** — autonomous agent ecosystems that self-organise, adapt, and evolve their own policies over time without manual re-configuration
- **Federation** — run workflows across organisational boundaries; join nodes into a mesh, share agent pools, and get unified telemetry across the whole cluster
- **Agentic patterns** — Review & Critique, Iterative Refinement — recognized patterns as first-class workflow builders
- **External skill catalog** — discover and run Python, Node.js, and Docker tools from registries directly through ToolKit
- **nnke Tools** — `nnke` scaffolds, validates, runs, and serves workflows locally; `nnke-platform` deploys, observes, and manages the federation mesh in the cloud
- **Production-oriented infrastructure** — distributed locking, checkpointing, circuit breaking,
  and OpenTelemetry tracing built in; not added later

> **Status:** Ananke is a release candidate (v0.8.x). The foundation is stable and the core surface area is locked. See the [Roadmap](about/roadmap.md) for what is complete, what is in progress, and what the path to 1.0 looks like.

---

## Quick Start

Install the core package and one provider:

```bash
dotnet add package Ananke.Orchestration
dotnet add package Ananke.Orchestration.OpenAI
```

Two LLM-backed agents, one typed state record, one `.Chain()` call to wire them together.

```csharp
using Ananke.Orchestration;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.OpenAI;
using Ananke.Orchestration.Workflows;

// Swap for AnthropicAgentModel or GeminiAgentModel — the workflow is unchanged.
var model = OpenAIChatAgentModel.Create(
    Environment.GetEnvironmentVariable("OPENAI_API_KEY")!, "gpt-4.1-mini");

var researchJob = AgentJobFactory.Create<PipelineState>("research", model)
    .WithSystemPrompt("You are a research assistant. List three key facts concisely.")
    .WithPrompt(s => $"Topic: {s.Topic}")
    .MapResult((s, text) => s with { Facts = text })
    .Build();

var reviewJob = AgentJobFactory.Create<PipelineState>("review", model)
    .WithSystemPrompt("You are an editor. Distill the facts into one sentence.")
    .WithPrompt(s => s.Facts)
    .MapResult((s, text) => s with { Summary = text })
    .Build();

var workflow = new Workflow<PipelineState>("research-pipeline")
    .Job("research", researchJob)
    .Job("review", reviewJob)
    .Chain("research", "review")
    .Then("review", Workflow.End);

var result = await workflow.RunAsync(new PipelineState { Topic = "CRISPR" });
Console.WriteLine(result.State.Summary);
Console.WriteLine(result.Status); // Completed

record PipelineState(string Topic = "", string Facts = "", string Summary = "");
```


-> [Full getting started guide](guides/01-getting-started.md) | [nnke Tools overview](cli/nnke-tools.md)

---

## Choose Your Path

| Goal | Start here |
|---|---|
| Build a streaming chatbot | [Guides 01, 03, 04, 05](learning-path.md) |
| Document Q&A (RAG) | [Guides 01, 03, 04, 06](learning-path.md) |
| Agentic workflow with human approval | [Guides 01, 02, 03, 04, 07](learning-path.md) |
| Agents that learn from experience | [Guides 01, 03, 04, 06, 15](learning-path.md) |
| Distributed multi-service coordination | [Guides 01, 02, 08, 09](learning-path.md) |
| Use the nnke CLI tools | [nnke Tools overview](cli/nnke-tools.md) |
| Run a manifest locally without credentials | [`nnke run`](cli/nnke-tool.md) |
| Agentic patterns (review, refine) | [Guide 16](guides/16-agentic-patterns.md) |

-> [Complete learning path](learning-path.md) | [Browse runnable demos](demos.md)

---

## Explore

- [Feature Index](reference/features.md) — every capability in one scannable table
- [Demos](demos.md) — runnable projects mapped to the guide set
- [FAQ](faq.md) — answers to common questions by topic
- [Background & Philosophy](about/background.md) — why infrastructure first and why the name Ananke
