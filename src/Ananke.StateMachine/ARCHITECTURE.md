# Ananke.StateMachine — Architecture

> Distributed state machine engine with transition guards, middleware,
> fault/reset circuit breaking, and channel-driven transitions.

## Role

Provides a generic state machine (`StateMachine<TState, TAction>`) and a
persistent, distributed variant (`AbstractStateMachine<C, S, T, N>`) that
uses `IDistributedLock` and `IKeyValueDataAdapter` for coordination across
processes. State machines can be driven by channel events (MQTT) or by
direct API calls.

## Dependencies

- `Ananke.Abstractions` (project)
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Hosting.Abstractions`

## Namespace → Folder Map

| Namespace | Contents |
|-----------|----------|
| `Ananke.StateMachine` | `StateMachine<S, T>`, `AbstractStateMachine<C, S, T, N>`, `IStateMachine`, `StateMachineOptions`, `TransitionResult` |
| `Ananke.StateMachine.Builder` | `ITransitionBuilder`, `TransitionBuilder` — fluent guard/action configuration |
| `Ananke.StateMachine.Middleware` | `ITransitionMiddleware` |
| `Ananke.StateMachine.Channels` | `TransitionEvent`, `StateMachineChannelWorker` — drive transitions from channel messages |
| `Ananke.StateMachine.Applications` | `AbstractServiceWorker` — `IHostedService` base for background state machine workers |
| `Ananke.StateMachine.Config` | `StateMachineServiceOptions` |
| `Ananke.StateMachine.Extensions` | `ServiceCollectionExtensions` |
| `Ananke.StateMachine.Tracing` | `StateMachineActivitySource` |

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `StateMachine<TState, TAction>` | Class | In-memory state machine with fluent `Configure(state).On(action).TransitionTo(next)` builder. Supports guards, actions, middleware. |
| `AbstractStateMachine<C, S, T, N>` | Abstract class | Persistent, distributed state machine. Uses `IDistributedLock` for concurrency, `IKeyValueDataAdapter` for state persistence. Keyed by `IBaseContext.Id`. Supports fault/reset, interrupt stack. |
| `ITransitionBuilder` | Interface | Fluent builder: `.When(guard).Do(action).TransitionTo(state)` |
| `ITransitionMiddleware` | Interface | Cross-cutting logic around transitions (logging, auditing) |
| `TransitionResult` | Record | Outcome of a transition attempt — success/failure + diagnostics |
| `StateMachineChannelWorker` | Class | Bridges `IChannelReader` events to state machine transitions |

## State Machine Lifecycle

```
Configure(state)
  .On(action)
    .When(guard)         ← optional predicate
    .Do(action)          ← optional side effect
    .TransitionTo(next)

TransitionAsync(context, action)
  → Acquire distributed lock (context.Id)
  → Load persisted state
  → Evaluate guards
  → Execute actions + middleware
  → Persist new state
  → Release lock
  → Return TransitionResult
```
