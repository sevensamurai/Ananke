<!-- topic: distributed, tags: distributed, redis, mqtt, handoff, bridge, pub-sub -->
# 09 — Distributed Systems

Coordinate across processes with Redis distributed locking, MQTT pub/sub,
agent-to-agent handoff, and the Bridge layer that wires state machines into
workflows.

The distributed layer is not a rewrite — it is a wiring change. The same `Workflow<T>` that runs in-memory during development and tests runs across processes in production by swapping `InMemoryHandoffChannel` for `MqttHandoffChannel` and `InMemoryDistributedLock` for `RedisDistributedLock`. No topology changes, no business logic changes.

**Demo:** [PetAdoptionDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/05-applications/PetAdoptionDemo)

---

## Infrastructure Packages

| Package | What it provides |
|---|---|
| `Ananke.Redis` | `IDistributedLock` via RedLock.net · `IKeyValueDataAdapter` and `IConversationMemory` via StackExchange.Redis |
| `Ananke.MQTT` | `IChannelReader` / `IChannelWriter` via MQTTnet · MessagePack serialization |

Each has zero-config in-memory alternatives for dev/test. `Ananke.Redis` and `Ananke.MQTT` depend on
`Ananke.Abstractions` only. Workflow checkpoints in Redis use `KeyValueCheckpointStore` (in `Ananke.Orchestration`) over the
`IKeyValueDataAdapter` that `AddRedis` registers.

---

## Redis Distributed Lock

Safe coordination across multiple service instances:

```bash
dotnet add package Ananke.Redis
```

`RedisDistributedLock` takes a `CacheConfig` (host/port/credentials) via `IOptions<CacheConfig>`
and establishes the Redis connection lazily on first use, or uses an `IConnectionMultiplexer` the
host passes in:

```csharp
using Ananke.Abstractions.Config;
using Ananke.Redis;
using Microsoft.Extensions.Options;

var config = Options.Create(new CacheConfig { Host = "localhost", Port = 6379 });
var locker = new RedisDistributedLock(config);

// Use with a state machine — RedisDistributedLock implements both
// IDistributedLock and IKeyValueDataAdapter, so it serves as both arguments
var machine = new TicketMachine(locker, locker);

// Or use directly — IDistributedLock wraps the critical section in a callback
// rather than returning an acquire/release handle
var result = await locker.RunCoordinatedActionAsync("my-resource", async () =>
{
    // ... critical section ...
    return 0;
});
// result.LockAcquired / result.Success / result.Result
```

`RunCoordinatedActionAsync` tries the lock once: when another holder has it, `LockAcquired` is false and the action
doesn't run. `RunCoordinatedActionWithRetryAsync`, an extension method for any `IDistributedLock`, retries taking the
lock, `maxRetries` times with a delay that doubles, then returns `LockFailed`. Either way the action runs at most
once: an exception it throws comes back in `result.Exception`, and a cancelled token throws
`OperationCanceledException`. `InMemoryDistributedLock` behaves the same for callers that share one instance; two
instances don't exclude each other.

For dev/test, use the in-memory alternative:

```csharp
var locker = new InMemoryDistributedLock();
```

---

## Redis Key-Value Store

General-purpose key-value storage for caching, state, and more:

```csharp
using Ananke.Abstractions.Config;
using Ananke.Redis;
using Microsoft.Extensions.Options;

var adapter = new RedisDataAdapter(Options.Create(new CacheConfig { Host = "localhost", Port = 6379 }));

await adapter.SetValueAsync("key", "value");
var value = await adapter.GetValueAsync("key");
```

A read returns `null` only when the key does not exist. When Redis cannot answer (unreachable,
timed out, or a value that does not deserialize), every operation throws instead, so a failure is
never read as a missing key: a state machine transition fails without writing, and the persisted
state is unchanged once Redis is back.

`SetValueAsync(key, value, expiry)` stores a value that is gone after `expiry`, in Redis and in
`InMemoryDistributedLock` alike.

Credentials, TLS (`Ssl`), a `ConnectionString`, the `Database`, a `KeyPrefix` for every key and
lock name, and the connect, sync and async timeouts (`ConnectTimeoutMs`, `SyncTimeoutMs`,
`AsyncTimeoutMs`) are set on `CacheConfig`; values and locks share one connection, or the
`IConnectionMultiplexer` the host registers. See the `Ananke.Redis` README.

Used by `CachingAgentModel` for LLM response caching.

---

## MQTT Pub/Sub

Message-based communication between services:

```bash
dotnet add package Ananke.MQTT
```

```csharp
using Ananke.MQTT;
using Ananke.Abstractions.Config;

var mqtt = new MqttHandoffChannel();
await mqtt.ConfigureAsync(new ChannelConfig
{
    Host = "localhost",
    Port = 1883,
    Namespace = "handoff"
});
```

For dev/test, use the in-memory channel:

