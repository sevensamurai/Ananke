<!-- topic: workflows, tags: workflow, job, fork, join, routing, parallelism, sub-workflow, streaming, conditional -->
# 02 — Workflows

Build complex orchestration graphs with conditional routing, fork/join parallelism,
sub-workflows, and real-time event streaming.

**Demo:** [AgenticDesignPatternsDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/02-workflow-patterns/AgenticDesignPatternsDemo)

---

## The Workflow Builder

Every workflow starts with `new Workflow<TState>(name)`. You register jobs, wire
edges, and call `.RunAsync()`:

```csharp
using Ananke.Orchestration;

var workflow = new Workflow<MyState>("example")
    .Job("step_a", async (state, ct) => state with { A = "done" })
    .Job("step_b", async (state, ct) => state with { B = "done" })
    .Then("step_a", "step_b")
    .Then("step_b", Workflow.End);

var result = await workflow.RunAsync(new MyState());
```

### Routing Primitives

| Primitive | What it does |
|---|---|
| `.Then("a", "b")` | Direct edge — `a` routes to `b` |
| `.Then("a", Workflow.End)` | Terminal — `a` ends the workflow |
| `.Chain("a", "b", "c")` | Shorthand for `Then("a","b")` + `Then("b","c")` |
| `.Then("a", Workflow.Decide<S>(...))` | Conditional routing via lambda |
| `.Then("a", Workflow.DecideWithAgent<S>(...))` | LLM-driven routing |
| `.Then("a", Workflow.Fork("b", "c"))` | Fan-out to parallel branches |
| `.Join(["b","c"], "d", merge)` | Fan-in with merge function |
| `.SubFlow("name", inner, mapIn, mapOut)` | Nest a workflow inside another |

---

## Conditional Routing

Route to different jobs based on runtime state:

```csharp
var workflow = new Workflow<OrderState>("order-flow")
    .Job("classify", async (state, ct) =>
        state with { Priority = state.Amount > 1000 ? "high" : "low" })
    .Job("fast_track", async (state, ct) => state with { Lane = "express" })
    .Job("standard",   async (state, ct) => state with { Lane = "normal" })
    .Then("classify", Workflow.Decide<OrderState>(state =>
        state.Priority == "high" ? "fast_track" : "standard"))
    .Then("fast_track", Workflow.End)
    .Then("standard", Workflow.End);
```

The `Decide` lambda receives the current state and returns the name of the next job
(or `Workflow.End`).

---

## Fork / Join (Parallel Execution)

Fan-out to multiple branches running in parallel, then merge results:

```csharp
var workflow = new Workflow<ResearchState>("parallel-research")
    .Job("plan", async (state, ct) =>
        state with { Plan = "Search web + database" })
    .Job("search_web", async (state, ct) =>
        state with { WebResults = ["Result A", "Result B"] })
    .Job("search_db", async (state, ct) =>
        state with { DbResults = ["Record 1", "Record 2"] })
    .Job("synthesize", async (state, ct) =>
        state with { Summary = $"Found {state.WebResults.Count + state.DbResults.Count} results" })
    .Then("plan", Workflow.Fork("search_web", "search_db"))
    .Join(["search_web", "search_db"], "synthesize", branches =>
    {
        var web = branches.FirstOrDefault(b => b.WebResults.Count > 0);
        var db  = branches.FirstOrDefault(b => b.DbResults.Count > 0);
        return new ResearchState
        {
            Plan       = branches[0].Plan,
            WebResults = web?.WebResults ?? [],
            DbResults  = db?.DbResults ?? []
        };
    })
    .Then("synthesize", Workflow.End);
```

**Fork modes:**
- **FailFast** (default) — if any branch throws, all branches are cancelled.
- **BestEffort** — all branches run to completion; failures are collected.

```csharp
.Then("plan", Workflow.Fork("search_web", "search_db", ForkMode.BestEffort))
```

---

## Sub-Workflows

Nest a workflow inside another with state mapping:

```csharp
var inner = new Workflow<InnerState>("validation")
    .Job("check", async (state, ct) => state with { Valid = true })
    .Then("check", Workflow.End);

var outer = new Workflow<OuterState>("pipeline")
    .Job("prepare", async (state, ct) => state with { Data = "ready" })
    .SubFlow("validate", inner,
        mapIn:  outer => new InnerState { Input = outer.Data },
        mapOut: (outer, inner) => outer with { IsValid = inner.Valid })
    .Then("prepare", "validate")
    .Then("validate", Workflow.End);
```

---

## Workflow Streaming

Consume orchestration events in real time via `IAsyncEnumerable`:

```csharp
using Ananke.Orchestration.Streaming;

await foreach (var evt in workflow.StreamAsync(initialState))
{
    switch (evt)
    {
        case JobStarted<MyState> js:
            Console.WriteLine($"▶ {js.JobName} starting");
            break;
        case JobCompleted<MyState> jc:
            Console.WriteLine($"✓ {jc.JobName} completed ({jc.Duration.TotalMilliseconds:F0}ms)");
            break;
        case StateUpdated<MyState> su:
            Console.WriteLine($"  state updated");
            break;
        case WorkflowCompleted<MyState> wc:
            Console.WriteLine($"✅ Done: {wc.Result.Status}");
            break;
        case WorkflowFaulted<MyState> wf:
            Console.WriteLine($"❌ Faulted: {wf.Exception.Message}");
            break;
    }
}
```

---

## Job Retry and Timeout

Add resilience to individual jobs with Polly-based retry and per-job timeout:

```csharp
var workflow = new Workflow<MyState>("resilient")
    .Job("flaky_api", async (state, ct) =>
    {
        var data = await CallExternalApi(ct);
        return state with { Data = data };
    },
    retry: 3,                                    // retry up to 3 times
    timeout: TimeSpan.FromSeconds(10))           // 10s per attempt
    .Then("flaky_api", Workflow.End);
```

---

## Graph Validation

Invalid topologies fail at **build time**, not at runtime. The workflow builder
validates:
- All jobs referenced in edges are registered
- Terminal connections exist (no dangling jobs)
- Fork targets are reachable
- Join sources match fork branches

---

## Mermaid Diagram Export

Export any workflow as a Mermaid diagram for documentation:

```csharp
using Ananke.Design;

Console.WriteLine(workflow.ToMermaid());
// graph TD
//   plan --> search_web
//   plan --> search_db
//   search_web --> synthesize
//   search_db --> synthesize
//   synthesize --> __end__
```

---

## What's Next

| Next guide | What you'll learn |
|---|---|
| [03 — Agents](03-agents.md) | Drop LLMs into workflow jobs |
| [04 — Tools](04-tools.md) | Give agents callable functions |
| [05 — Streaming Chat](05-streaming-chat.md) | Build a streaming chat UI |

---

← [Back to Learning Path](learning-path.md)
