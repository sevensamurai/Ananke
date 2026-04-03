# ADR-005: Layered Simplification — SM as Top-Level Orchestrator

| Field         | Value                                                          |
|---------------|----------------------------------------------------------------|
| **Status**    | Accepted — All phases (1–7) implemented                        |
| **Date**      | 2025-07-25                                                     |
| **Authors**   | —                                                              |
| **Deciders**  | Ananke maintainers                                             |
| **Tags**      | simplification, state-machine, workflow, interrupts, DX         |
| **Relates to**| ADR-004 (interrupt propagation), `Ananke.Bridge`, `StreamingChatWorkflow` |

---

## Context

ADR-004 identified four gaps in interrupt propagation and proposed bottom-up
fixes. Those fixes shipped (Increments 1–4) but revealed a deeper issue:
**the architecture is inverted.**

Today the workflow is the top-level construct and the state machine is bolted
on via a Bridge layer. This creates:

- **Generic type explosion:** `StateMachineTriggerJob<TWorkflowState, TContext, TState, TTransition, TNotification>` — 5 type parameters
- **Mandatory ceremony:** Every SM requires `IBaseContext` record + `Notification` enum + `AbstractStateMachine` subclass + `TransitionAsync` override — ~40 lines for 4 lines of protocol
- **Handle indirection:** `ChatSessionHandle` bundles events + interrupt sink + completion into one object that only exists because `BuildInterruptible()` merges concerns
- **Bridge layer:** 4 hand-written adapter patterns to connect SM ↔ Workflow because neither speaks the other's language
- **`IBaseContext.Command`:** MQTT transport concern leaked into the core abstraction

### The inversion

The natural hierarchy for a multi-phase conversational app is:

```
StateMachine (declares phases and protocol)
  └─ each phase runs a Workflow or Agent (does the work)
```

But today it's:

```
StreamingChatWorkflow.BuildInterruptible() (top-level)
  └─ Bridge.CreateInterruptBridge(machine, ...) (glue)
     └─ StateMachine (bolted on for validation)
```

### The insight

If the **state machine is the top-level orchestrator**, then:

- `OnEnter` runs whatever work a state needs (agent, workflow, or plain code)
- The SM owns the `CancellationToken` for the current state's work
- Interrupt transitions cancel the CTS and deliver the payload — no bridge needed
- The workflow is just "the work a state does" — it doesn't know about interrupts
- Events flow from the work to the consumer independently

This gives us **incremental levels of complexity** where each level is
independently useful and nothing at a lower level references a higher one:

```
Level 1: Agent              → just runs, produces events
Level 2: Workflow           → coordinates multiple agents/jobs
Level 3: State Machine      → coordinates phases, owns interrupts + CTS
Level 4: Distributed        → SM uses distributed lock, sink uses MQTT
```

---

## Decision

Restructure the building blocks bottom-up so each level is self-contained.
The state machine becomes a configured instance (not a subclass), owns its
interrupt infrastructure, and never references workflow types.

### Level 1 — Agent (no changes needed)

Already works:

```csharp
await StreamingChatWorkflow.Create("chat", model)
    .WithSystemPrompt("You are helpful.")
    .WithTools(tools)
    .OnTextDelta(async delta => Console.Write(delta))
    .RunAsync([AgentMessage.User("hello")], ct);
```

No SM, no workflow graph, no handle.

### Level 2 — Workflow (no changes needed)

Already works:

```csharp
var workflow = new Workflow<MyState>("pipeline")
    .Job("classify", classifyJob)
    .Job("search", searchJob)
    .Job("respond", respondJob)
    .Then("classify", Workflow.Decide<MyState>(s =>
        s.NeedsSearch ? "search" : "respond"))
    .Then("search", "respond");

await workflow.RunAsync(initialState, ct);
```

Multiple agents/tools composed into a graph. No SM.

### Level 3 — State Machine (the core change)

**New simplified interface — 2 type parameters instead of 4:**

