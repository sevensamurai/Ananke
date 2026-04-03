# ADR-013: Implementation Plan — Agentic Design Pattern Readiness

| Field          | Value                                                              |
|----------------|---------------------------------------------------------------------|
| **Status**     | Proposed                                                            |
| **Date**       | 2025-07-24                                                          |
| **Relates to** | ADR-013 (agentic design pattern readiness)                          |

---

## Phase Overview

```
Phase 0 ─ Pattern Surface Layer             ┐  Cross-cutting: defines the
                                            │  discoverability strategy for
                                            ┘  all subsequent phases.
Phase 1 ─ Loop Primitive                    ┐
Phase 2 ─ Agent Middleware                  │  Each phase is independently
Phase 3 ─ Context Strategy                  ├  shippable and backward-compatible.
Phase 4 ─ Cost Tracking                     │  No phase depends on a prior phase
Phase 5 ─ Dynamic Agent Spawning            ┘  unless explicitly noted.
```

All new abstractions follow the project invariant: **every infrastructure
contract ships with an in-memory implementation suitable for unit testing.**

---

## Phase 0 — Pattern Surface Layer

**Goal:** Give agentic design patterns a dedicated, discoverable home in the API
so power users find them immediately and casual users can grow into them.

### The problem

If pattern support is spread across builder methods on `Workflow<TState>` —
`Loop()`, `MapReduce()`, etc. alongside `Job()`, `Then()`, `Fork()`, `Join()`,
`SubFlow()`, `Chain()` — the agentic features become invisible in IntelliSense.
A developer looking for "how do I implement the review-and-critique pattern"
won't know to look at `Workflow<T>.Loop()`. A developer browsing IntelliSense on
`Workflow<T>` sees 20+ methods and has no way to know which ones implement
recognized agentic design patterns.

These power users are the ones most likely to push the framework's boundaries
and provide architectural feedback. They need a curated surface.

### Design precedent

Ananke already has this pattern in three places:

| Entry point | What it packages | Returns |
|---|---|---|
| `StreamingChatWorkflow.Create(...)` | The streaming agent chat loop | `Workflow<StreamingChatState>` (via builder) |
| `Handoff.To(...)` | Correlated request-response handoff | `HandoffJob<TState,...>` (an `IJob<TState>`) |
| `Workflow.DecideWithAgent(...)` | LLM-driven routing | `AgentRouter<TState>` (via builder) |

Each is a **static entry point class** with a **nested fluent builder** that
produces a standard Ananke type. Users discover them by name, not by browsing
a mega-builder.

### Design: `AgenticPattern` static class

A single static class in the existing `Ananke.Orchestration` namespace serves
as the entry point for all named agentic patterns. Each pattern has its own
nested builder that produces a `Workflow<TState>`. The class is deliberately
singular (`AgenticPattern`, not `AgenticPatterns`) to read naturally:
`AgenticPattern.ReviewCritique(...)`, `AgenticPattern.IterativeRefinement(...)`.

```csharp
namespace Ananke.Orchestration;

/// <summary>
/// Entry point for named agentic design patterns. Each method returns a fluent
/// builder that produces a <see cref="Workflow{TState}"/> pre-wired for a
/// recognized pattern (loop, review-critique, iterative refinement, map-reduce,
/// etc.).
/// <para>
/// The returned <see cref="Workflow{TState}"/> can be further customized with
/// checkpointing, tracing, metadata, and additional jobs — or embedded as a
/// <see cref="Workflow{TState}.SubFlow{TChild}"/> inside a larger workflow.
/// </para>
/// </summary>
/// <remarks>
/// <para><b>Pattern catalog:</b></para>
/// <list type="table">
///   <listheader>
///     <term>Method</term>
///     <description>Agentic pattern (Google Cloud / industry name)</description>
///   </listheader>
///   <item>
///     <term><see cref="ReviewCritique{TState}"/></term>
///     <description>Review and Critique / Generator-Critic — generate → evaluate → revise loop</description>
///   </item>
///   <item>
///     <term><see cref="IterativeRefinement{TState}"/></term>
///     <description>Iterative Refinement — progressively improve output over multiple cycles</description>
///   </item>
///   <item>
///     <term><see cref="MapReduce{TState, TTask, TResult}"/></term>
///     <description>Hierarchical Task Decomposition / Factory — dynamic parallel agent spawning</description>
///   </item>
/// </list>
/// <para>
/// See also: <see cref="StreamingChatWorkflow"/> (streaming agent chat pattern)
/// and <see cref="Handoff"/> (agent-to-agent delegation pattern).
/// </para>
/// </remarks>
public static class AgenticPattern
{
    // Each method delegates to a nested Builder, documented below.
}
```

### Pattern 1: Review and Critique

```csharp
/// <summary>
/// Creates a builder for the <b>Review and Critique</b> pattern (also known as
/// Generator-Critic). A generator agent produces output, a critic agent evaluates
/// it against quality criteria, and the loop repeats until the critic approves or
/// the iteration cap is reached.
/// </summary>
/// <example>
/// <code>
/// var workflow = AgenticPattern.ReviewCritique&lt;ArticleState&gt;("draft-review")
///     .WithGenerator(generatorAgent)
///     .WithCritic(criticAgent)
///     .Until(s =&gt; s.ApprovalScore &gt;= 0.9)
///     .MaxIterations(5)
///     .Build();
///
/// var result = await workflow.RunAsync(initialState);
/// </code>
/// </example>
public static ReviewCritiqueBuilder<TState> ReviewCritique<TState>(string name)
```

