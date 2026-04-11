<!-- topic: dsl-syntax, tags: dsl, topology, design, fork, join, router, subflow, interrupt, connections, manifest -->
# Ananke.Design — Workflow DSL

`Ananke.Design` provides a text-based DSL for declaring workflow topology separately from job implementations.
The DSL defines the graph structure (connections, forks, joins, routers); 
code supplies the behavior via `Bind` methods.

## Why separate topology from behavior?

| Concern | DSL (text) | Code (C#) |
|---------|-----------|-----------|
| **Graph structure** | `plan -> fork(a, b)` | — |
| **Job logic** | — | `Func<TState, CancellationToken, Task<TState>>` or `IJob<TState>` |
| **Merge functions** | — | `Func<TState[], TState>` |
| **Routers** | — | `IRouter<TState>` |

This separation enables:
- **Design-first workflows** — architects define the graph, developers implement jobs
- **LLM-generated topologies** — an agent produces the DSL, the system binds registered implementations
- **Test fixtures** — parse a graph, plug in mock jobs
- **External configuration** — embed DSL lines in YAML/JSON config files

## DSL Syntax Reference

### Direct connection

```
a -> b
```

Connects job `a` to job `b`. Equivalent to `workflow.Then("a", "b")`.

### Terminal connection

```
a -> End
```

Marks job `a` as terminal. Equivalent to `workflow.Then("a", Workflow.End)`.

### Fork (parallel execution)

```
plan -> fork(fetch_a, fetch_b)
```

Fan-out from `plan` to `fetch_a` and `fetch_b` running in parallel (default: FailFast mode).

With explicit mode:

```
plan -> fork(fetch_a, fetch_b, mode: best-effort)
```

Supported modes: `fail-fast` (default), `best-effort`.

### Join (fan-in)

```
join(transform_a, transform_b) -> combine
```

Waits for `transform_a` and `transform_b` to complete, then merges their states and continues to `combine`. Requires a merge function bound via `BindMerge()`.

### Router (dynamic decision)

```
analyze -> router(enrich, validate, End)
```

Declares `analyze` as a decision point with the listed options. Requires an `IRouter<TState>` bound via `BindRouter()`.

### Comments and blank lines

```
# This is a comment
plan -> validate    # Inline comment

validate -> End
```

Lines starting with `#` and blank lines are ignored. Inline `#` comments are stripped.

### SubFlow (nested workflow)

```
subflow(refine)
```

Marks the job `refine` as a nested sub-workflow. The job must also appear in a connection (e.g. `draft -> refine`). At bind time, supply the inner workflow and state mappers via `BindSubFlow()`.

### Interrupt (human-in-the-loop)

```
interrupt(publish)
```

Pauses execution before the named job runs. The workflow returns with `ExecutionStatus.Interrupted` and can be resumed via `ResumeAsync`. Requires `UseCheckpointing` to be configured on the built workflow.

## Usage

### Basic: linear workflow

```csharp
var scaffold = WorkflowScaffold.Parse<MyState>("pipeline", """
    ingest -> transform
    transform -> publish
    publish -> End
    """);

var workflow = scaffold
    .Bind("ingest", async (state, ct) => state with { Raw = "data" })
    .Bind("transform", async (state, ct) => state with { Clean = "clean" })
    .Bind("publish", async (state, ct) => state with { Done = true })
    .Build();

var result = await workflow.RunAsync(initialState);
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
    .Bind("plan", async (state, ct) => state with { Step = "planned" })
    .Bind("fetch_a", async (state, ct) => state with { RawA = "data-A" })
    .Bind("fetch_b", async (state, ct) => state with { RawB = "data-B" })
    .Bind("transform_a", async (state, ct) => state with { CleanA = "ok" })
    .Bind("transform_b", async (state, ct) => state with { CleanB = "ok" })
    .Bind("combine", async (state, ct) => state with { Output = "merged" })
    .BindMerge("combine", branches =>
    {
        var a = branches.FirstOrDefault(b => b.CleanA is not null);
        var b = branches.FirstOrDefault(b2 => b2.CleanB is not null);
        return new PipelineState { CleanA = a?.CleanA, CleanB = b?.CleanB };
    })
    .Build();
```

### Router (dynamic decision)

```csharp
var scaffold = WorkflowScaffold.Parse<AnalysisState>("analysis", """
    analyze -> router(enrich, validate, End)
    enrich -> analyze
    validate -> End
    """);

var workflow = scaffold
    .Bind("analyze", async (state, ct) => state with { Score = ComputeScore(state) })
    .Bind("enrich", async (state, ct) => state with { Data = Enrich(state) })
    .Bind("validate", async (state, ct) => state with { Valid = true })
    .BindRouter("analyze", Workflow.Decide<AnalysisState>(s =>
        s.Score > 0.8 ? "validate" :
        s.Score > 0.3 ? "enrich" :
        Workflow.End))
    .Build();
```

### SubFlow (nested workflow)

```csharp
var editLoop = new Workflow<EditState>("edit-loop")
    .Job("edit", async (state, ct) => state with { Text = "polished", Attempts = state.Attempts + 1 })
    .Job("validate", async (state, ct) => state with { Valid = state.Attempts >= 2 })
    .Then("edit", "validate")
    .Then("validate", Workflow.Decide<EditState>(s => s.Valid ? Workflow.End : "edit"));

var scaffold = WorkflowScaffold.Parse<DocState>("pipeline", """
    draft -> refine
    refine -> publish
    publish -> End
    subflow(refine)
    """);

var workflow = scaffold
    .Bind("draft", async (state, ct) => state with { Draft = "rough draft" })
    .BindSubFlow("refine", editLoop,
        parent => new EditState { Text = parent.Draft },
        (parent, child) => parent with { Draft = child.Text })
    .Bind("publish", async (state, ct) => state with { Published = true })
    .Build();
```

### Interrupt (human-in-the-loop)

```csharp
var scaffold = WorkflowScaffold.Parse<ApprovalState>("approval", """
    draft -> review
    review -> publish
    publish -> End
    interrupt(publish)
    """);

var workflow = scaffold
    .Bind("draft", async (state, ct) => state with { Draft = "content" })
    .Bind("review", async (state, ct) => state with { Reviewed = true })
    .Bind("publish", async (state, ct) => state with { Published = true })
    .Build()
    .UseCheckpointing(new InMemoryCheckpointStore());

// First run pauses before "publish"
var execution = await workflow.RunAsync(initialState);
// execution.Status == ExecutionStatus.Interrupted

// Resume after human approval
var resumed = await workflow.ResumeAsync(execution.Id);
```

### Using IJob&lt;TState&gt; implementations

```csharp
var scaffold = WorkflowScaffold.Parse<MyState>("pipeline", """
    ingest -> transform
    transform -> End
    """);

var workflow = scaffold
    .Bind("ingest", new IngestJob())       // IJob<MyState>
    .Bind("transform", new TransformJob()) // IJob<MyState>
    .Build();
```

### Parsing from a collection of lines

```csharp
// Useful when loading from YAML or config files
string[] lines = ["plan -> validate", "validate -> End"];

var scaffold = WorkflowScaffold.Parse<MyState>("from-config", lines);
```

## Introspection

The scaffold exposes binding state for tooling and diagnostics:

```csharp
var scaffold = WorkflowScaffold.Parse<MyState>("pipeline", dsl);

// All job names discovered from the DSL
IReadOnlySet<string> all = scaffold.JobNames;

// What still needs binding
IReadOnlySet<string> jobs     = scaffold.UnboundJobs;
IReadOnlySet<string> merges   = scaffold.UnboundMerges;
IReadOnlySet<string> routers  = scaffold.UnboundRouters;
IReadOnlySet<string> subflows = scaffold.UnboundSubFlows;
```

## Combining with Mermaid export

The scaffold builds a standard `Workflow<TState>`, so all existing extensions work:

```csharp
var workflow = scaffold
    .Bind(/* ... */)
    .Build();

// Export the topology as a Mermaid diagram
string mermaid = workflow.ToMermaid();
string markdown = workflow.ToMarkdownMermaid();
```

## Validation

The scaffold validates at two stages:

1. **Parse time** — syntax errors, minimum argument counts (fork ≥ 2 targets, join ≥ 2 sources, router ≥ 2 options), and directive targets referencing known jobs (subflow/interrupt)
2. **Build time** — all jobs bound, all join merges bound, all routers bound, all subflows bound. The resulting `Workflow<TState>.Build()` then applies the standard graph validation (reachability, terminal connections, etc.)

Error messages are explicit:

```
Unbound job(s): fetch_a, fetch_b. Call Bind() for each job before building.
Unbound merge(s) for join target(s): combine. Call BindMerge() for each join target before building.
Unbound subflow(s): refine. Call BindSubFlow() for each subflow before building.
Job 'unknown' is not declared in the DSL. Known jobs: plan, fetch_a, fetch_b
```

## Project structure

```
Ananke.Design/
├── Ananke.Design.csproj
├── AgentTextResponse.cs            — Default structured response type for text-output agents
├── ModelResolver.cs                — Resolves YAML model aliases to live IAgentModel instances
├── WorkflowDiagramExtensions.cs    — Mermaid/Markdown export (Workflow → text)
├── WorkflowManifest.cs             — Parsed .ananke.yml manifest (models, jobs, connections)
├── WorkflowScaffold.cs             — DSL import and binding (text → Workflow)
└── Dsl/
    ├── ConnectionLine.cs           — Parsed line discriminated union
    └── WorkflowDslParser.cs        — Regex-based DSL parser
```

**Dependency direction:** `Ananke.Design → Ananke.Orchestration → Ananke.Abstractions`
(leaf package — nothing depends on it)
