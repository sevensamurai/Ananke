# 09 — Distributed Systems

Coordinate across processes with Redis distributed locking, MQTT pub/sub,
agent-to-agent handoff, and the Bridge layer that wires state machines into
workflows.

**Demo:** [DistributedServicesDemo](../../src/demos/DistributedServicesDemo/)

---

## Infrastructure Packages

| Package | What it provides |
|---|---|
| `Ananke.Redis` | `IDistributedLock` via RedLock.net · `IKeyValueDataAdapter` via StackExchange.Redis |
| `Ananke.MQTT` | `IChannelReader` / `IChannelWriter` via MQTTnet · MessagePack serialization |

Both have zero-config in-memory alternatives for dev/test.

---

## Redis Distributed Lock

Safe coordination across multiple service instances:

```bash
dotnet add package Ananke.Redis
```

```csharp
using Ananke.Redis;
using StackExchange.Redis;

var redis = await ConnectionMultiplexer.ConnectAsync("localhost:6379");
var locker = new RedisDistributedLock(redis);

// Use with a state machine
var machine = new TicketMachine(locker);

// Or use directly
await using var handle = await locker.AcquireAsync("my-resource", TimeSpan.FromSeconds(30));
// ... critical section ...
```

For dev/test, use the in-memory alternative:

```csharp
var locker = new InMemoryDistributedLock();
```

---

## Redis Key-Value Store

General-purpose key-value storage for caching, state, and more:

```csharp
using Ananke.Redis;

var adapter = new RedisDataAdapter(redis);

await adapter.SetAsync("key", "value", TimeSpan.FromMinutes(5));
var value = await adapter.GetAsync("key");
```

Used by `CachingAgentModel` for LLM response caching and by
`RedisConversationMemory` for chat history persistence.

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
    channel,
    "specialist-queue",
    mapOut:  state => new TicketHandoff { TicketId = state.TicketId, Summary = state.Description },
    mapIn:   (state, result) => state with { Resolution = result.Resolution });
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

// Define the FSM
var lifecycle = new TicketLifecycleMachine(new InMemoryDistributedLock());

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
    .Then("fsm_close", Workflow.End);
```

Jobs prefixed with `fsm_` fire state machine transitions. Business logic
lives in the other jobs. The Bridge extension infers all generic type parameters
— no manual `StateMachineTriggerJob<TWorkflowState, TContext, TState, TTransition, TNotification>`.

---

## Conversation Memory — Redis

Persist chat history across requests with Redis:

```csharp
using Ananke.Orchestration.Memory;

var memory = new RedisConversationMemory(redis, ttl: TimeSpan.FromHours(1));
```

Or in-memory for dev/test:

```csharp
var memory = new InMemoryConversationMemory(ttl: TimeSpan.FromHours(1));
```

---

## Full Demo Architecture

The [DistributedServicesDemo](../../src/demos/DistributedServicesDemo/) shows all five
features in one pipeline:

```
classify → fsm_triage → [decide] → escalate    ─┐
                                  → auto_resolve ─┤
                                                  │
fsm_resolve ──────────────────────────────────────┘
    │
  notify → fsm_close → End
```

Run modes:
```bash
dotnet run                    # single process (in-memory channels)
dotnet run -- --specialist    # specialist service (MQTT listener)
```

Infrastructure toggle via config:
```json
{
  "Mqtt": { "Host": "localhost", "Port": 1883 },
  "Redis": { "Host": "localhost", "Port": 6379 }
}
```

Empty host values fall back to in-memory implementations automatically.

---

## What's Next

| Next guide | What you'll learn |
|---|---|
| [10 — Observability](10-observability.md) | OpenTelemetry tracing across distributed services |
| [11 — Advanced Agents](11-advanced-agents.md) | Production resilience and caching |

---

← [Back to Learning Path](../learning.md)