The builder:

```csharp
public sealed class ReviewCritiqueBuilder<TState>
{
    // Required
    public ReviewCritiqueBuilder<TState> WithGenerator(IJob<TState> generator)
    public ReviewCritiqueBuilder<TState> WithCritic(IJob<TState> critic)
    public ReviewCritiqueBuilder<TState> Until(Func<TState, bool> predicate)

    // Optional
    public ReviewCritiqueBuilder<TState> MaxIterations(int max)        // default: 5
    public ReviewCritiqueBuilder<TState> OnLoopExit(Action<TState, LoopExitReason> handler)

    // Terminal
    public Workflow<TState> Build()
}
```

`Build()` produces:

```
generator → critic → [Loop: until predicate or max] → __end__
```

Internally it calls `Workflow<TState>.Job()`, `.Then()`, `.Loop()` — the same
primitives from Phase 1. The builder is sugar that names the pattern, validates
required parts, and wires them correctly.

### Pattern 2: Iterative Refinement

```csharp
/// <summary>
/// Creates a builder for the <b>Iterative Refinement</b> pattern. A single
/// agent refines its output over multiple cycles until a quality threshold is
/// met or the iteration cap is reached. Simpler than Review-Critique: one
/// agent plays both roles.
/// </summary>
public static IterativeRefinementBuilder<TState> IterativeRefinement<TState>(string name)
```

```csharp
public sealed class IterativeRefinementBuilder<TState>
{
    public IterativeRefinementBuilder<TState> WithAgent(IJob<TState> agent)
    public IterativeRefinementBuilder<TState> Until(Func<TState, bool> predicate)
    public IterativeRefinementBuilder<TState> MaxIterations(int max)      // default: 10
    public Workflow<TState> Build()
}
```

`Build()` produces:

```
refine → [Loop: until predicate or max] → __end__
```

### Pattern 3: Map-Reduce (Hierarchical Task Decomposition)

```csharp
/// <summary>
/// Creates a builder for the <b>Map-Reduce</b> pattern (also known as
/// Hierarchical Task Decomposition or Factory over Assistant). An agent
/// decomposes a task into N sub-tasks, each sub-task is executed in parallel
/// by a worker agent, and results are merged back.
/// </summary>
public static MapReduceBuilder<TState, TTask, TResult>
    MapReduce<TState, TTask, TResult>(string name)
    where TTask : class
    where TResult : class
```

```csharp
public sealed class MapReduceBuilder<TState, TTask, TResult>
    where TTask : class
    where TResult : class
{
    public MapReduceBuilder<...> Decompose(Func<TState, IReadOnlyList<TTask>> decompose)
    public MapReduceBuilder<...> Execute(Func<TTask, (IAgentModel, AgentRequest)> factory)
    public MapReduceBuilder<...> Reduce(Func<TState, IReadOnlyList<TResult>, TState> merge)
    public MapReduceBuilder<...> MaxConcurrency(int max)
    public MapReduceBuilder<...> MaxTasks(int max)                       // default: 20
    public Workflow<TState> Build()
}
```

### Why builders instead of one-shot factory methods

| Concern | One-shot factory | Fluent builder |
|---|---|---|
| Parameter count | 5–8 params → hard to read | Named methods → self-documenting |
| Optional configuration | Default-parameter explosion | Only set what you need |
| Validation | At runtime, buried in constructor | `Build()` validates all required parts, clear error messages |
| Discoverability | Params discovered by hovering | IntelliSense shows each step as you type |
| Extensibility | New param = breaking change | New method = additive |

### Why a single entry point class (not per-pattern classes)

A developer who types `AgenticPattern.` in their IDE immediately sees:

```
AgenticPattern.
  ├── ReviewCritique<TState>(...)
  ├── IterativeRefinement<TState>(...)
  ├── MapReduce<TState, TTask, TResult>(...)
  └── (future patterns appear here)
```

This is the "catalog moment" — the IntelliSense equivalent of a table of
contents. Compare with scattering the same methods across `Workflow<T>` where
they'd be interleaved with `Job()`, `Then()`, `Fork()`, etc.

### Relationship to low-level primitives

The `AgenticPattern` builders are **sugar, not magic**. Each builder calls
the same low-level primitives that power users can also use directly:

```
┌──────────────────────────────────────────────────────────────┐
│  AgenticPattern.ReviewCritique(...)                          │  ← Pattern layer
│  AgenticPattern.IterativeRefinement(...)                     │    (named, guided,
│  AgenticPattern.MapReduce(...)                               │     validated)
└──────────────────────┬───────────────────────────────────────┘
                       │ calls
┌──────────────────────▼───────────────────────────────────────┐
│  Workflow<T>.Job() / .Then() / .Loop() / .MapReduce()       │  ← Primitive layer
│  Workflow<T>.Fork() / .Join() / .SubFlow()                  │    (composable,
│  Workflow.Decide() / .DecideWithAgent()                     │     unrestricted)
└──────────────────────────────────────────────────────────────┘
```

Developers who need a pattern variant not covered by a builder can always
drop down to the primitive layer. The pattern layer exists for discoverability
and correctness-by-construction, not to limit composition.

