# Ananke.Design

[![NuGet](https://img.shields.io/nuget/v/Ananke.Design.svg)](https://www.nuget.org/packages/Ananke.Design)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

Design-time tooling for Ananke — YAML manifest import, workflow topology DSL, model resolution, and Mermaid diagram export. Define pipeline structure declaratively; bind job implementations in code.

## Install

```bash
dotnet add package Ananke.Design
```

## What it does

`Ananke.Design` separates **what the workflow looks like** (graph structure, model aliases, system prompts) from **what each job does** (C# code). This enables design-first workflows, LLM-generated topologies, and external configuration — while keeping job implementations type-safe and testable.

## Quick start — DSL scaffold

Parse a topology from text, bind implementations, build a runnable workflow:

```csharp
using Ananke.Design;

var scaffold = WorkflowScaffold.Parse<MyState>("etl-pipeline", """
    plan -> fork(fetch_a, fetch_b)
    fetch_a -> transform_a
    fetch_b -> transform_b
    join(transform_a, transform_b) -> combine
    combine -> End
    """);

var workflow = scaffold
    .Bind("plan", async (state, ct) => state with { Step = "planned" })
    .Bind("fetch_a", fetchAJob)
    .Bind("fetch_b", fetchBJob)
    .Bind("transform_a", async (state, ct) => state)
    .Bind("transform_b", async (state, ct) => state)
    .Bind("combine", async (state, ct) => state)
    .BindMerge("combine", branches => branches[0])
    .Build();

var result = await workflow.RunAsync(initialState);
```

## Quick start — YAML manifest

Load a full manifest with model definitions, agent jobs, and connections from an `.ananke.yml` file:

```yaml
name: research-pipeline

models:
  fast:
    provider: openai
    model: gpt-4.1-mini
  local:
    provider: openai
    model: llama3.2
    endpoint: http://localhost:11434/v1

jobs:
  plan:
    type: agent
    model: fast
    system_prompt: You are a research planner.
  search:
    type: agent
    model: local
  synthesize:
    type: code

connections:
  - plan -> fork(search_web, search_db)
  - join(search_web, search_db) -> synthesize
  - synthesize -> End
```

```csharp
var manifest = WorkflowManifest.Load("pipeline.ananke.yml");

var models = new ModelResolver()
    .Register("openai", "OpenAI", OpenAIChatAgentModel.Create)
    .Resolve(manifest, key => config[key]);
```

## DSL syntax

| Pattern | Example | Equivalent fluent API |
|---|---|---|
| Direct | `a -> b` | `.Then("a", "b")` |
| Terminal | `a -> End` | `.Then("a", Workflow.End)` |
| Fork | `a -> fork(b, c)` | `.Then("a", Workflow.Fork("b", "c"))` |
| Fork (best-effort) | `a -> fork(b, c, mode: best-effort)` | `.Then("a", Workflow.Fork("b", "c", ForkMode.BestEffort))` |
| Join | `join(a, b) -> c` | `.Join(["a", "b"], "c", merge)` |
| Router | `a -> router(b, c, End)` | `.Then("a", Workflow.Decide(...))` |
| SubFlow | `subflow(name)` | `.SubFlow("name", inner, mapIn, mapOut)` |
| Interrupt | `interrupt(name)` | `.InterruptBefore("name")` |

Full syntax reference: **[docs/workflow-dsl.md](https://github.com/sevensamurai/Ananke/blob/main/docs/workflow-dsl.md)**

## What is included

| Type | Description |
|---|---|
| `WorkflowScaffold<TState>` | Parse DSL topology, bind jobs/merges/routers, build a `Workflow<TState>` |
| `WorkflowManifest` | Parse `.ananke.yml` files — models, jobs, connections |
| `ModelResolver` | Resolve manifest model aliases to live `IAgentModel` instances via registered provider factories |
| `WorkflowDiagramExtensions` | `.ToMermaid()` export for any validated workflow graph |
| `AgentTextResponse` | Default structured response type for agent jobs that return plain text |
| `WorkflowDslParser` | Internal DSL parser — direct, fork, join, router, subflow, interrupt |

## Mermaid diagram export

```csharp
var definition = workflow.Validate();
var mermaid = definition.ToMermaid();
Console.WriteLine(mermaid);
```

Outputs:

```
graph TD
    _plan["▶ plan"]
    _fetch_a["fetch_a"]
    _fetch_b["fetch_b"]
    _combine["combine"]
    _end(["End"])

    _plan -->|fork| _fetch_a
    _plan -->|fork| _fetch_b
    _fetch_a --> _combine
    _fetch_b --> _combine
    _combine --> _end
```

## Related packages

| Package | What it adds |
|---|---|
| `Ananke.Orchestration` | Workflow engine, agent jobs, tool calling, streaming |
| `Ananke.Orchestration.OpenAI` | OpenAI / Ollama / Azure OpenAI `IStreamingAgentModel` provider |
| `Ananke.Orchestration.Anthropic` | Anthropic / Claude `IStreamingAgentModel` provider |
| `Ananke.MCP` | Expose workflows and tools as MCP server capabilities |
| `Ananke` | Meta-package — includes Orchestration + StateMachine + Bridge |

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
