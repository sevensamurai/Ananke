# Ananke.StateMachine

[![NuGet](https://img.shields.io/nuget/v/Ananke.StateMachine.svg)](https://www.nuget.org/packages/Ananke.StateMachine)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

Distributed state machine engine for .NET — RedLock coordination, composable middleware pipeline, guard conditions, and fault/reset circuit breaking.

## Install

```bash
dotnet add package Ananke.StateMachine
```

## Quick start

Define states, transitions, and wire them up:

```csharp
public class OrderMachine(IDistributedLock locker)
    : AbstractStateMachine<OrderCtx, OrderState, OrderTransition, OrderEvent>(
        OrderState.Pending, locker)
{
    protected override Action<ITransitionBuilder<OrderState, OrderTransition>> Transitions => b => b
        .From(OrderState.Pending)
            .On(OrderTransition.Reserve).GoTo(OrderState.Reserved)
            .On(OrderTransition.Cancel).GoTo(OrderState.Cancelled)
        .From(OrderState.Reserved)
            .On(OrderTransition.Confirm).GoTo(OrderState.Confirmed)
            .On(OrderTransition.Cancel).GoTo(OrderState.Cancelled);
}
```

### DI registration

```csharp
using Ananke.StateMachine.Extensions;

services.AddStateMachine(o => o
    .AllowImplicitSelfTransitions(false)
    .ConfigureLockRetry(maxRetries: 5));

// Register your concrete state machine
services.AddStateMachine<OrderMachine, OrderCtx, OrderState, OrderTransition, OrderEvent>();
```

`AddStateMachine` registers an in-memory `IDistributedLock` by default. Add `Ananke.Redis` to replace it with Redis-backed locking — call order doesn't matter.

## Features

- **Distributed locking** — safe coordination across instances via `IDistributedLock`
- **Composable middleware** — intercept every transition for logging, metrics, validation
- **Guard conditions** — block transitions based on runtime state
- **Fault / Reset** — circuit-breaker pattern (`OperationalStatus.Faulted` blocks all transitions until `ResetAsync`)
- **Lifecycle hooks** — `OnEnter` / `OnExit` per state
- **OpenTelemetry tracing** — built-in `ActivitySource` for transition spans

## Related packages

| Package | What it adds |
|---|---|
| `Ananke.Redis` | Redis-backed `IDistributedLock` and `IKeyValueDataAdapter` |
| `Ananke.MQTT` | MQTT-backed pub/sub channels for distributed messaging |
| `Ananke` | Meta-package — includes StateMachine + Orchestration + Bridge |

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