### Customization: the returned `Workflow<TState>` is open

`Build()` returns a `Workflow<TState>`, not a sealed opaque type. Developers
can add checkpointing, tracing, metadata, extra jobs, and additional edges:

```csharp
var workflow = AgenticPattern.ReviewCritique<ArticleState>("draft-review")
    .WithGenerator(generatorAgent)
    .WithCritic(criticAgent)
    .Until(s => s.Score >= 0.9)
    .Build()
    // Standard workflow customizations:
    .UseCheckpointing(checkpointStore)
    .UseTracing(tracer)
    .WithMetadata("team", "content-ops");
```

Or embed as a sub-workflow:

```csharp
var outerWorkflow = new Workflow<PipelineState>("content-pipeline")
    .Job("gather", gatherJob)
    .SubFlow("review",
        AgenticPattern.ReviewCritique<ArticleState>("draft-review")
            .WithGenerator(generatorAgent)
            .WithCritic(criticAgent)
            .Until(s => s.Score >= 0.9)
            .Build(),
        mapIn: s => s.Article,
        mapOut: (s, article) => s with { Article = article })
    .Job("publish", publishJob)
    .Chain("gather", "review", "publish", Workflow.End);
```

### Documentation structure

The single entry point enables a focused documentation path:

```
docs/
  guides/
    16-agentic-patterns.md          ← New guide: pattern catalog + when to use each
  reference/
    agentic-patterns-reference.md   ← API reference for AgenticPattern class
demos/
    ReviewCritiqueDemo/             ← Self-contained demo: generator + critic loop
```

Guide 16 becomes the "landing page" for developers asking "how do I implement
X pattern in Ananke?" — a single document that maps Google Cloud / industry
pattern names to Ananke's `AgenticPattern.*` methods, with runnable examples
and links to the corresponding demo project.

### Files changed

| File | Change |
|---|---|
| `Ananke.Orchestration/AgenticPattern.cs` | New file — static class + nested builders |
| `Ananke.Orchestration/Patterns/ReviewCritiqueBuilder.cs` | New file |
| `Ananke.Orchestration/Patterns/IterativeRefinementBuilder.cs` | New file |
| `Ananke.Orchestration/Patterns/MapReduceBuilder.cs` | New file |
| `docs/guides/16-agentic-patterns.md` | New guide |
| `docs/reference/agentic-patterns-reference.md` | New reference doc |
| `demos/ReviewCritiqueDemo/` | New demo project |
| `tests/Ananke.Orchestration.Tests/Patterns/` | New test directory |

---

## Phase 1 — Loop Primitive

**Goal:** First-class loop/cycle support in the workflow builder with built-in
termination guards, iteration tracking, and diagram visibility.

### 1.1 New types

#### `LoopConnection` (in `Ananke.Orchestration/Routing/Connections.cs`)

```csharp
/// <summary>
/// A connection that cycles execution from <see cref="Connection.From"/> back to
/// <paramref name="LoopTarget"/> until <paramref name="Until"/> returns <c>true</c>
/// or <paramref name="MaxIterations"/> is reached.
/// </summary>
public sealed record LoopConnection<TState> : Connection
{
    /// <summary>Job to loop back to when the condition is not met.</summary>
    public required string LoopTarget { get; init; }

    /// <summary>Job to continue to when the loop exits.</summary>
    public required string ExitTarget { get; init; }

    /// <summary>Termination predicate evaluated after <see cref="Connection.From"/> completes.</summary>
    public required Func<TState, bool> Until { get; init; }

    /// <summary>Maximum iterations before forced exit. Prevents infinite loops.</summary>
    public required int MaxIterations { get; init; }
}
```

#### `LoopExitReason` (new file in `Ananke.Orchestration/Routing/`)

```csharp
/// <summary>
/// Indicates why a loop terminated.
/// </summary>
public enum LoopExitReason
{
    /// <summary>The <c>Until</c> predicate returned <c>true</c>.</summary>
    ConditionMet,

    /// <summary>The maximum iteration count was reached.</summary>
    MaxIterationsReached
}
```

### 1.2 Builder API (in `Workflow<TState>`)

```csharp
/// <summary>
/// Creates a loop that cycles from <paramref name="from"/> back to
/// <paramref name="loopTarget"/> until <paramref name="until"/> returns <c>true</c>,
/// then continues to <paramref name="exitTarget"/>.
/// </summary>
/// <param name="from">The evaluation job — its output state is tested each iteration.</param>
/// <param name="loopTarget">The job to restart when the condition is not met.</param>
/// <param name="exitTarget">The job to continue to when the loop exits (or <see cref="Workflow.End"/>).</param>
/// <param name="until">Predicate evaluated after <paramref name="from"/> completes.</param>
/// <param name="maxIterations">Safety cap. Default 10.</param>
public Workflow<TState> Loop(
    string from,
    string loopTarget,
    string exitTarget,
    Func<TState, bool> until,
    int maxIterations = 10)
```

**Usage — Review and Critique pattern:**

```csharp
var workflow = new Workflow<ReviewState>("review-critique")
    .Job("generate", generatorAgent)
    .Job("critique", criticAgent)
    .Job("publish", publishJob)
    .Then("generate", "critique")
    .Loop("critique", loopTarget: "generate", exitTarget: "publish",
          until: s => s.ApprovalScore >= 0.9, maxIterations: 5)
    .Then("publish", Workflow.End);
```