```csharp
var channel = new InMemoryHandoffChannel();
```

---

## Agent Handoff

Send a request to a remote agent (in another process or service) and wait for
the response. Uses `HandoffJob` with either MQTT or in-memory channels.

### Sender (orchestrator workflow)

```csharp
using Ananke.Orchestration.Jobs;

// HandoffJob sends TicketHandoff and expects SpecialistResult back
var handoffJob = Handoff.To<TicketState, TicketHandoff, SpecialistResult>(
    "specialist-queue",
    channel,
    createMessage: state => new TicketHandoff { TicketId = state.TicketId, Summary = state.Description },
    mapResult:     (state, result) => state with { Resolution = result.Resolution });
```

### Receiver (specialist service)

```csharp
// In-memory handler
channel.RegisterHandler<TicketHandoff, SpecialistResult>(
    "specialist-queue",
    async ticket =>
    {
        // ... process the ticket ...
        return new SpecialistResult
        {
            Resolution = $"Resolved: {ticket.Summary}",
            HandledBy = "specialist-agent-1"
        };
    });
```

With MQTT, the receiver runs in a separate process listening on the same queue.

---

## Bridge Layer — FSM in Workflows

The Bridge convenience layer wires state machine transitions into workflow jobs
with full type inference:

```csharp
using Ananke.Bridge;
using Ananke.Orchestration.Workflows;

// Define the FSM
var lockAndStore = new InMemoryDistributedLock();
var lifecycle = new TicketLifecycleMachine(lockAndStore, lockAndStore);

// Map workflow state to FSM context
TicketLifecycleContext FsmContext(TicketState s) =>
    new(long.Parse(s.TicketId[3..]));

// Wire FSM transitions as workflow jobs
var workflow = new Workflow<TicketState>("support-triage")
    .Job("classify", classifyJob)
    .StateMachineJob("fsm_triage", lifecycle, FsmContext, LifecycleAction.BeginTriage)
    .Job("escalate", escalateJob)
    .StateMachineJob("fsm_resolve", lifecycle, FsmContext, LifecycleAction.Resolve)
    .Job("notify", notifyJob)
    .StateMachineJob("fsm_close", lifecycle, FsmContext, LifecycleAction.Close)
    .Then("classify", "fsm_triage")
    .Then("fsm_triage", Workflow.Decide<TicketState>(s =>
        s.Category == "escalate" ? "escalate" : "auto_resolve"))
    // ... routing continues ...
    .Then("fsm_close", Workflow.End); // a multi-job workflow needs an explicit terminal edge
```

Jobs prefixed with `fsm_` fire state machine transitions. Business logic
lives in the other jobs. The Bridge extension infers all generic type parameters
— no manual `StateMachineTriggerJob<TWorkflowState, TContext, TState, TTransition, TNotification>`.

---

## Conversation Memory — Redis

Persist chat history across requests with Redis, from `Ananke.Redis`:

```bash
dotnet add package Ananke.Redis
```

```csharp
using Ananke.Redis;
using StackExchange.Redis;

var redis = await ConnectionMultiplexer.ConnectAsync("localhost:6379");
var memory = new RedisConversationMemory(redis, ttl: TimeSpan.FromHours(1));
```

Each append and its expiry go to Redis in one transaction. Workflow checkpoints go through
`KeyValueCheckpointStore` (`Ananke.Orchestration`), over the `IKeyValueDataAdapter` that `AddRedis` registers:
`new KeyValueCheckpointStore(sp.GetRequiredService<IKeyValueDataAdapter>())`.

Or in-memory for dev/test:

```csharp
using Ananke.Orchestration.Memory;

var memory = new InMemoryConversationMemory(ttl: TimeSpan.FromHours(1));
```

---

## Full Demo Architecture

The [PetAdoptionDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/05-applications/PetAdoptionDemo) shows distributed features
in a full-stack application:

- **MQTT handoff** — payment processing runs as a separate service (`dotnet run -- --payment-service`)
- **Redis conversation memory** — per-session chat history with TTL-based expiry
- **State machine** — adoption phases (`Searching → Paperwork → Payment → Done`) with interrupt support
- **Qdrant vector search** — knowledge base for pet data and shelter information
- **Web UI** — ASP.NET Core with streaming chat, interrupt, and payment endpoints

Run modes:
```bash
dotnet run                        # web app (main process)
dotnet run -- --payment-service   # standalone MQTT payment listener
```

Infrastructure (Docker):
```bash
cd src/demos/PetAdoptionDemo
docker compose up -d    # starts Qdrant, MQTT, and Redis
```

---

## What's Next

| Next guide | What you'll learn |
|---|---|
| [10 — Observability](10-observability.md) | OpenTelemetry tracing across distributed services |
| [11 — Advanced Agents](11-advanced-agents.md) | Production resilience and caching |

---

← [Back to Learning Path](../learning-path.md)
