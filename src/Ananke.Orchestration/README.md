# Ananke.Orchestration

[![NuGet](https://img.shields.io/nuget/v/Ananke.Orchestration.svg)](https://www.nuget.org/packages/Ananke.Orchestration)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

Workflow orchestration engine for .NET — fluent graph-as-code builder, AI agent jobs with tool calling, fork/join parallelism, human-in-the-loop interrupts, checkpointing, and tracing.

## Install

```bash
dotnet add package Ananke.Orchestration
```

Add an LLM provider:

```bash
dotnet add package Ananke.Orchestration.OpenAI    # or Ananke.Orchestration.Anthropic
```

## Quick start

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

### DI registration

```csharp
using Ananke.Orchestration.Extensions;

services.AddWorkflowOrchestration(o => o
    .UseCheckpointing()
    .StoreCompletions(false)
    .WithCheckpointTtl(TimeSpan.FromDays(14)));
```

## Features

- **Fluent graph builder** — `.Then()`, `.Fork()`, `.Join()`, `.SubFlow()`, `.Chain()`
- **AI agent jobs** — `AgentJobFactory` with tool calling, structured output, token streaming
- **Conditional routing** — `Workflow.Decide()` for lambda-based, `DecideWithAgent()` for LLM-driven
- **Fork / Join** — fan-out to parallel branches, fan-in with merge function
- **Human-in-the-loop** — `.InterruptBefore()` / `.InterruptAfter()` with `ResumeAsync()`
- **Checkpointing** — persist and resume workflow state (`InMemoryCheckpointStore` or custom `ICheckpointStore` — see interface remarks for Redis/SQL guidance)
- **Resilience** — Polly-based retry built into the runner
- **Smart Tool Router** — `CompositeSmartToolRouter` pipeline with heuristic-tag, semantic-recall, affinity re-rank, health-filter, and LLM routing stages; controlled via `SmartToolRouterMiddleware`
- **Model decorators** — `ResilientAgentModel` (429 retry + OTel) and `CachingAgentModel` (LLM response caching)
- **Tracing** — `IWorkflowTracer` for OpenTelemetry integration

## Related packages

| Package | What it adds |
|---|---|
| `Ananke.Orchestration.Knowledge` | Knowledge pipeline — vector stores, document processing, catalog, linking (included as transitive dep) |
| `Ananke.Orchestration.OpenAI` | OpenAI `IStreamingAgentModel` + `IEmbeddingModel` provider |
| `Ananke.Orchestration.Anthropic` | Anthropic / Claude `IStreamingAgentModel` provider |
| `Ananke.Orchestration.Google` | Google Gemini `IStreamingAgentModel` + `IEmbeddingModel` provider |
| `Ananke.Documents` | PDF and Markdown extractors for the knowledge pipeline |
| `Ananke.MCP` | Expose workflows and tools as MCP server capabilities |
| `Ananke.OpenTelemetry` | OTLP tracing export (BetterStack, Jaeger, Grafana Tempo) |
| `Ananke` | Meta-package — includes Orchestration + StateMachine in one step |

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