**Usage — Iterative Refinement pattern:**

```csharp
var workflow = new Workflow<DraftState>("refine")
    .Job("draft", draftAgent)
    .Job("evaluate", evalAgent)
    .Loop("evaluate", loopTarget: "draft", exitTarget: Workflow.End,
          until: s => s.QualityScore >= threshold, maxIterations: 8);
```

### 1.3 Runner changes (in `WorkflowRunner`)

The `ExecuteAsync` loop already supports arbitrary next-job resolution. Changes:

1. **Resolve loop connections:** After a job completes, check for `LoopConnection<TState>`.
   If found, evaluate the `Until` predicate. If false and under max iterations, route
   back to `LoopTarget`. If true or max reached, route to `ExitTarget`.

2. **Iteration tracking:** Maintain a `Dictionary<string, int>` of loop counters per
   `LoopConnection.From` key in the execution context. Increment on each cycle.
   Reset when execution exits the loop.

3. **Exit reason:** Store the `LoopExitReason` in `WorkflowExecution.Metadata` under
   key `loop:{from}:exit_reason`. Emit a `LoopExited` event in the stream.

4. **Tracing:** Each loop iteration gets a span attribute `loop.iteration = N`.

### 1.4 Validation changes (in `WorkflowDefinition.Validate()`)

- `LoopConnection` targets (`LoopTarget`, `ExitTarget`) must reference defined jobs
  or `Workflow.End`
- `MaxIterations` must be ≥ 1
- The loop source (`From`) must have a defined job
- Loop connections are **exempt** from the "every job must have an outgoing connection"
  check (the loop itself provides the outgoing path)

### 1.5 Diagram support (in `WorkflowDiagramExtensions`)

Mermaid output for loop connections uses a distinctive edge style:

```mermaid
critique -->|"loop (max 5)"| generate
critique -->|"exit"| publish
```

### 1.6 Stream event

```csharp
/// <summary>Emitted when a loop terminates.</summary>
public sealed record LoopExited<TState> : WorkflowEvent<TState>
{
    public required string LoopFrom { get; init; }
    public required string LoopTarget { get; init; }
    public required int IterationsCompleted { get; init; }
    public required LoopExitReason Reason { get; init; }
}
```

### 1.7 Test plan

| Test | Validates |
|---|---|
| Loop exits when `Until` returns true | Condition-based termination |
| Loop exits at `MaxIterations` when condition never met | Safety cap |
| Loop counter resets on re-entry (nested loops) | Counter isolation |
| `LoopExited` event emitted with correct reason | Observability |
| Mermaid output includes loop annotation | Diagram fidelity |
| Build-time validation rejects `MaxIterations < 1` | Input validation |
| Build-time validation rejects undefined `LoopTarget` | Topology validation |
| Checkpoint/resume preserves loop counter | Persistence |

### 1.8 Files changed

| File | Change |
|---|---|
| `Ananke.Orchestration/Routing/Connections.cs` | Add `LoopConnection<TState>` |
| `Ananke.Orchestration/Routing/LoopExitReason.cs` | New file |
| `Ananke.Orchestration/Workflow.cs` | Add `Loop()` builder method |
| `Ananke.Orchestration/WorkflowDefinition.cs` | Store loops, resolve in `ResolveLoop()`, update `Validate()` |
| `Ananke.Orchestration/Execution/WorkflowRunner.cs` | Loop resolution in `ExecuteAsync`, iteration counter, `LoopExited` event |
| `Ananke.Orchestration/Streaming/WorkflowEvent.cs` | Add `LoopExited<TState>` |
| `Ananke.Orchestration/Checkpointing/Checkpoint.cs` | Include loop counters |
| `Ananke.Design/WorkflowDiagramExtensions.cs` | Render loop edges |
| `tests/Ananke.Orchestration.Tests/` | New test class `LoopTests.cs` |

---

## Phase 2 — Agent Model Middleware

**Goal:** A composable middleware pipeline around individual LLM calls, enabling
PII redaction, prompt injection detection, guardrails, and per-call observability
without modifying provider implementations.

### 2.1 New types

#### `IAgentModelMiddleware` (new file in `Ananke.Orchestration/Agents/`)

```csharp
/// <summary>
/// Intercepts individual LLM calls within any <see cref="IAgentModel"/> implementation.
/// Middlewares are invoked in registration order around each <see cref="IAgentModel.GenerateAsync"/>
/// and <see cref="IStreamingAgentModel.GenerateStreamAsync"/> call.
/// </summary>
public interface IAgentModelMiddleware
{
    /// <summary>
    /// Called before the request is sent to the model. Return a modified request
    /// to transform what the model sees (e.g., redact PII, inject system guardrails).
    /// </summary>
    Task<AgentRequest> OnBeforeGenerateAsync(
        AgentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Called after the model returns a complete response. Return a modified response
    /// to transform what the caller sees (e.g., filter unsafe content, validate structure).
    /// </summary>
    Task<AgentResponse> OnAfterGenerateAsync(
        AgentResponse response, AgentRequest request, CancellationToken ct = default);
}
```

#### `MiddlewareAgentModel` (decorator in `Ananke.Orchestration/Agents/`)