```csharp
public interface IStateMachine<S, T> where S : Enum where T : Enum
{
    Task<TransitionResult<S>> FireAsync(T transition, object? payload = null);
    S CurrentState { get; }
    bool IsInterrupted { get; }
}
```

**Factory instead of subclass:**

```csharp
var machine = StateMachine.Create<Phase, Action>(Phase.Searching, b => b
    .From(Searching).On(StartPaperwork).To(Paperwork)
    .From(Paperwork).On(Complete).To(Done)
    .From(Searching).On(Interrupt).ToInterrupt(Searching)
    .From(Searching).On(Resume).ToResume());
```

No `IBaseContext`. No `Notification` enum. No `AbstractStateMachine` subclass.
No `TransitionAsync → InternalTransitionAsync` override.

**SM owns CTS — `OnEnter` receives a `CancellationToken`:**

```csharp
machine.OnEnter(Phase.Searching, async ct =>
{
    // ct is cancelled when SM leaves this state (interrupt or normal transition)
    await StreamingChatWorkflow.Create("search", model)
        .WithTools(searchTools)
        .RunAsync(messages, ct);
});

machine.OnEnter(Phase.Paperwork, async ct =>
{
    await StreamingChatWorkflow.Create("adoption", model)
        .WithTools(adoptionTools)
        .RunAsync(messages, ct);
});
```

The SM creates a `CancellationTokenSource` on state entry. When it exits the
state (normal transition or interrupt), it cancels the CTS. The work doesn't
need to know about interrupts — it just respects its CT.

**SM delivers interrupts — `OnInterrupt` registers a sink:**

```csharp
machine.OnInterrupt(async (payload, ct) =>
{
    // Fires after a successful interrupt transition
    // SM already cancelled the CTS for the current state's work
    await deliverToClient(payload);
});
```

Or with `IInterruptSink<T>`:

```csharp
machine.OnInterrupt(sink);
```

**Full Level 3 example (Pet Adoption Demo):**

```csharp
// Protocol — what phases exist, what interrupts are allowed
var machine = StateMachine.Create<Phase, Action>(Phase.Searching, b => b
    .From(Searching).On(StartPaperwork).To(Paperwork)
    .From(Paperwork).On(Complete).To(Done)
    .From(Searching).On(Interrupt).ToInterrupt(Searching)
    .From(Searching).On(Resume).ToResume());

// Work — what each phase does (each is a small, focused workflow)
machine.OnEnter(Phase.Searching, async ct =>
{
    await StreamingChatWorkflow.Create("search", model)
        .WithTools(searchTools)
        .OnTextDelta(async delta => await sseWriter.WriteDelta(delta))
        .OnToolCall(async (name, args) => await sseWriter.WriteToolCall(name, args))
        .RunAsync(messages, ct);
});

// Interrupt delivery
machine.OnInterrupt(sink);

// Interrupt endpoint — just fires the transition
app.MapPost("/api/interrupt", async (InterruptRequest req) =>
{
    var result = await machine.FireAsync(
        Action.Interrupt,
        AgentMessage.User(req.Message));

    return result.Success
        ? Results.Ok(new { status = "interrupted" })
        : Results.Conflict(new { error = result.ErrorMessage });
});
```

Comment out the `.From(Searching).On(Interrupt).ToInterrupt(Searching)` line →
`FireAsync` returns `Success = false` → nothing else changes.

### Level 4 — Distributed

Same SM, opt-in distributed primitives:

```csharp
var machine = StateMachine.Create<Phase, Action>(Phase.Searching, b => b
    ...)
    .WithDistributedLock(redisLock);

machine.OnInterrupt(new MqttInterruptSink<AgentMessage>(mqttClient, topic));
```

The `IStateMachine<S,T>` contract doesn't change. The lock is an internal
concern. The sink is transport-agnostic.

---

## What Stays

