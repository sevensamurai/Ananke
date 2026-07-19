<!-- topic: faq-operations, tags: faq, testing, observability, opentelemetry, architecture, design, orleans -->
# FAQ — Testing, Observability & Design

← [Back to all FAQs](../faq.md)

---

## Testing

### Can I test agents without calling a real LLM or any external service?

Yes. Every infrastructure contract ships with a zero-config in-memory implementation:

| Contract | In-memory implementation |
|---|---|
| `IDistributedLock` | `InMemoryDistributedLock` |
| `IKnowledgeStore` | `InMemoryKnowledgeStore` |
| `IEmpiricalMemory` | `InMemoryEmpiricalMemory` |
| `ICheckpointStore` | `InMemoryCheckpointStore` |
| `IHandoffChannel` | `InMemoryHandoffChannel` |
| `IConversationMemory` | `InMemoryConversationMemory` |

For LLM calls, implement `IStreamingAgentModel` with a stub that returns deterministic
responses. Integration tests run in milliseconds with no API keys and no network access.

### How do I write integration tests for a workflow?

```csharp
// Use in-memory infrastructure
var store      = new InMemoryKnowledgeStore(embeddingDimension: 1536);
var checkpoint = new InMemoryCheckpointStore();
var locker     = new InMemoryDistributedLock();

// Build and run the workflow
var result = await workflow.RunAsync(initialState);

// Assert on typed state and status
result.Status.ShouldBe(ExecutionStatus.Completed);
result.State.Output.ShouldNotBeNullOrEmpty();
```

See [Testing](../guides/14-testing.md) for patterns and examples.

---

## Observability

### Does Ananke support OpenTelemetry?

Yes. `Ananke.OpenTelemetry` provides one-liner OTLP export. The following are automatically
instrumented:

- Workflow job start and end spans with state transition metadata
- State machine transition spans with guard condition results
- LLM retry events (`llm.rate_limit_retry`) with attempt count and delay on the active span
- Tool execution spans with `output_length` and `tool.error` attributes

Compatible with Jaeger, Grafana Tempo, BetterStack, and any OTLP-compatible backend.

### How do I enable tracing?

```bash
dotnet add package Ananke.OpenTelemetry
```

```csharp
services.AddTracingPipeline(o =>
{
    o.ServiceName = "my-service";
    o.UseOtlp("http://localhost:4317");
});
```

See [Observability](../guides/10-observability.md).

---

## Design & Architecture

### Why is the core provider-agnostic?

The workflow graph, state types, tool definitions, and routing rules are all expressed in
terms of Ananke's own interfaces (`IJob<T>`, `IStreamingAgentModel`, `IDistributedLock`,
etc.). LLM providers, vector databases, and infrastructure services are pluggable
implementations. This means:

- Provider changes, outages, or cost optimizations never require changes to business logic
- The same workflow that runs against OpenAI in production can run against a stub in tests
- The same state machine that uses Redis in production uses an in-memory lock in CI

### What makes Ananke's design unique?

- **Infrastructure-first** — distributed locks, checkpointing, and typed state exist before
  any LLM call; the LLM is a pluggable component, not the foundation
- **Full in-memory test mode** — every external dependency has a zero-config stub;
  no Docker, no API keys, no network needed to run a full integration test
- **Three-layer memory model** — semantic RAG + episodic conversation + empirical learning
  in one cohesive system with a clear promotion path from empirical to canonical knowledge
- **Agents that compound intelligence** — `IEmpiricalMemory` + `IOfflineLearner` enable
  genuine improvement over time, not just retrieval
- **MCP + A2A interoperability** — both major agent interop protocols supported out of the box
- **Agentic design patterns** — `AgenticPattern` builder pre-wires recognized patterns
  (Review-Critique, Iterative Refinement) as first-class constructs
- **Idiomatic C#** — async/await, generics, DI, `IAsyncEnumerable` throughout;
  no Python idioms translated to C#, no code-generation required

### Why three memory layers instead of one?

Each layer serves a different time horizon and access pattern:

| Layer | Time horizon | How it grows | Queried by |
|---|---|---|---|
| Semantic (RAG) | Stable documents | Manual ingestion pipeline | Vector similarity |
| Episodic | Single session | Auto-populated per conversation | Recency + session ID |
| Empirical | Repeated interactions | Agent tools + offline learner | Semantic similarity × confidence |

Combining all three into one store would require compromising the design of each.
Keeping them separate lets each layer be optimized for its use case.

### Can I extend Ananke with custom implementations?

Yes. Every core contract is an interface with a default in-memory implementation.
Implement `IKnowledgeStore` for a new vector database, `IStreamingAgentModel` for a new LLM
provider, `IDistributedLock` for a new coordination backend, etc.
### Why doesn't Ananke use Orleans for per-entity memory?

Orleans is a mature distributed virtual actor framework. It's compelling for per-entity
state -- each user becomes a grain with persistent state and single-writer guarantees.
However, Ananke already provides equivalents for every Orleans primitive:

| Orleans concept | Ananke equivalent |
|---|---|
| Grain identity | `IBaseContext.Id` / `EntityId` |
| Grain state persistence | `IKeyValueDataAdapter` (Redis/in-memory) |
| Single-writer guarantee | `IDistributedLock` (RedLock) |
| Timers / reminders | `BackgroundProcessor` |
| Activation / deactivation | TTL-based cleanup |
| Event messaging | `IHandoffChannel` / MQTT |

The real gap was **memory scoping**, not coordination. Ananke's memory layers were global
by default. This is now solved with first-class `EntityId` support on `EmpiricalEntry`,
`Episode`, and query APIs (`RecallOptions.EntityId`, `BrowseAsync(entityId:)`).

Adopting Orleans would add friction:

- **Dual lifecycle** -- Orleans grain activation vs. Ananke's `WorkflowExecution` / `ICheckpointStore`
- **Serialization lock-in** -- Orleans requires its own serializers; Ananke uses `System.Text.Json` everywhere
- **Testing overhead** -- Orleans needs `TestCluster`; Ananke tests run in milliseconds with `InMemory*` stubs
- **~6 extra NuGet packages** -- contradicts Ananke's focused packaging philosophy
- **Opinionated hosting** -- Orleans owns the host; Ananke embeds anywhere (ASP.NET Core, console, MCP server)

**When Orleans does make sense:** if you need automatic grain placement across silo clusters,
thousands of concurrent entity activations with memory-pressure-based lifecycle, or your
system is primarily actor-based and built on the Orleans ecosystem.

For `remember things about users across sessions,` a string field and a filter condition
solve the problem without importing a runtime.

---

← [Back to all FAQs](../faq.md) · [Feature Index](../reference/features.md) · [Getting Started](../guides/01-getting-started.md)