```csharp
/// <summary>
/// Decorator that applies a pipeline of <see cref="IAgentModelMiddleware"/> instances
/// around any <see cref="IStreamingAgentModel"/>. Composes with <see cref="ResilientAgentModel"/>
/// and <see cref="CachingAgentModel"/>.
/// </summary>
public sealed class MiddlewareAgentModel : IStreamingAgentModel
{
    private readonly IStreamingAgentModel _inner;
    private readonly IReadOnlyList<IAgentModelMiddleware> _middlewares;

    public MiddlewareAgentModel(
        IStreamingAgentModel inner,
        IEnumerable<IAgentModelMiddleware> middlewares) { ... }

    public static MiddlewareAgentModel Wrap(
        IStreamingAgentModel inner,
        params IAgentModelMiddleware[] middlewares) { ... }
}
```

### 2.2 Composition order

```
User code
  → MiddlewareAgentModel (PII redaction, guardrails, logging)
    → ResilientAgentModel (429 retry)
      → CachingAgentModel (response cache)
        → OpenAIAgentModel / AnthropicAgentModel / GoogleAgentModel
```

Each layer is optional and independently composable.

### 2.3 Streaming considerations

For `GenerateStreamAsync`:
- `OnBeforeGenerateAsync` runs before the stream starts (transforms the request)
- `OnAfterGenerateAsync` runs after the stream completes (transforms the final
  `AgentResponse` carried by the last chunk)
- Individual stream chunks are **not** intercepted — this preserves streaming
  latency. If per-chunk interception is needed later, a separate
  `OnStreamChunkAsync` hook can be added.

### 2.4 Built-in middleware implementations

Ship with the core package:

| Middleware | Purpose |
|---|---|
| `LoggingAgentModelMiddleware` | Logs request/response metadata (token count, latency, tool calls) via `ILogger` |
| `GuardrailAgentModelMiddleware` | Rejects responses matching configurable deny patterns (regex or delegate) |

Additional implementations (PII, prompt injection) can be added later or provided
by consumers.

### 2.5 Test plan

| Test | Validates |
|---|---|
| Single middleware transforms request | `OnBeforeGenerateAsync` |
| Single middleware transforms response | `OnAfterGenerateAsync` |
| Pipeline of 3 middlewares executes in order | Composition |
| Middleware exception propagates to caller | Error handling |
| Streaming: request transformed, response transformed, chunks unmodified | Stream integrity |
| `GuardrailAgentModelMiddleware` rejects matching response | Safety |
| Composition with `ResilientAgentModel` — retry triggers middleware for each attempt | Decorator ordering |

### 2.6 Files changed

| File | Change |
|---|---|
| `Ananke.Orchestration/Agents/IAgentModelMiddleware.cs` | New file |
| `Ananke.Orchestration/Agents/MiddlewareAgentModel.cs` | New file |
| `Ananke.Orchestration/Agents/LoggingAgentModelMiddleware.cs` | New file |
| `Ananke.Orchestration/Agents/GuardrailAgentModelMiddleware.cs` | New file |
| `tests/Ananke.Orchestration.Tests/AgentModelMiddlewareTests.cs` | New test class |

---

## Phase 3 — Context Strategy

**Goal:** Pluggable context window management so agents don't silently fail when
conversation history exceeds model token limits.

### 3.1 New types

#### `IContextStrategy` (new file in `Ananke.Orchestration/Agents/`)

```csharp
/// <summary>
/// Controls how conversation history is managed before being sent to the model.
/// Applied by <see cref="AgentJob"/> and <see cref="StreamingChatWorkflow"/> when
/// the message list may exceed the model's context window.
/// </summary>
public interface IContextStrategy
{
    /// <summary>
    /// Filters, compacts, or summarizes the message list to fit within constraints.
    /// The system prompt (if any) is passed separately so implementations can account
    /// for its token cost. Returns the (possibly shorter) message list to send.
    /// </summary>
    Task<IReadOnlyList<AgentMessage>> ApplyAsync(
        IReadOnlyList<AgentMessage> messages,
        string? systemPrompt,
        CancellationToken ct = default);
}
```

#### `ITokenCounter` (new file in `Ananke.Orchestration/Agents/`)

```csharp
/// <summary>
/// Estimates the token count for text content. Used by context strategies
/// to determine when compaction is needed.
/// </summary>
public interface ITokenCounter
{
    /// <summary>Estimates the token count for a single text string.</summary>
    int EstimateTokens(string text);

    /// <summary>Estimates the total token count for a message (all content parts).</summary>
    int EstimateTokens(AgentMessage message);
}
```

### 3.2 Built-in implementations

| Implementation | Behavior |
|---|---|
| `SlidingWindowContextStrategy` | Keeps the most recent N messages (or N tokens). Drops oldest messages first, always preserving the last user message. |
| `SummarizingContextStrategy` | When history exceeds threshold, summarizes older messages via an LLM call and replaces them with a single summary message. Requires an `IAgentModel` for summarization. |
| `ApproximateTokenCounter` | Estimates tokens using the `chars / 4` heuristic. Suitable for most use cases without a tokenizer dependency. |

### 3.3 Integration points

#### `AgentJob.Builder`

```csharp
/// <summary>
/// Sets the context strategy applied before each LLM call.
/// When set, the message history is passed through the strategy before
/// building the <see cref="AgentRequest"/>.
/// </summary>
public Builder WithContextStrategy(IContextStrategy strategy)
```

This also activates the currently unused `_maxContextTokens` field — pass it to
the strategy as the budget.

