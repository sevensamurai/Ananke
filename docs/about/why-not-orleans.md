# Why Ananke Does Not Use Orleans for Per-Entity Memory

> "Why reinvent the wheel? Orleans already solves the virtual actor problem."

This is a fair question. Orleans is a mature, battle-tested framework from
Microsoft for building distributed systems with a virtual actor model. If
Ananke needs per-entity long-term memory — where each user, customer, or
device has its own persistent context across sessions — why not adopt Orleans
and let grains handle it?

The short answer: **Ananke already has the primitives. Orleans would add a
framework-sized dependency to solve a key-prefixing problem.**

---

## What Orleans gives you

Orleans organizes distributed state around **grains** — virtual actors with
an identity, persistent state, and single-threaded execution. You define a
grain interface, inherit from `Grain`, register a storage provider, and the
runtime handles activation, deactivation, placement across silos, and
distributed coordination.

For per-entity state, this is compelling: each user becomes a grain, their
state loads on first access, and Orleans guarantees that only one activation
handles requests for that user at a time.

## What Ananke already provides

Before looking at what Orleans adds, it's worth mapping what Ananke already
has — because the overlap is larger than it first appears.

| Orleans concept | Ananke equivalent | Where it lives |
|---|---|---|
| **Grain identity** (string key) | `IBaseContext.Id` | `Ananke.Abstractions` |
| **Grain state persistence** | `IKeyValueDataAdapter` (Redis/in-memory) | `Ananke.Abstractions` + `Ananke.Redis` |
| **Single-writer guarantee** | `IDistributedLock.RunCoordinatedActionAsync` | `Ananke.Abstractions` + `Ananke.Redis` |
| **Activation/deactivation** | TTL-based cleanup in `IConversationMemory` | `Ananke.Orchestration` + `Ananke.Redis` |
| **Timers/reminders** | `BackgroundProcessor` / `IBackgroundWorker` | `Ananke.Abstractions` |
| **State machine with FSM transitions** | `AbstractStateMachine<C,S,T,N>` | `Ananke.StateMachine` |
| **Event messaging** | `IHandoffChannel` / MQTT | `Ananke.Abstractions` + `Ananke.MQTT` |

The `AbstractStateMachine` already **is** a virtual actor in practice: it
uses `IBaseContext.Id` as the partition key, persists state via
`IKeyValueDataAdapter`, coordinates writes via `IDistributedLock`, and
activates lazily on first interaction. It just doesn't call itself a grain.

## Where the real gap was

The gap wasn't in coordination or persistence. It was in **memory scoping**.
Ananke's three memory layers — conversation (`IConversationMemory`), empirical
(`IEmpiricalMemory`), and knowledge (`IKnowledgeStore`) — were all global by
default. An empirical entry about Customer A's preferences lived in the same
undifferentiated pool as entries about Customer B.

This is now solved with first-class `EntityId` support on `EmpiricalEntry`,
`Episode`, and the corresponding query APIs (`RecallOptions.EntityId`,
`BrowseAsync(entityId:)`). The fix is a string field and a filter condition —
not a framework adoption.

## Where Orleans adds friction

Adopting Orleans for what amounts to a key-prefixing concern introduces
substantial friction:

### 1. Dual lifecycle model

Ananke workflows have their own lifecycle: `WorkflowExecution`, `ICheckpointStore`,
pause/resume semantics, `SubFlowJob`. Orleans grains have a separate lifecycle
managed by the silo runtime. Running both creates two competing state management
systems that must be kept in sync. Every workflow that touches entity memory would
need to bridge between Ananke's execution model and Orleans' grain activation model.

### 2. Serialization lock-in

Orleans requires its own serialization framework — either
`[GenerateSerializer]` source generators or the older `Bond`/`MessagePack`
integration. Ananke uses `System.Text.Json` everywhere: checkpoint
serialization, Redis storage, Qdrant payloads, conversation history. Bridging
Orleans serialization with Ananke's JSON-based persistence means maintaining
two serialization paths for the same data.

### 3. Testing overhead

Ananke tests are plain NUnit + Shouldly against in-memory implementations.
Every store interface (`IEmpiricalMemory`, `IConversationMemory`,
`ICheckpointStore`, `IDistributedLock`) has a zero-config in-memory
implementation. Tests run in milliseconds with no infrastructure.

Orleans testing requires `TestCluster` or `TestSiloHost`, which spins up a
real silo. This adds seconds of startup overhead per test class, complicates
CI, and makes it harder to test memory-related logic in isolation.

