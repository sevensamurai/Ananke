# Ananke.Orchestration

[![NuGet](https://img.shields.io/nuget/v/Ananke.Orchestration.svg)](https://www.nuget.org/packages/Ananke.Orchestration)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

Workflow orchestration engine for .NET — typed workflows, agent jobs, streaming chat loops, tool execution, checkpointing, and orchestration-level tracing.

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
    .Join(["search_web", "search_db"], "synthesize", Merge);

var result = await workflow.RunAsync(new ResearchState { Query = "distributed systems" });
```

The same workflow surface supports:

- delegate jobs and custom `IJob<TState>` implementations
- direct, routed, looped, fork/join, and sub-workflow transitions
- checkpoint/resume for human-in-the-loop scenarios
- event streaming through `IWorkflowRunner.StreamAsync(...)`
- agent jobs backed by `IAgentModel` and `IStreamingAgentModel`

### DI registration

```csharp
using Ananke.Orchestration.Extensions;

services.AddWorkflowOrchestration(o => o
    .UseCheckpointing()
    .StoreCompletions(false)
    .WithCheckpointTtl(TimeSpan.FromDays(14)));
```

## Features

- **Typed workflow builder** — `Workflow<TState>` with `Job`, `Then`, `Chain`, `Loop`, `Fork`, `Join`, and `SubFlow`
- **Routing primitives** — `Workflow.Decide(...)`, `Workflow.DecideAsync(...)`, and `Workflow.DecideWithAgent(...)`
- **Agent jobs** — `AgentJob<TState,TResponse>` for structured output and `TextAgentJob<TState>` for plain-text generations
- **Streaming chat workflow** — `StreamingChatWorkflow` builder for agent → tools → agent loops with delta callbacks
- **Checkpointing and resume** — `ICheckpointStore`, interrupts, and `IWorkflowRunner.ResumeAsync(...)`
- **Workflow event streaming** — `IWorkflowRunner.StreamAsync(...)` for progress, fork/join, and terminal events
- **Tool execution** — `ToolKit`, `ToolDefinition`, `ToolBuilder`, memory-backed tool gating, and execution strategies
- **Smart tool routing** — `CompositeSmartToolRouter` and routing stages surfaced through `SmartToolRouterMiddleware`
- **Model middleware** — logging, guardrails, caching, resilience, and tool-window narrowing at the `IAgentModel` layer
- **Pattern builders** — `AgenticPattern.ReviewCritique<TState>()`, `AgenticPattern.IterativeRefinement<TState>()`, and `AgenticPattern.Interview<TState>()` (conversational turns via `Workflow<TState>.AwaitInput`, resumed by platform adapters via `WorkflowInputExtensions.ResumeWithInputAsync`)
- **Tracing and budgets** — `IWorkflowTracer`, workflow trace context, token-usage capture for model calls, and a `BudgetExceeded<TState>` workflow event backed by `IBudgetMeter`
- **Adaptive harness** — `CompositeAdaptiveHarnessPolicy` reacts to per-episode `TrajectorySnapshot`s (from `Ananke.Abstractions.Trajectory`): triggers a learning cycle on hallucination spikes and rewards/penalizes tool affinities on clean successes or abandoned faults

## Key surfaces

| Type | Purpose |
|---|---|
| `Workflow<TState>` | Fluent workflow definition and convenience execution entry point |
| `IWorkflowRunner` / `WorkflowRunner` | Execution, resume, and event streaming engine |
| `IJob<TState>` | Job abstraction for custom workflow work units |
| `IWorkflowJobMiddleware<TState>` | Cross-cutting wrapper around workflow job execution |
| `AgentJob<TState,TResponse>` | Structured-output agent job with optional tool loop |
| `TextAgentJob<TState>` | Plain-text agent job with optional tool loop |
| `StreamingChatWorkflow` | Pre-built streaming conversation loop with optional memory and tools |
| `ToolKit` | Named collection of tools, tool-memory integration, and routing hooks |
| `AgenticPattern` | Factory for common multi-step agent patterns |
| `CompositeAdaptiveHarnessPolicy` | Default `IAdaptiveHarnessPolicy`/`ITrajectoryObserver` — adapts tool affinities and triggers learning cycles from completed-episode trajectory snapshots |

## Package boundaries

`Ananke.Orchestration` depends on `Ananke.Orchestration.Knowledge`, but the knowledge package remains independently consumable. This package also includes bridge types that expose knowledge stores and catalogs as agent-callable tools.

For compatibility, several agent and knowledge types are type-forwarded so existing consumers that referenced them through `Ananke.Orchestration` continue to resolve after package extraction.

## Related packages

| Package | What it adds |
|---|---|
| `Ananke.Orchestration.Knowledge` | Vector stores, document processing, knowledge catalog, and document-linking pipeline (included transitively) |
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