#### `StreamingChatWorkflow.Builder`

```csharp
/// <summary>
/// Sets the context strategy applied before each agent generation round.
/// </summary>
public Builder WithContextStrategy(IContextStrategy strategy)
```

### 3.4 Test plan

| Test | Validates |
|---|---|
| `SlidingWindowContextStrategy` drops oldest messages when over limit | Token budget enforcement |
| `SlidingWindowContextStrategy` preserves system prompt and last user message | Correctness |
| `SummarizingContextStrategy` calls model to summarize old history | LLM-based compaction |
| `SummarizingContextStrategy` passes through when under threshold | No-op path |
| `ApproximateTokenCounter` returns reasonable estimates | Token counting |
| `AgentJob` with strategy applies compaction before LLM call | Integration |
| `StreamingChatWorkflow` with strategy applies compaction | Integration |
| Strategy receives correct system prompt for budget calculation | Parameter passing |

### 3.5 Files changed

| File | Change |
|---|---|
| `Ananke.Orchestration/Agents/IContextStrategy.cs` | New file |
| `Ananke.Orchestration/Agents/ITokenCounter.cs` | New file |
| `Ananke.Orchestration/Agents/SlidingWindowContextStrategy.cs` | New file |
| `Ananke.Orchestration/Agents/SummarizingContextStrategy.cs` | New file |
| `Ananke.Orchestration/Agents/ApproximateTokenCounter.cs` | New file |
| `Ananke.Orchestration/Agents/AgentJob.cs` | Wire `IContextStrategy` in `ExecuteAsync` |
| `Ananke.Orchestration/Agents/StreamingChatWorkflow.cs` | Wire `IContextStrategy` in agent job lambda |
| `tests/Ananke.Orchestration.Tests/ContextStrategyTests.cs` | New test class |

---

## Phase 4 — Cost Tracking and Budget Enforcement

**Goal:** Track cumulative LLM token usage across a workflow execution and
optionally enforce a cost budget.

### 4.1 New types

#### `TokenUsage` (new file in `Ananke.Orchestration/Agents/`)

```csharp
/// <summary>
/// Token consumption metadata returned by an LLM call.
/// </summary>
public sealed record TokenUsage
{
    /// <summary>Number of input/prompt tokens consumed.</summary>
    public int InputTokens { get; init; }

    /// <summary>Number of output/completion tokens generated.</summary>
    public int OutputTokens { get; init; }

    /// <summary>Total tokens (input + output).</summary>
    public int TotalTokens => InputTokens + OutputTokens;
}
```

### 4.2 Changes to existing types

#### `AgentResponse` — add optional `Usage`

```csharp
// Add to AgentResponse:
/// <summary>Token usage for this LLM call, if reported by the provider.</summary>
public TokenUsage? Usage { get; init; }
```

Each provider (`OpenAIAgentModel`, `AnthropicAgentModel`, `GoogleAgentModel`) maps
the provider-specific usage object to `TokenUsage` when available. This is additive
and non-breaking — `Usage` is nullable.

#### `WorkflowExecution<TState>` — add cost accumulation

```csharp
// Add to WorkflowExecution<TState>:
/// <summary>Cumulative token usage across all LLM calls in this execution.</summary>
public TokenUsage CumulativeUsage { get; internal set; } = new();

/// <summary>Estimated cumulative cost in the unit defined by the workflow's cost model.</summary>
public decimal EstimatedCost { get; internal set; }
```

### 4.3 Budget enforcement

#### `Workflow<TState>.WithBudget()`

```csharp
/// <summary>
/// Sets a cost budget for the workflow. If cumulative estimated cost exceeds
/// <paramref name="maxCost"/>, the workflow terminates with
/// <see cref="ExecutionStatus.BudgetExceeded"/>.
/// </summary>
/// <param name="maxCost">Maximum allowed estimated cost.</param>
/// <param name="costPer1KInputTokens">Cost per 1,000 input tokens.</param>
/// <param name="costPer1KOutputTokens">Cost per 1,000 output tokens.</param>
public Workflow<TState> WithBudget(
    decimal maxCost,
    decimal costPer1KInputTokens,
    decimal costPer1KOutputTokens)
```

**Enforcement point:** After each job completes in `WorkflowRunner.ExecuteAsync`,
if a budget is configured, accumulate usage from any `AgentResponse.Usage` captured
during that job's execution. If `EstimatedCost > maxCost`, set status to
`BudgetExceeded` and stop.

#### New execution status

```csharp
// Add to ExecutionStatus enum:
/// <summary>The workflow was terminated because its cost budget was exceeded.</summary>
BudgetExceeded
```

### 4.4 Stream event

```csharp
/// <summary>Emitted when the workflow terminates due to budget exhaustion.</summary>
public sealed record BudgetExceeded<TState> : WorkflowEvent<TState>
{
    public required decimal EstimatedCost { get; init; }
    public required decimal Budget { get; init; }
    public required TokenUsage CumulativeUsage { get; init; }
}
```

### 4.5 Token usage plumbing

The `AgentJob` tool loop and `StreamingChatWorkflow` agent job already receive
`AgentResponse`. The implementation must capture `Usage` from each response and
make it available to the runner. Options:

- **Option A (chosen):** `AgentJob.ExecuteAsync` stores usage in an
  `AsyncLocal<TokenUsage>` that the runner reads after job completion.
