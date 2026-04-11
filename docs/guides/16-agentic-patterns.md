<!-- topic: agentic-patterns, tags: agentic-patterns, review-critique, iterative-refinement, router, handoff, loop -->
# 16 — Agentic Patterns

Build recognized agentic design patterns with `AgenticPattern` — pre-wired
workflow builders for review-and-critique, iterative refinement, and more.

**Demo:** [ReviewCritiqueDemo](../../src/demos/ReviewCritiqueDemo/) *(coming soon)*

---

## Why a Pattern Layer?

The `Workflow<TState>` builder gives you full compositional freedom:
`Job`, `Then`, `Fork`, `Join`, `SubFlow`, `Decide`, `DecideWithAgent`.
You can build **any** orchestration graph.

But when you're implementing a recognized agentic design pattern — the kind
documented by [Google Cloud](https://docs.cloud.google.com/architecture/choose-design-pattern-agentic-ai-system)
or the [Awesome Agentic Patterns](https://www.agentic-patterns.com/patterns)
catalog — you want:

1. **Discoverability** — type `AgenticPattern.` and see every available pattern
2. **Guided construction** — fluent builders that require the right parts and validate at `Build()`
3. **Named topology** — the generated workflow carries the pattern's semantics, not just arbitrary edges

`AgenticPattern` provides all three. Under the hood, each builder calls the
same `Workflow<TState>` primitives you'd use directly.

---

## Pattern Catalog

| Pattern | Entry Point | What it does |
|---|---|---|
| **Review & Critique** | `AgenticPattern.ReviewCritique<TState>(...)` | Generator → Critic → loop until approved or max iterations |
| **Iterative Refinement** | `AgenticPattern.IterativeRefinement<TState>(...)` | Single agent refine loop until quality threshold or max iterations |

More patterns will be added in future releases (Map-Reduce, Swarm, etc.).

---

## Review and Critique

The **Review and Critique** pattern (also called Generator-Critic) wires two
agents in a feedback loop:

```
generator → critic → [approved?] → end
               ↑          │ no
               └──────────┘
```

### Basic Usage

```csharp
using Ananke.Orchestration;

var workflow = AgenticPattern.ReviewCritique<ArticleState>("draft-review")
    .WithGenerator(generatorAgent)    // produces or revises output
    .WithCritic(criticAgent)          // evaluates quality
    .Until(s => s.ApprovalScore >= 0.9)
    .MaxIterations(5)                 // safety cap (default: 5)
    .Build();

var result = await workflow.RunAsync(new ArticleState { Topic = "AI agents" });
```

### With Inline Delegates

If your generate/critique logic is simple, skip `IJob<TState>` and use lambdas:

```csharp
var workflow = AgenticPattern.ReviewCritique<DraftState>("code-review")
    .WithGenerator("generate", async (state, ct) =>
    {
        // Call your LLM or code generation logic
        var draft = await GenerateCode(state.Spec, ct);
        return state with { Code = draft };
    })
    .WithCritic("review", async (state, ct) =>
    {
        // Evaluate the generated code
        var review = await ReviewCode(state.Code, ct);
        return state with { Score = review.Score, Feedback = review.Notes };
    })
    .Until(s => s.Score >= 0.95)
    .Build();
```

### Loop Exit Callback

Track why the loop terminated for logging or metrics:

```csharp
.OnLoopExit((state, reason) =>
{
    Console.WriteLine(reason switch
    {
        LoopExitReason.ConditionMet => $"✓ Approved (score: {state.Score})",
        LoopExitReason.MaxIterationsReached => $"⚠ Max iterations reached (score: {state.Score})",
        _ => "Unknown"
    });
})
```

---

## Iterative Refinement

The **Iterative Refinement** pattern is simpler: one agent refines output in a
self-loop until quality is sufficient.

```
refine → [good enough?] → end
  ↑           │ no
  └───────────┘
```

### Basic Usage

```csharp
var workflow = AgenticPattern.IterativeRefinement<DraftState>("polish")
    .WithAgent(refinementAgent)
    .Until(s => s.QualityScore >= 0.95)
    .MaxIterations(8)                // default: 10
    .Build();

var result = await workflow.RunAsync(new DraftState { Draft = initialDraft });
```

### When to Use Which

| Scenario | Pattern |
|---|---|
| Separate generator and evaluator agents | **Review & Critique** |
| Same agent produces and assesses output | **Iterative Refinement** |
| Need typed feedback flowing from critic to generator | **Review & Critique** |
| Simple "keep improving until good enough" | **Iterative Refinement** |

---

## Composing Patterns

The `Build()` method returns a standard `Workflow<TState>`. You can:

### Add checkpointing and tracing

```csharp
var workflow = AgenticPattern.ReviewCritique<ArticleState>("review")
    .WithGenerator(generator)
    .WithCritic(critic)
    .Until(s => s.Score >= 0.9)
    .Build()
    .UseCheckpointing(checkpointStore)
    .UseTracing(tracer)
    .WithMetadata(new() { ["team"] = "content-ops" });
```

### Embed as a sub-workflow

```csharp
var pipeline = new Workflow<PipelineState>("content-pipeline")
    .Job("gather", gatherJob)
    .SubFlow("review",
        AgenticPattern.ReviewCritique<ArticleState>("review")
            .WithGenerator(generator)
            .WithCritic(critic)
            .Until(s => s.Score >= 0.9)
            .Build(),
        mapIn: s => s.Article,
        mapOut: (s, article) => s with { Article = article })
    .Job("publish", publishJob)
    .Chain("gather", "review", "publish", Workflow.End);
```

### Stream events

```csharp
await foreach (var evt in workflow.StreamAsync(initialState, ct: ct))
{
    switch (evt)
    {
        case JobStarted<ArticleState> js:
            Console.WriteLine($"▶ {js.JobName}");
            break;
        case JobCompleted<ArticleState> jc:
            Console.WriteLine($"✓ {jc.JobName} ({jc.Duration.TotalMilliseconds:F0}ms)");
            break;
    }
}
```

---

## Dropping to the Primitive Layer

If the builder doesn't cover your exact variant, use `Workflow<TState>`
primitives directly. For example, a three-agent loop (generate → critique →
rewrite):

```csharp
var workflow = new Workflow<MyState>("custom-loop")
    .Job("generate", generateAgent)
    .Job("critique", critiqueAgent)
    .Job("rewrite", rewriteAgent)
    .Then("generate", "critique")
    .Then("critique", "rewrite")
    .Then("rewrite", Workflow.Decide<MyState>(s =>
        s.Score >= 0.9 ? Workflow.End : "critique"))
    .Build();
```

The `AgenticPattern` layer is an on-ramp, not a cage.

---

## See Also

- [Guide 02 — Workflows](02-workflows.md) — full workflow builder reference
- [Guide 03 — Agents](03-agents.md) — `AgentJob` for LLM-powered jobs
- [Guide 05 — Streaming Chat](05-streaming-chat.md) — `StreamingChatWorkflow` pattern
- [Guide 07 — Human-in-the-Loop](07-human-in-the-loop.md) — interrupts and resume