| Component | Status | Reason |
|---|---|---|
| `TransitionBuilder` fluent DSL | ✅ Keep | Good DX. Now produces a machine instance directly |
| Guard conditions | ✅ Keep | Real value for protocol validation |
| Interrupt stack (push/pop/depth) | ✅ Keep | Correct abstraction for nested interrupts |
| Middleware pipeline | ✅ Keep | Logging, tracing, audit |
| `Workflow<TState>` job graph | ✅ Keep | Right model for multi-step orchestration |
| `StreamingChatWorkflow` builder | ✅ Keep | Level 1 + Level 2 agent loop |
| `IInterruptSink<T>` | ✅ Keep | Transport-agnostic interrupt delivery |
| `ChatSessionEvent` types | ✅ Keep | Clean event hierarchy for SSE |
| `ToolKit` | ✅ Keep | Tool definitions and execution |
| `AbstractStateMachine<C,S,T,N>` | ✅ Keep | Backward compat, power users, distributed |
| MQTT/Redis infrastructure | ✅ Keep | Level 4 distributed transport |

## What Goes or Gets Absorbed

| Component | Fate | Reason |
|---|---|---|
| `InterruptableBridge` | Absorbed into SM `OnInterrupt` | SM validates AND delivers |
| `ChatSessionHandle` | Dissolved | Events via callbacks/stream, interrupt via SM, completion via task |
| `InterruptableSession` | Absorbed into SM CTS ownership | SM owns the CTS per state |
| `BridgeExtensions.CreateInterruptBridge` | Removed | No bridge needed |
| `WorkflowTriggerAction` | Absorbed into SM `OnEnter` | SM already has `OnEnter` |
| `IBaseContext.Command` | Moved to MQTT layer | Transport concern |
| Mandatory `Notification` enum | Optional | Most SMs don't notify |
| `AbstractStateMachine` subclassing (required) | Optional (power users) | Factory creates instances |

---

## Implementation Plan

### Phase 1: `IStateMachine<S,T>` + `StateMachine.Create<S,T>()`

**Package:** `Ananke.StateMachine`

| Change | Description |
|---|---|
| New `IStateMachine<S,T>` | 2-param interface: `FireAsync`, `CurrentState`, `IsInterrupted` |
| New `StateMachine` static class | `Create<S,T>(S initial, Action<ITransitionBuilder<S,T>> configure)` factory |
| New internal `SimpleStateMachine<S,T>` | Wraps `TransitionBuilder` + in-memory lock. No context, no notification |
| Keep `AbstractStateMachine<C,S,T,N>` | Unchanged, implements both `IActionStateMachine` and `IStateMachine` |

**Validation:** StateMachineDemo still works. New unit tests for `StateMachine.Create`.

**Developer experience after Phase 1:**

```csharp
// Before: ~40 lines (enum + record + class + overrides)
// After: 5 lines
var machine = StateMachine.Create<Phase, Action>(Phase.Searching, b => b
    .From(Searching).On(StartPaperwork).To(Paperwork)
    .From(Searching).On(Interrupt).ToInterrupt(Searching)
    .From(Searching).On(Resume).ToResume());

await machine.FireAsync(Action.StartPaperwork);
```

### Phase 2: SM Owns CTS — `OnEnter` Receives `CancellationToken`

**Package:** `Ananke.StateMachine`

| Change | Description |
|---|---|
| `OnEnter(S, Func<CancellationToken, Task>)` | New overload on builder + `IStateMachine<S,T>` |
| CTS-per-state lifecycle | SM creates linked CTS on state entry, cancels on exit/interrupt |
| `OnExit` fires after CTS cancel | Exit action runs after work is cancelled, for cleanup |

**Key detail:** The CTS is linked to an optional parent CT (passed to `FireAsync`
or held by the machine). When the SM transitions out of a state — whether by
normal transition or interrupt — the CTS is cancelled before `OnExit` fires.

**Validation:** Test that `OnEnter` work is cancelled when a transition fires.

### Phase 3: `OnInterrupt(IInterruptSink<T>)` on the SM

**Package:** `Ananke.StateMachine`

| Change | Description |
|---|---|
| `OnInterrupt(IInterruptSink<T>)` | Registers a sink. On successful interrupt transition: cancel CTS + deliver payload |
| `OnInterrupt(Func<object?, CancellationToken, Task>)` | Callback alternative for simple cases |
| Payload passthrough | `FireAsync(transition, payload)` → sink receives the payload |