- **Option B:** Return usage as part of `TState` — rejected because it pollutes
  the user's state type.

### 4.6 Test plan

| Test | Validates |
|---|---|
| `TokenUsage` accumulates across multiple jobs | Cumulative tracking |
| Workflow terminates with `BudgetExceeded` when cost exceeds limit | Budget enforcement |
| `BudgetExceeded` event emitted in stream | Observability |
| No budget configured — workflow runs without cost tracking | Opt-in behavior |
| Provider returns null usage — no crash, just skipped | Graceful degradation |
| Checkpoint includes cumulative usage | Persistence |

### 4.7 Files changed

| File | Change |
|---|---|
| `Ananke.Orchestration/Agents/TokenUsage.cs` | New file |
| `Ananke.Orchestration/Agents/AgentResponse.cs` | Add `Usage` property |
| `Ananke.Orchestration/WorkflowExecution.cs` | Add `CumulativeUsage`, `EstimatedCost` |
| `Ananke.Orchestration/ExecutionStatus.cs` | Add `BudgetExceeded` |
| `Ananke.Orchestration/Workflow.cs` | Add `WithBudget()` |
| `Ananke.Orchestration/WorkflowDefinition.cs` | Store budget config |
| `Ananke.Orchestration/Execution/WorkflowRunner.cs` | Post-job cost accumulation and enforcement |
| `Ananke.Orchestration/Streaming/WorkflowEvent.cs` | Add `BudgetExceeded<TState>` |
| `Ananke.Orchestration/Agents/AgentJob.cs` | Expose usage via `AsyncLocal` |
| `Ananke.Orchestration.OpenAI/...` | Map provider usage → `TokenUsage` |
| `Ananke.Orchestration.Anthropic/...` | Map provider usage → `TokenUsage` |
| `Ananke.Orchestration.Google/...` | Map provider usage → `TokenUsage` |
| `tests/Ananke.Orchestration.Tests/BudgetTests.cs` | New test class |

---

## Phase 5 — Dynamic Agent Spawning (Map-Reduce)

**Goal:** Enable agents to dynamically decompose a task into N parallel sub-tasks
at runtime, execute them concurrently, and merge results — without requiring the
topology to be fixed at build time.

### 5.1 New types

#### `MapReduceJob<TState, TTask, TResult>` (new file in `Ananke.Orchestration/Jobs/`)

```csharp
/// <summary>
/// A job that dynamically spawns N parallel agent executions at runtime.
/// <list type="number">
///   <item><b>Map:</b> Decompose the current state into a list of sub-tasks.</item>
///   <item><b>Execute:</b> Run each sub-task through an agent concurrently.</item>
///   <item><b>Reduce:</b> Merge all results back into a single state.</item>
/// </list>
/// </summary>
/// <remarks>
/// Unlike <see cref="ForkConnection"/> (static topology), this job determines
/// the number and content of parallel tasks at runtime based on the current state.
/// Implements the "Factory over Assistant" and "LLM Map-Reduce" patterns.
/// </remarks>
public sealed class MapReduceJob<TState, TTask, TResult> : IJob<TState>
    where TTask : class
    where TResult : class
{
    public string Name { get; }

    /// <param name="name">Job name for traces and logs.</param>
    /// <param name="decompose">Extracts the list of sub-tasks from current state.</param>
    /// <param name="agentFactory">Creates an agent model + prompt for each sub-task.</param>
    /// <param name="reduce">Merges all sub-task results back into the workflow state.</param>
    /// <param name="maxConcurrency">Maximum parallel executions. Default: unbounded.</param>
    /// <param name="maxTasks">Safety cap on number of sub-tasks. Default: 20.</param>
    public MapReduceJob(
        string name,
        Func<TState, IReadOnlyList<TTask>> decompose,
        Func<TTask, (IAgentModel Model, AgentRequest Request)> agentFactory,
        Func<TState, IReadOnlyList<TResult>, TState> reduce,
        int? maxConcurrency = null,
        int maxTasks = 20) { ... }

    public async Task<TState> ExecuteAsync(TState state, CancellationToken ct)
    {
        var tasks = decompose(state);

        if (tasks.Count > maxTasks)
            throw new InvalidOperationException(
                $"MapReduceJob '{Name}' produced {tasks.Count} sub-tasks, " +
                $"exceeding the safety cap of {maxTasks}.");

        // Execute all sub-tasks with optional concurrency limit
        var results = await ExecuteParallelAsync(tasks, ct);

        return reduce(state, results);
    }
}
```

### 5.2 Builder convenience

```csharp
// On Workflow<TState>:
/// <summary>
/// Adds a map-reduce job that dynamically spawns parallel agent executions at runtime.
/// </summary>
public Workflow<TState> MapReduce<TTask, TResult>(
    string name,
    Func<TState, IReadOnlyList<TTask>> decompose,
    Func<TTask, (IAgentModel Model, AgentRequest Request)> agentFactory,
    Func<TState, IReadOnlyList<TResult>, TState> reduce,
    int? maxConcurrency = null,
    int maxTasks = 20)
    where TTask : class
    where TResult : class
```

### 5.3 Usage example — Hierarchical task decomposition

