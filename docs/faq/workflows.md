<!-- topic: faq-workflows, tags: faq, workflows, state-machine, routing, middleware, graph -->
# FAQ — Workflows & State Machine

← [Back to all FAQs](../faq.md)

---

## Workflows

### What is a workflow in Ananke?

A workflow is a directed graph of **jobs** connected by edges. Each job receives a typed
state object, performs work (optionally calling an LLM, tools, or external services), and
returns a new state. The graph is validated at build time — invalid topologies (disconnected
nodes, missing edges) fail before the workflow ever runs.

```csharp
var workflow = new Workflow<MyState>("my-workflow")
    .Job("step-a", async (state, ct) => state with { A = "done" })
    .Job("step-b", async (state, ct) => state with { B = "done" })
    .Chain("step-a", "step-b")
    .Then("step-b", Workflow.End);

var result = await workflow.RunAsync(new MyState());
```

### What routing patterns are supported?

| Pattern | API |
|---|---|
| Linear chain | `.Chain("a", "b", "c")` |
| Conditional branching | `.Decide(state => ...)` with a lambda returning the next job name |
| LLM-driven routing | `.DecideWithAgent(...)` — the model picks the next step |
| Fork / Join | Fan-out to parallel branches, fan-in with a merge function |
| Sub-workflows | `.SubFlow(innerWorkflow)` — nest a complete workflow inside a parent |
| Agentic patterns | `AgenticPattern.ReviewCritique<T>()`, `AgenticPattern.IterativeRefinement<T>()` |

### Can I stream workflow events in real time?

Yes. `workflow.BuildStream(initialState)` returns an `IAsyncEnumerable<WorkflowEvent>` that
you can forward over Server-Sent Events (SSE) to a web client. See
[Streaming Chat](../guides/05-streaming-chat.md) and [Workflows](../guides/02-workflows.md).

### Does Ananke validate the workflow graph at build time?

Yes. Calling `.Build()` (or running the workflow) validates the topology. Invalid configurations
— disconnected nodes, missing terminal edge, duplicate job names — throw at build time,
not at runtime.

### What are Agentic Patterns?

`AgenticPattern` is a higher-level builder that pre-wires recognized agentic design patterns on
top of the `Workflow<TState>` primitives:

- **Review & Critique** — generator agent → critic agent → loop until approved or max iterations
- **Iterative Refinement** — single agent refinement loop until quality threshold

More patterns (Map-Reduce, Swarm, etc.) will be added in future releases.

---

## State Machine

### What is the difference between a Workflow and a State Machine?

| | Workflow | State Machine |
|---|---|---|
| **Model** | Directed pipeline — runs start to finish | Long-lived entity — stable states + event-driven transitions |
| **Best for** | Task pipelines, document processing, batch jobs, agentic tasks | Conversation sessions, order lifecycle, device management, anything with ongoing status |
| **Trigger** | Started explicitly with `.RunAsync()` | Driven by external events via `.TransitionAsync()` |
| **Composition** | Can contain sub-workflows | Can invoke workflows as jobs via the Bridge layer |

Both compose: a state machine can invoke a `Workflow<T>` as part of a transition, and a
workflow can interact with a state machine.

### Does the state machine support distributed coordination?

Yes. `AbstractStateMachine` uses `IDistributedLock` to ensure safe concurrent transitions across
multiple service instances. In production, use `RedisDistributedLock` (via `Ananke.Redis`
with RedLock.net). In tests, use the zero-config `InMemoryDistributedLock`.

### What is the middleware pipeline?

`IJobMiddleware<T>` lets you intercept every state transition for logging, metrics, validation,
or custom business rules. Middleware chains compose cleanly and are applied in order.

### What is circuit breaking?

When `AbstractStateMachine.OperationalStatus` is set to `Faulted`, all transitions are blocked
until `ResetAsync()` is called. This prevents cascading failures in distributed systems.

---

← [Back to all FAQs](../faq.md) · [Feature Index](../reference/features.md) · [Getting Started](../guides/01-getting-started.md)
