<!-- topic: state-machine, tags: state-machine, distributed, locking, guard, middleware, circuit-breaker, transitions -->
# 08 — State Machine

Build production-grade finite state machines with distributed locking, guard
conditions, composable middleware, lifecycle hooks, and circuit breaking.

`AbstractStateMachine` exists because long-lived entities — conversations, orders, device sessions — don't fit a start-to-finish pipeline model. The state machine handles ongoing status, event-driven transitions, and safe coordination across concurrent service instances. The same state machine that uses `InMemoryDistributedLock` in tests uses `RedisDistributedLock` in production; the topology doesn't change.

**Demo:** [StateMachineDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/01-foundations/StateMachineDemo)

---

## Core Concepts

`AbstractStateMachine` is a distributed FSM engine designed for long-running
services where multiple instances must coordinate safely. One machine serves
every context of its kind (every ticket, every device): each context's state,
step, interrupt stack and operational status are stored under its own key, and
each transition of a context runs under that context's lock. It provides:

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
sealed record TicketContext(string Id) : IBaseContext
{
    public string? Title { get; set; }
    public string? ResolutionNote { get; set; }
}

// 3. Implement the state machine
sealed class TicketMachine(IDistributedLock locker, IKeyValueDataAdapter store, StateMachineOptions? options = null)
    : AbstractStateMachine<TicketContext, TicketState, TicketTransition, TicketNotification>(
        TicketState.Open, locker, store, options)
{
    protected override Action<ITransitionBuilder<TicketState, TicketTransition>> Transitions => b => b
        .From(TicketState.Open)
            .On(TicketTransition.Assign).To(TicketState.InProgress)
        .From(TicketState.InProgress)
            .On(TicketTransition.Resolve).To(TicketState.Resolved)
        .From(TicketState.Resolved)
            .On(TicketTransition.Reopen).To(TicketState.Open)
        .From(TicketState.Resolved)
            .On(TicketTransition.Close).To(TicketState.Closed);

    public override Task<TransitionResult<TicketState>> TransitionAsync(
        TicketContext ctx, TicketTransition t, CancellationToken ct = default) =>
        InternalTransitionAsync(ctx, t, ct);

    public override Task NotifyAsync(TicketContext ctx, TicketNotification n, CancellationToken ct = default) =>
        Task.CompletedTask;
}
```

---

## Using the State Machine

```csharp
// InMemoryDistributedLock implements both IDistributedLock and IKeyValueDataAdapter
var lockAndStore = new InMemoryDistributedLock();
var machine = new TicketMachine(lockAndStore, lockAndStore);

var ticket = new TicketContext("1") { Title = "Login page returns HTTP 500" };

// Open → InProgress
var result = await machine.TransitionAsync(ticket, TicketTransition.Assign);
Console.WriteLine($"{result.PreviousState} → {result.CurrentState}");
// Open → InProgress

// InProgress → Resolved
ticket.ResolutionNote = "Fixed null-reference in AuthController";
result = await machine.TransitionAsync(ticket, TicketTransition.Resolve);

// Resolved → Closed
result = await machine.TransitionAsync(ticket, TicketTransition.Close);

// A context's state and status are read from the store, not from the machine
var status = await machine.GetStatusAsync(ticket);
Console.WriteLine($"{status.State} after {status.Step} transitions, {status.OperationalStatus}");
// Closed after 3 transitions, Operative
```

### Keys

Each context is stored, and locked, under `{KeyPrefix}:{MachineName}:{context id}`; the ticket
above is under statemachine:TicketMachine:1. `MachineName` defaults to the machine's type name with
its type arguments, so two machine types never share a record for the same id. Set either part in
`StateMachineOptions` (`MachineName = "tickets"`); an empty part is left out. Set `MachineName` when
two machine types in different namespaces have the same name, and before renaming a machine type
whose records you keep. `machine.GetContextKey(id)` returns the key.

### Cancellation

Every `TransitionAsync` takes a `CancellationToken`. A cancelled token stops the transition with
`OperationCanceledException` while it waits for the lock, reads the context, runs a guard or runs
the exit action: guards and actions get the token, and an exit action that throws on it has done
what it did before the throw while the state stays old. Once the exit action has run, the new state
is stored whether or not the token is cancelled. The enter and after-transition actions get the
token too; one that throws on it leaves the new state stored and returns a failed result with
`StateStored` set, like any other throw from them, and a state the after-transition action would
have returned is not stored.

---

## Invalid Transitions

By default (`StateMachineOptions.AllowImplicitSelfTransitions = true`), calling a transition
that isn't defined for the current state is **not** an error — it's treated as a no-op
self-transition (`result.Success == true`, state unchanged). To make undefined transitions
fail instead, disable that option:

```csharp
var strictOptions = new StateMachineOptions { AllowImplicitSelfTransitions = false };
var machine = new TicketMachine(lockAndStore, lockAndStore, strictOptions);