```csharp
var workflow = new Workflow<ResearchState>("research")
    .Job("plan", plannerAgent)       // Agent decomposes research question into sub-questions
    .MapReduce<SubQuestion, SubAnswer>("research",
        decompose: s => s.SubQuestions,       // planner populated these
        agentFactory: q => (researchModel, new AgentRequest
        {
            SystemPrompt = "You are a research assistant.",
            Messages = [AgentMessage.User(q.Text)],
            Tools = [searchTool]
        }),
        reduce: (s, answers) => s with { Findings = answers.ToList() },
        maxConcurrency: 4,
        maxTasks: 10)
    .Job("synthesize", synthesisAgent)  // Combines findings into final report
    .Then("plan", "research")
    .Then("research", "synthesize")
    .Then("synthesize", Workflow.End);
```

### 5.4 Safety constraints

| Constraint | Default | Rationale |
|---|---|---|
| `maxTasks` | 20 | Prevents decomposition that produces unbounded work |
| `maxConcurrency` | null (unbounded) | Controls resource usage; set to limit parallel LLM calls |
| Cancellation | Respected per-task | Individual task failure behavior mirrors `ForkMode.FailFast` |

### 5.5 Test plan

| Test | Validates |
|---|---|
| Decompose returns 3 tasks → 3 parallel executions → reduce merges | Happy path |
| Decompose returns 0 tasks → reduce receives empty list | Edge case |
| Decompose exceeds `maxTasks` → throws | Safety cap |
| `maxConcurrency = 2` → at most 2 concurrent executions | Concurrency limit |
| One sub-task fails → entire job fails (FailFast) | Error propagation |
| Cancellation token respected mid-execution | Cleanup |
| Tracing spans emitted per sub-task | Observability |

### 5.6 Files changed

| File | Change |
|---|---|
| `Ananke.Orchestration/Jobs/MapReduceJob.cs` | New file |
| `Ananke.Orchestration/Workflow.cs` | Add `MapReduce()` convenience method |
| `tests/Ananke.Orchestration.Tests/MapReduceJobTests.cs` | New test class |

---

## Cross-Cutting Concerns

### Documentation

Each phase includes:
- XML doc comments on all public types (existing project convention)
- Update to `docs/reference/features.md` capability table
- Update to `docs/about/framework-comparison.md` — mark newly covered patterns

Phase 0 establishes the documentation home for all patterns:

| Artifact | Purpose |
|---|---|
| `docs/guides/16-agentic-patterns.md` | The landing page — maps industry pattern names to `AgenticPattern.*` methods, with "when to use" guidance and runnable code snippets |
| `docs/reference/agentic-patterns-reference.md` | Full API reference for `AgenticPattern` class and all builders |
| `demos/ReviewCritiqueDemo/` | Self-contained demo: generator + critic loop with streaming output |

Subsequent phases add sections to Guide 16 as each pattern ships. The guide
grows incrementally but is always complete for what's available.

### Versioning

- Phase 0 ships with Phase 1 (they are co-dependent — the pattern layer needs
  `Loop()` to wire `ReviewCritique` and `IterativeRefinement`)
- Phases 2–3 target the same or next minor version (additive, non-breaking)
- Phase 4 touches provider packages (OpenAI, Anthropic, Google) — may warrant
  a coordinated minor bump
- Phase 5 is additive and can ship independently

### Migration

No migration required. All changes are additive:
- New builder methods alongside existing ones
- New static class (`AgenticPattern`) in existing namespace — no new `using` required
- New optional properties with default values
- New event types in the stream (consumers that don't handle them are unaffected)
- Existing demos and tests continue to pass without modification

---

## Summary Matrix

| Phase | Key Deliverable | New Interfaces | Patterns Unlocked | Breaking? |
|---|---|---|---|---|
| 0 | `AgenticPattern` static class + per-pattern builders | 0 | Discoverability for all patterns; ReviewCritique, IterativeRefinement builders | No |
| 1 | `Loop()` builder + `LoopConnection` | 0 (types only) | Loop, Review-Critique, Iterative Refinement, Reflection | No |
| 2 | `IAgentModelMiddleware` + `MiddlewareAgentModel` | 1 | PII Tokenization, Prompt Injection Defense, Guardrails | No |
| 3 | `IContextStrategy` + `ITokenCounter` | 2 | Context Compaction, Semantic Filtering, Token Management | No |
| 4 | `TokenUsage` + `WithBudget()` | 0 (types only) | Budget-Aware Routing, Cost-Capped Execution | No |
| 5 | `MapReduceJob` | 0 (class only) | Hierarchical Decomposition, Factory, Swarm Migration, Map-Reduce | No |

---

## Appendix: Developer Journey

The two-layer design serves different users at different stages:

```
                        Casual user                    Power user
                        ──────────                     ──────────

Discovery       "How do I make an agent?"       "How do I implement the
                                                 review-and-critique pattern?"
                         │                                │
                         ▼                                ▼
Entry point     Guide 03 – Agents                Guide 16 – Agentic Patterns
                StreamingChatWorkflow.Create()    AgenticPattern.ReviewCritique()
                         │                                │
                         ▼                                ▼
Customization   .WithTools() / .OnTextDelta()    .Until() / .MaxIterations()
                         │                                │
                         ▼                                ▼
Advanced        Workflow<T> builder               Workflow<T> builder
                (when they outgrow the              (returned by Build(),
                 pre-built pattern)                  open for composition)
```

Both paths converge on `Workflow<TState>` — the pattern layer is an on-ramp,
not a cage.