**Validation:** Test interrupt → CTS cancelled → sink receives payload → `OnEnter` re-fires.

### Phase 4: Dissolve the Handle

**Package:** `Ananke.Orchestration`

| Change | Description |
|---|---|
| `StreamingChatWorkflow` returns `IAsyncEnumerable<ChatSessionEvent>` | New `BuildStream()` method alongside existing `Build()`/`BuildInterruptible()` |
| Deprecate `BuildInterruptible()` | Still works but marked obsolete — use SM + `BuildStream()` instead |
| Deprecate `ChatSessionHandle` | Event stream + sink + completion are separate concerns now |
| Keep `ChatSessionEvent` types | Clean, reusable event hierarchy |

**Migration:** `BuildInterruptible()` continues to work. New path is:

```csharp
// New: events as IAsyncEnumerable, interrupts via SM
await foreach (var evt in StreamingChatWorkflow.Create("search", model)
    .WithTools(tools)
    .BuildStream(messages, ct))
{
    // pattern match on ChatSessionEvent
}
```

### Phase 5: Simplify Bridge

**Package:** `Ananke` (Bridge)

| Change | Description |
|---|---|
| Deprecate `InterruptableBridge` | Absorbed into SM `OnInterrupt` |
| Deprecate `WorkflowTriggerAction` | Absorbed into SM `OnEnter` |
| Keep `StateMachineTriggerJob` | Still useful for workflow→SM signals (Level 2→3 bridge) |
| Keep `WorkflowCompletionTrigger` | Still useful for "workflow done → fire transition" |

### Phase 6: Clean Up Abstractions

**Package:** `Ananke.Abstractions`

| Change | Description |
|---|---|
| Move `Command` out of `IBaseContext` | Into MQTT-specific context type |
| `IBaseContext` becomes `{ long Id { get; } }` | Only the lock key |
| Consider renaming `IChannelWriter<A>` → `ITransportWriter<A>` | Disambiguation (separate PR) |

### Phase 7: Update Demos

| Demo | Change |
|---|---|
| **PetAdoptionDemo** | Rewrite with Level 3 pattern: SM top-level, search + adoption as separate workflows |
| **StateMachineDemo** | Add Level 1→3 progression example |
| **BasicAgentDemo** | Level 1 showcase (no changes needed) |
| **SimpleWorkflowDemo** | Level 2 showcase (no changes needed) |

---

## Phasing and Risk

| Phase | Breaking? | Risk | Can Ship Independently? | Status |
|---|---|---|---|---|
| **1** (IStateMachine + factory) | No — additive | Low | ✅ Yes | ✅ Done |
| **2** (CTS ownership) | No — new overload | Medium — lifecycle correctness | ✅ Yes | ✅ Done |
| **3** (OnInterrupt + sink) | No — additive | Low | ✅ Yes, after Phase 2 | ✅ Done |
| **4** (Dissolve handle) | No — deprecation | Low — old path still works | ✅ Yes | ✅ Done |
| **5** (Simplify bridge) | No — deprecation | Low | ✅ Yes | ✅ Done |
| **6** (Clean abstractions) | **Yes** — `IBaseContext.Command` | Medium — MQTT + distributed demos | After Phase 5 | ✅ Done |
| **7** (Update demos) | No | Low | ✅ Yes, after each phase | ✅ Done |

Phases 1–3 are the core. They can ship as one PR or separately.
Phases 4–5 are cleanup. They make the old path obsolete but don't remove it.
Phase 6 is the only breaking change.
Phase 7 is incremental — each demo updates as the phase it depends on ships.

---

## Validation Criteria

Each phase must satisfy:

1. **Level N works without Level N+1:** Agent without workflow. Workflow without SM. SM without distributed.
2. **Removing a level is subtractive:** Comment out the SM → workflows still run, interrupts are no longer validated.
3. **Existing tests pass:** `AbstractStateMachine` path unchanged.
4. **Demo progression:** PetAdoptionDemo can be shown at each level, getting richer each time.