// Cannot Resolve before Assigning (Open → Resolved not defined)
var result = await machine.TransitionAsync(ticket, TicketTransition.Resolve);
Console.WriteLine(result.Success);       // false
Console.WriteLine(result.ErrorMessage);  // "Invalid transition 'Resolve' from state 'Open'"
```

---

## Guard Conditions

Block transitions based on runtime state. Guards are evaluated before the
transition executes. A guard gets the context being transitioned and the transition's
`CancellationToken`; it casts the context to the machine's own context type:

```csharp
protected override Action<ITransitionBuilder<TicketState, TicketTransition>> Transitions => b => b
    .From(TicketState.InProgress)
        .On(TicketTransition.Resolve)
        .To(TicketState.Resolved)
        .When((ctx, _) => !string.IsNullOrEmpty(((TicketContext)ctx).ResolutionNote));
```

One machine serves every context, often several at once, so a guard reads what it checks from
the context, never from a field of the machine: a field holds whichever context set it last.

`.When(...)` (and the async `.WhenAsync(...)`) answer only true or false — there's no way to
attach a custom message to a specific guard. A failed guard always reports the same
`ErrorMessage`:

```csharp
ticket.ResolutionNote = null;
var result = await machine.TransitionAsync(ticket, TicketTransition.Resolve);
// result.Success == false
// result.ErrorMessage == "Transition guard condition not met"

ticket.ResolutionNote = "Fixed the bug";
result = await machine.TransitionAsync(ticket, TicketTransition.Resolve);
// result.Success == true
```

---

## Middleware Pipeline

Intercept every transition attempt with `ITransitionMiddleware<TContext, TState, TTransition>`:

```csharp
using Ananke.StateMachine.Middleware;

machine.UseMiddleware(new LoggingMiddleware<TicketContext, TicketState, TicketTransition>(
    msg => Console.WriteLine($"  ~ {msg}")));
```

The middleware runs on every transition of a context that is Operative — both
successful and failed — making it ideal for logging, metrics, auditing, and
validation. It runs inside the context's lock, after the context is read, so its
`currentState` is the context's stored state. It must not call `TransitionAsync`,
`FaultAsync` or `ResetAsync` for the same context: each takes that lock, which is
held, and fails once its retries run out. A transition refused because the lock wasn't acquired, the
store couldn't be read or the context is Faulted doesn't reach the middleware.

A middleware of your own implements `InvokeAsync(context, transition, currentState, next, ct)` and
continues with `next(ct)`. `ct` is the `TransitionAsync` token: the lock is held until the middleware
returns, so pass it to anything the middleware awaits.

---

## Lifecycle Hooks

Run code when entering or exiting a state. Like a guard, each action (`OnEnter`, `OnExit`,
`WithAction`) gets the context being transitioned and the transition's token:

```csharp
protected override Action<ITransitionBuilder<TicketState, TicketTransition>> Transitions => b => b
    .From(TicketState.Open)
        .On(TicketTransition.Assign).To(TicketState.InProgress)
    .State(TicketState.Open)
        .OnExit((ctx, _) => { Console.WriteLine($"{ctx.Id} leaving Open"); return Task.CompletedTask; })
    .State(TicketState.InProgress)
        .OnEnter((ctx, _) => { Console.WriteLine($"{ctx.Id} entering InProgress"); return Task.CompletedTask; });
```

### When actions run, and what a failure leaves

A transition runs, in order: the guard, the exit action of the old state, **the write of the new
state**, the enter action of the new state, the after-transition action (`WithAction`). So:

| What throws | Stored state | `result.Success` | `result.StateStored` | `result.CurrentState` |
|---|---|---|---|---|
| Guard, exit action, or the write | old state | `false` | `false` | old state |
| Enter or after-transition action | new state | `false` | `true` | new state |

An exit action that throws has already done whatever it did before the throw; the state stays old.
An enter or after-transition action that throws leaves the context in the new state:
`result.ErrorMessage` says so, and `result.PreviousState` is the state it left.

---

## Circuit Breaking — Fault / Reset

`OperationalStatus` provides a circuit breaker per context. When a context is
faulted, **its transitions are refused** on every instance that shares the
store, until it is explicitly reset. Other contexts keep moving. Fault and reset
take the context's lock, so a transition running at the same time can't write
over the status:

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
Console.WriteLine(reset.CurrentStatus);  // Operative

// Transitions resume
result = await machine.TransitionAsync(ticket, TicketTransition.Assign);
Console.WriteLine(result.Success);  // true
```

`(await machine.GetStatusAsync(ticket)).OperationalStatus` reads a context's status, and
`OperationalStatusReason` the reason it was faulted.

---

## Distributed Locking

In production, use Redis for safe coordination across instances:

```bash
dotnet add package Ananke.Redis
```

```csharp
using Ananke.Redis;

// RedisDistributedLock implements both IDistributedLock and IKeyValueDataAdapter
var redisLock = new RedisDistributedLock(redisConnection);
var machine = new TicketMachine(redisLock, redisLock);
```

For dev/test:

```csharp
var lockAndStore = new InMemoryDistributedLock();
var machine = new TicketMachine(lockAndStore, lockAndStore);
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

← [Back to Learning Path](../learning-path.md)
