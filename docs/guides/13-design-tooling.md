<!-- topic: design-tooling, tags: dsl, manifest, yaml, topology, mermaid, scaffold, design, nnke -->
# 13 — Design Tooling

Define workflow topologies in a text DSL or YAML manifest, bind code at runtime,
and export validated graphs as Mermaid diagrams.

**Demo:** [DesignPipelineDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/02-workflow-patterns/DesignPipelineDemo)

→ **Full DSL reference:** [Workflow DSL Reference](../reference/workflow-dsl.md)

---

## Why Separate Topology from Behavior?

| Concern | DSL / YAML | Code (C#) |
|---|---|---|
| Graph structure | `plan -> fork(a, b)` | — |
| Job logic | — | `Func<TState, CancellationToken, Task<TState>>` |
| Merge functions | — | `Func<TState[], TState>` |
| Routers | — | `IRouter<TState>` |

This separation enables:
- **Design-first workflows** — architects define the graph, developers implement jobs
- **LLM-generated topologies** — an agent produces the DSL, the system binds implementations
- **External configuration** — embed DSL lines in YAML/JSON config files
- **Test fixtures** — parse a graph, plug in mock jobs

---

## Text DSL

### Syntax

```
# Direct connection
a -> b

# Terminal
a -> End

# Fork (parallel)
plan -> fork(fetch_a, fetch_b)

# Join (fan-in)
join(transform_a, transform_b) -> combine

# Router (dynamic decision)
analyze -> router(enrich, validate, End)
```

### Parse and Bind

```csharp
using Ananke.Design;

var scaffold = WorkflowScaffold.Parse<MyState>("pipeline", """
    ingest -> transform
    transform -> publish
    publish -> End
    """);

var workflow = scaffold
    .Bind("ingest",    async (state, ct) => state with { Raw = "data" })
    .Bind("transform", async (state, ct) => state with { Clean = "clean" })
    .Bind("publish",   async (state, ct) => state with { Done = true })
    .Build();

var result = await workflow.RunAsync(new MyState());
```

### Fork / Join

```csharp
var scaffold = WorkflowScaffold.Parse<PipelineState>("etl", """
    plan -> fork(fetch_a, fetch_b)
    fetch_a -> transform_a
    fetch_b -> transform_b
    join(transform_a, transform_b) -> combine
    combine -> End
    """);

var workflow = scaffold
    .Bind("plan",        planJob)
    .Bind("fetch_a",     fetchAJob)
    .Bind("fetch_b",     fetchBJob)
    .Bind("transform_a", transformAJob)
    .Bind("transform_b", transformBJob)
    .Bind("combine",     combineJob)
    .BindMerge("combine", branches => MergeResults(branches))
    .Build();
```

### Router

```csharp
var scaffold = WorkflowScaffold.Parse<AnalysisState>("analysis", """
    analyze -> router(enrich, validate, End)
    enrich -> analyze
    validate -> End
    """);

scaffold.BindRouter("analyze", Workflow.Decide<AnalysisState>(s =>
    s.Score > 0.8 ? "validate" :
    s.Score > 0.3 ? "enrich" :
    Workflow.End));
```

---

## Introspection

Check binding state before building:

```csharp
var scaffold = WorkflowScaffold.Parse<MyState>("pipeline", dsl);

IReadOnlySet<string> all     = scaffold.JobNames;       // all discovered jobs
IReadOnlySet<string> jobs    = scaffold.UnboundJobs;     // need Bind()
IReadOnlySet<string> merges  = scaffold.UnboundMerges;   // need BindMerge()
IReadOnlySet<string> routers = scaffold.UnboundRouters;  // need BindRouter()
```

---

## YAML Manifests

Define models, jobs, and topology in a YAML file:

```yaml
# etl-pipeline.yml
name: etl-pipeline

models:
  planner:
    provider: openai
    model: gpt-4.1-mini
    config_key: OpenAI:ApiKey
  analyst:
    provider: anthropic
    model: claude-sonnet-5
    config_key: Anthropic:ApiKey

jobs:
  plan:
    type: agent
    model: planner
    system_prompt: "Plan the ETL pipeline for the given datasets."
    max_tool_rounds: 3
  transform_a:
    type: agent
    model: analyst
    system_prompt: "Transform raw data into a clean summary."
  transform_b:
    type: agent
    model: analyst
    system_prompt: "Transform raw data into a clean summary."
  fetch_a:
    type: code
  fetch_b:
    type: code
  combine:
    type: agent
    model: planner
    system_prompt: "Combine dataset summaries into a final report."

connections:
  - plan -> fork(fetch_a, fetch_b)
  - fetch_a -> transform_a
  - fetch_b -> transform_b
  - join(transform_a, transform_b) -> combine
  - combine -> End
```

### Loading and Binding

```csharp
using Ananke.Design;

var manifest = WorkflowManifest.Load("etl-pipeline.yml");

// Resolve models from secrets
var models = new ModelResolver()
    .Register("openai", "OpenAI", OpenAIChatAgentModel.Create)
    .Register("anthropic", "Anthropic", AnthropicAgentModel.Create)
    .Resolve(manifest, key => config[key]);

// Parse topology
var scaffold = WorkflowScaffold.Parse<PipelineState>(manifest.Name, manifest.Connections);

// Bind agent jobs from YAML config
foreach (var (jobName, jobDef) in manifest.Jobs.Where(j => j.Value.Type == "agent"))
{
    var model = models[jobDef.ModelAlias!];
    var builder = AgentJobFactory.Create<PipelineState, AgentTextResponse>(jobName, model)
        .WithSystemPrompt(jobDef.SystemPrompt!)
        .WithPrompt(state => BuildPrompt(jobName, state))
        .MapResult((state, response) => ApplyResult(jobName, state, response.Text ?? ""))
        .WithMaxToolRounds(jobDef.MaxToolRounds);

    scaffold.Bind(jobName, builder.Build());
}

// Bind code jobs and merges manually
scaffold
    .Bind("fetch_a", fetchAJob)
    .Bind("fetch_b", fetchBJob)
    .BindMerge("combine", MergeResults);

var workflow = scaffold.Build();
```

### Model Validation

`ModelCatalog.Validate(provider, model)` checks a manifest's `model:` string against the known
catalog for that provider and returns a lifecycle-aware result — it never rewrites the string
(whatever the manifest says is what reaches the SDK), it only tells you whether it recognizes it:

| Model status | `IsValid` | `Message` |
|---|---|---|
| Current or Legacy (e.g. `claude-sonnet-5`, `claude-opus-4-8`) | `true` | `null` |
| Deprecated (e.g. `gpt-4.1`) | `true` | `"'gpt-4.1' is deprecated; use 'gpt-5.6-sol' instead."` |
| Retired | `false` | names the replacement, if there is one |
| Ambiguous family name (e.g. `sonnet` with no version) | `false` | `"'sonnet' is ambiguous for provider 'anthropic'. Specify a version."`, `Suggestions` lists the candidates |
| Unknown to this provider, or an unrecognized provider entirely | `true` | a warning naming the provider/model — passed through, not rejected, so a brand-new model the catalog hasn't been updated for yet still works |

In practice, `Retired` currently never fires: a model confirmed retired by its provider is removed
from `Models.cs`/`ModelCatalog` outright rather than kept around with a `Retired` lifecycle entry
(see [Model Deprecations](../reference/model-deprecations.md#removed-models-retired-then-deleted)),
so a manifest referencing one falls into the "unknown to this provider" row above instead. The
`Retired` branch stays in `Validate()` for the window between "provider announced a firm date" and
"we've verified and removed the constant."

```csharp
var result = ModelCatalog.Validate("openai", manifestModelString);
if (!result.IsValid)
    logger.LogWarning("Manifest model rejected: {Message}", result.Message);
else if (result.Message is not null)
    logger.LogInformation(result.Message); // deprecated or unrecognized — still usable
```

See [Model Deprecations](../reference/model-deprecations.md) for the full lifecycle policy
(Current/Legacy/Deprecated/Retired) and the current per-provider status tables.

---

## Tool Binding from Manifests

When a manifest declares `tools:` sections, `WorkflowToolResolver` reads those
declarations and builds per-job `ToolKit` instances automatically, so you don't
have to wire tools by hand:

`WorkflowToolResolver` is a static class — call `ResolveJobToolKitsAsync` directly:

```csharp
using Ananke.Design.Tools;

// Resolve all manifest-declared tools into a per-job map
IReadOnlyDictionary<string, ToolKit> toolKits =
    await WorkflowToolResolver.ResolveJobToolKitsAsync(manifest, bindingResolver);

// Bind agent jobs using the resolved kits
foreach (var (jobName, jobDef) in manifest.Jobs.Where(j => j.Value.Type == "agent"))
{
    var kit = toolKits.GetValueOrDefault(jobName) ?? new ToolKit(jobName);
    var builder = AgentJobFactory.Create<PipelineState, AgentTextResponse>(jobName, model)
        .WithSystemPrompt(jobDef.SystemPrompt!)
        .WithTools(kit);

    scaffold.Bind(jobName, builder.Build());
}
```

### Smart-Router Stage Declarations

A job's manifest entry can declare a `router:` block of smart-tool-router stages — narrowing
or re-ranking which tools reach the model on that job's turn. This is **per-job tool routing**,
unrelated to `Workflow.Decide`/job-to-job routing:

```yaml
# my-workflow.ananke.yml (excerpt)
jobs:
  triage:
    type: agent
    model: planner
    tools: [search_kb, escalate]
    router:
      - kind: pinned
        tools: [escalate]
      - kind: semantic_recall
        top_k: 5
```

Valid `kind` values are `pinned`, `health_filter`, `semantic_recall`, `affinity_rerank`,
`heuristic_tags`, and `llm` (which additionally requires `model:`) — see
[16 — Agentic Patterns § Smart Tool Router](16-agentic-patterns.md#smart-tool-router) for what
each stage does. Fields are flat under each stage item (`top_k:`, `tools:`, `model:`,
`max_selected:`) — there's no nested `options:` key.

`ResolveJobToolKitsAsync` builds the stages via `RouterStageFactory.Build` and attaches the
result with `ToolKit.WithRouter(...)` automatically — there's nothing extra to wire by hand;
the returned `ToolKit` for `triage` already has the smart router attached.

### Testing Tool Resolution

For tests, use `InMemoryToolBindingResolver` to avoid external registries:

```csharp
var resolver = new InMemoryToolBindingResolver()
    .Register("my-binding-ref", someToolDefinition);

var toolKits = await WorkflowToolResolver.ResolveJobToolKitsAsync(testManifest, resolver);
// Jobs with no tool: references are simply omitted from the result
```

---

## Mermaid Diagram Export

Export any validated workflow as a Mermaid diagram:

```csharp
// As raw Mermaid syntax
string mermaid = workflow.ToMermaid();

// As a Markdown code block
string markdown = workflow.ToMarkdownMermaid();
```

Output:

```mermaid
graph TD
  plan --> fetch_a
  plan --> fetch_b
  fetch_a --> transform_a
  fetch_b --> transform_b
  transform_a --> combine
  transform_b --> combine
  combine --> __end__
```

---

## What's Next

| Next guide | What you'll learn |
|---|---|
| [14 — Testing](14-testing.md) | In-memory implementations for zero-config testing |

---

← [Back to Learning Path](../learning-path.md)