### 4. Dependency surface

Orleans brings a significant package graph:

- `Microsoft.Orleans.Core`
- `Microsoft.Orleans.Runtime`
- `Microsoft.Orleans.Serialization`
- `Microsoft.Orleans.Streaming` (if using streams)
- A storage provider package (Redis, Azure Storage, SQL)
- `Microsoft.Orleans.Sdk` (source generators)

For a library that ships 14 focused NuGet packages — each with a narrow
dependency surface — absorbing a framework-sized dependency for one feature
contradicts Ananke's packaging philosophy.

### 5. Cognitive duplication

Orleans has its own concepts for everything Ananke already provides:

| Concern | Orleans name | Ananke name |
|---|---|---|
| Timers | Grain timers / reminders | `BackgroundProcessor` |
| Persistence | `IPersistentState<T>` | `IKeyValueDataAdapter` |
| Coordination | Turn-based scheduling | `IDistributedLock` |
| Messaging | Orleans Streams | `IHandoffChannel` / MQTT |

Adopting Orleans doesn't replace these — it adds parallel abstractions.
Developers would need to learn both sets of concepts and decide which to use
in each situation.

### 6. Opinionated hosting

Orleans owns the host. It manages silo membership, grain placement, cluster
topology, and the threading model. Ananke is designed to be embedded anywhere:
ASP.NET Core, console apps, background workers, test harnesses — even inside
an MCP server. Surrendering host ownership to Orleans limits this flexibility.

## What Ananke does instead

Rather than adopting a framework, Ananke closes the entity-scoping gap with
minimal, composable additions to its existing primitives:

**First-class `EntityId` on data types.** `EmpiricalEntry` and `Episode` now
have a `string? EntityId` property. Null means global; non-null means scoped
to that entity. This is an opaque partition key — the same pattern as
`IConversationMemory`'s `sessionId` and `AbstractStateMachine`'s
`IBaseContext.Id`.

**Entity-aware dedup.** Semantic deduplication in `CommitAsync` now scopes by
entity: a pattern about Customer A will never merge with a similar pattern
about Customer B, preventing cross-entity knowledge leakage.

**Entity-aware recall.** `RecallOptions.EntityId` filters recall to a specific
entity. `RecallOptions.IncludeGlobal` adds a fallback layer: "show me what we
know about this customer, plus any global knowledge that's relevant."

**Entity-aware browsing.** Background processes (decay sweeps, curiosity walks,
consolidation) can scope to a specific entity via `BrowseAsync(entityId:)`.

**Zero new dependencies.** All of this works with the existing
`InMemoryEmpiricalMemory`, `QdrantEmpiricalMemory`, `InMemoryEpisodeStore`,
and `QdrantEpisodeStore` — no new packages, no new infrastructure.

## When Orleans might make sense

Orleans is a strong choice when:

- You need **automatic grain placement** across a cluster of silos — true
  distributed actor scheduling, not just distributed locking.
- You have **thousands of concurrent entity activations** that benefit from
  Orleans' memory management (activation/deactivation based on pressure).
- Your system is **primarily actor-based** — Orleans is the application model,
  not a library added to an existing one.
- You're building on the **Orleans ecosystem** (Streaming, Transactions,
  Reminders) and these features are central to your design.

If your system fits this profile, Orleans is excellent. But if your primary
need is "remember things about users across sessions" — which is what
per-entity long-term memory is — then a string field and a filter condition
solve the problem without importing a runtime.

## Summary

| Concern | Orleans | Ananke (current) |
|---------|---------|-------------------|
| Entity identity | `IGrainWithStringKey` | `IBaseContext.Id` / `EntityId` |
| State persistence | `IPersistentState<T>` + storage provider | `IKeyValueDataAdapter` + entity-keyed fields |
| Single-writer | Grain turn-based scheduling | `IDistributedLock` |
| Memory layers | Custom (must build) | `IConversationMemory` + `IEmpiricalMemory` + `IKnowledgeStore` |
| Entity-scoped memory | Grain state isolation | `EntityId` field + filter conditions |
| Lifecycle | Silo-managed activation | TTLs + lazy initialization |
| Testing | `TestCluster` / `TestSiloHost` | Plain NUnit with `InMemory*` implementations |
| Dependency footprint | ~6 NuGet packages + silo hosting | Zero new dependencies |
| Layered/global fallback | Must build custom | `RecallOptions.IncludeGlobal` |
