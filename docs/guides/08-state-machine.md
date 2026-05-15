<!-- topic: state-machine, tags: state-machine, distributed, locking, guard, middleware, circuit-breaker, transitions -->
# 08 — State Machine

Build production-grade finite state machines with distributed locking, guard
conditions, composable middleware, lifecycle hooks, and circuit breaking.

`AbstractStateMachine` exists because long-lived entities — conversations, orders, device sessions — don't fit a start-to-finish pipeline model. The state machine handles ongoing status, event-driven transitions, and safe coordination across concurrent service instances. The same state machine that uses `InMemoryDistributedLock` in tests uses `RedisDistributedLock` in production; the topology doesn't change.

**Demo:** [StateMachineDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/01-foundations/StateMachineDemo)

---

## Core Concepts

`AbstractStateMachine` is a distributed FSM engine designed for long-running
services where multiple instances must coordinate safely. It provides:

- **Typed states and transitions** — compile-time safety
- **Distributed locking** — safe coordination across instances
- **Guard conditions** — block transitions based on runtime state
- **Middleware pipeline** — intercept every transition for logging, metrics, etc.
- **Circuit breaking** — `Fault` / `Reset` lifecycle for incident management

---

## Defining a State Machine

```csharp
using Ananke.Abstractions;
using Ananke.Abstractions.Distributed;
using Ananke.StateMachine;
using Ananke.StateMachine.Builder;

// 1. Define state, transitions, and notifications as enums
enum TicketState { Open, InProgress, Resolved, Closed }
enum TicketTransition { Assign, Resolve, Reopen, Close }
enum TicketNotification { None }

// 2. Define a context (identifies the entity being tracked)
sealed record TicketContext(long Id) : IBaseContext
{
    public string? Title { get; set; }
}

// 3. Implement the state machine
sealed class TicketMachine(IDistributedLock locker, StateMachineOptions? options = null)
    : AbstractStateMachine<TicketContext, TicketState, TicketTransition, TicketNotification>(
        TicketState.Open, locker, options)
{
    public string? ResolutionNote { get; set; }

    protected override Action<ITransitionBuilder<TicketState, TicketTransition>> Transitions => b => b
        .From(TicketState.Open)
            .On(TicketTransition.Assign).To(TicketState.InProgress)
        .From(TicketState.InProgress)
            .On(TicketTransition.Resolve).To(TicketState.Resolved)
        .From(TicketState.Resolved)
            .On(TicketTransition.Reopen).To(TicketState.Open)
            .On(TicketTransition.Close).To(TicketState.Closed);

    public override Task<TransitionResult<TicketState>> TransitionAsync(
        TicketContext ctx, TicketTransition t) =>
        InternalTransitionAsync(ctx, t);

    public override Task NotifyAsync(TicketContext ctx, TicketNotification n) =>
        Task.CompletedTask;
}
```

---

## Using the State Machine

```csharp
var machine = new TicketMachine(new InMemoryDistributedLock());

var ticket = new TicketContext(1) { Title = "Login page returns HTTP 500" };

// Open → InProgress
var result = await machine.TransitionAsync(ticket, TicketTransition.Assign);
Console.WriteLine($"{result.PreviousState} → {result.CurrentState}");
// Open → InProgress

// InProgress → Resolved
machine.ResolutionNote = "Fixed null-reference in AuthController";
result = await machine.TransitionAsync(ticket, TicketTransition.Resolve);

// Resolved → Closed
result = await machine.TransitionAsync(ticket, TicketTransition.Close);
```

---

## Invalid Transitions

Transitions not defined in the builder are rejected at runtime:

```csharp
// Cannot Resolve before Assigning (Open → Resolved not defined)
var result = await machine.TransitionAsync(ticket, TicketTransition.Resolve);
Console.WriteLine(result.Success);       // false
Console.WriteLine(result.ErrorMessage);  // "No transition defined..."
```

---

## Guard Conditions

Block transitions based on runtime state. Guards are evaluated before the
transition executes:

```csharp
protected override Action<ITransitionBuilder<TicketState, TicketTransition>> Transitions => b => b
    .From(TicketState.InProgress)
        .On(TicketTransition.Resolve)
        .To(TicketState.Resolved)
        .WithGuard(() => !string.IsNullOrEmpty(ResolutionNote),
            "ResolutionNote must be set before resolving");
```

```csharp
machine.ResolutionNote = null;
var result = await machine.TransitionAsync(ticket, TicketTransition.Resolve);
// result.Success == false
// result.ErrorMessage == "ResolutionNote must be set before resolving"

machine.ResolutionNote = "Fixed the bug";
result = await machine.TransitionAsync(ticket, TicketTransition.Resolve);
// result.Success == true
```

---

## Middleware Pipeline

Intercept every transition attempt with `IJobMiddleware<T>`:

```csharp
using Ananke.StateMachine.Middleware;

machine.UseMiddleware(new LoggingMiddleware<TicketContext, TicketState, TicketTransition>(
    msg => Console.WriteLine($"  ~ {msg}")));
```

The middleware runs on every transition — both successful and failed — making it
ideal for logging, metrics, auditing, and validation.

---

## Lifecycle Hooks

Run code when entering or exiting a state:

```csharp
protected override Action<ITransitionBuilder<TicketState, TicketTransition>> Transitions => b => b
    .From(TicketState.Open)
        .On(TicketTransition.Assign).To(TicketState.InProgress)
        .OnEnter(() => Console.WriteLine("Entering InProgress"))
        .OnExit(() => Console.WriteLine("Leaving Open"));
```

---

## Circuit Breaking — Fault / Reset

`OperationalStatus` provides a machine-level circuit breaker. When faulted,
**all transitions are blocked** until the machine is explicitly reset:

```csharp
// Simulate a critical incident
var fault = await machine.FaultAsync(ticket,
    "Schema migration rolled back — manual DBA intervention required");
Console.WriteLine(fault.CurrentStatus);  // Faulted

// All transitions blocked while faulted
var result = await machine.TransitionAsync(ticket, TicketTransition.Assign);
Console.WriteLine(result.Success);  // false

// Operator remediation complete — reset the machine
var reset = await machine.ResetAsync(ticket,
    "Migration re-applied — system verified healthy");
Console.WriteLine(reset.CurrentStatus);  // Active

// Transitions resume
result = await machine.TransitionAsync(ticket, TicketTransition.Assign);
Console.WriteLine(result.Success);  // true
```

---

## Distributed Locking

In production, use Redis for safe coordination across instances:

```bash
dotnet add package Ananke.Redis
```

```csharp
using Ananke.Redis;

var locker = new RedisDistributedLock(redisConnection);
var machine = new TicketMachine(locker);
```

For dev/test:

```csharp
var machine = new TicketMachine(new InMemoryDistributedLock());
```

---

## Bridge Layer — FSM in Workflows

Use the Bridge convenience layer to wire state machine transitions into workflow
jobs. See [Guide 09 — Distributed Systems](09-distributed.md) for details.

---

## What's Next

| Next guide | What you'll learn |
|---|---|
| [09 — Distributed](09-distributed.md) | Redis locking, MQTT pub/sub, agent handoff |
| [10 — Observability](10-observability.md) | OpenTelemetry tracing |

---

← [Back to Learning Path](learning-path.md)
