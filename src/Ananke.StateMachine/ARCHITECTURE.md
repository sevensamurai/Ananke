# Ananke.StateMachine — Architecture

> Distributed state machine engine with transition guards, middleware,
> fault/reset circuit breaking, and channel-driven transitions.

## Role

Provides a simplified in-process state machine (`IStateMachine<S, T>` /
`StateMachine<S, T>`, created via the `StateMachine.Create<S, T>(...)` factory) and a
persistent, distributed variant (`AbstractStateMachine<C, S, T, N>`) that
uses `IDistributedLock` and `IKeyValueDataAdapter` for coordination across
processes. Both are configured with the same fluent `ITransitionBuilder<S, T>`.
State machines can be driven by channel events (MQTT) or by direct API calls.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `StateMachine<S, T>` — the in-process state machine, created via `StateMachine.Create<S, T>(...)`; supports guards, actions, and an interrupt stack — `src/Ananke.StateMachine/StateMachine.cs`
2. `AbstractStateMachine<C, S, T, N>` — the persistent, distributed variant; uses `IDistributedLock` and `IKeyValueDataAdapter` for coordination across processes — `src/Ananke.StateMachine/AbstractStateMachine.cs`
3. `ITransitionBuilder<S, T>` — the fluent builder (`.From(state).On(transition).To(state)`, `.When(guard)`, `.WithAction(action)`) used to configure both state machine variants — `src/Ananke.StateMachine/Builder/ITransitionBuilder.cs`

---

## Dependencies

- `Ananke.Abstractions` (project)
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Hosting.Abstractions`

## Namespace → Folder Map

| Namespace | Contents |
|-----------|----------|
| `Ananke.StateMachine` | `StateMachine` (factory), `StateMachine<S, T>`, `AbstractStateMachine<C, S, T, N>`, `IStateMachine<S, T>`, `StateMachineOptions`, `TransitionResult<S>` |
| `Ananke.StateMachine.Builder` | `ITransitionBuilder<S, T>`, `ITransitionConfigBuilder<S, T>`, `TransitionBuilder<S, T>` — fluent guard/action configuration |
| `Ananke.StateMachine.Middleware` | `ITransitionMiddleware<C, S, T>` |
| `Ananke.StateMachine.Channels` | `TransitionEvent`, `StateMachineChannelWorker` — drive transitions from channel messages |
| `Ananke.StateMachine.Applications` | `HostedServiceBase` — `BackgroundService` base for background state machine workers |
| `Ananke.StateMachine.Config` | `StateMachineServiceOptions` |
| `Ananke.StateMachine.Extensions` | `ServiceCollectionExtensions` |
| `Ananke.StateMachine.Tracing` | `StateMachineActivitySource` |

## Key Types

| Type | Kind | Purpose | Source |
|------|------|---------|--------|
| `StateMachine<S, T>` | Class | In-process state machine created via `StateMachine.Create<S, T>(initialState, configure, options?)`. Supports guards, actions, and an interrupt stack (`OnEnter`, `IsInterrupted`). No distributed locking or context. | `src/Ananke.StateMachine/StateMachine.cs` |
| `AbstractStateMachine<C, S, T, N>` | Abstract class | Persistent, distributed state machine. Uses `IDistributedLock` for concurrency, `IKeyValueDataAdapter` for state persistence. Keyed by `IBaseContext.Id`. Supports fault/reset, interrupt stack. | `src/Ananke.StateMachine/AbstractStateMachine.cs` |
| `ITransitionBuilder<S, T>` / `ITransitionConfigBuilder<S, T>` | Interfaces | Fluent builder: `.From(state).On(transition).To(state)` (or `.ToInterrupt(state)` / `.ToResume()`), then optionally `.When(guard)` / `.WhenAsync(guard)` / `.WithAction(action)` | `src/Ananke.StateMachine/Builder/ITransitionBuilder.cs` |
| `ITransitionMiddleware<C, S, T>` | Interface | Cross-cutting logic around transitions (logging, auditing) | `src/Ananke.StateMachine/Middleware/ITransitionMiddleware.cs` |
| `TransitionResult<S>` | Record | Outcome of a transition attempt — success/failure + diagnostics | `src/Ananke.StateMachine/TransitionResult.cs` |
| `StateMachineChannelWorker` | Class | Bridges `IChannelReader` events to state machine transitions | `src/Ananke.StateMachine/Channels/StateMachineChannelWorker.cs` |

## State Machine Lifecycle

```
StateMachine.Create<S, T>(initialState, b => b
  .From(state).On(transition)
    .To(next)             ← or .ToInterrupt(state) / .ToResume()
    .When(guard)          ← optional predicate
    .WithAction(action))  ← optional side effect

FireAsync(transition, payload?)               (StateMachine<S, T>)
TransitionAsync(context, transition)          (AbstractStateMachine<C, S, T, N> — adds:)
  → Acquire distributed lock (context.Id)
  → Load persisted state
  → Evaluate guards
  → Execute actions + middleware
  → Persist new state
  → Release lock
  → Return TransitionResult<S>
```
