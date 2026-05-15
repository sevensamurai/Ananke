### Step 6 — MQTT scale-out (was: `ChannelConfig.PerKeyBackpressure`)  ⚠️ Revert + recast
**Projects:** `Ananke.Abstractions`, `Ananke.MQTT`
**Blocks:** Seren per-device MQTT fairness

**Status note (2026-05-09):** The in-process per-key dispatcher
(`PerKeyDispatcher`, `PerKeyBackpressure`, `BackpressurePolicy`) was built and
its tests pass — but the design was reconsidered before merge. In-process
fairness is a single-node band-aid for what is fundamentally a *distribution*
problem: one process has one CPU/memory/network budget, and any backpressure
policy is just choosing which data to sacrifice once the wall is hit. The
framework should push that concern to the broker, which already solves it
natively (shared subscriptions, MQTT 5 flow control, session expiry), instead
of reimplementing a weaker version of it in the consumer.

**Revert tasks (on this branch, before 0.8.4 ships):**

1. Delete `Ananke.MQTT/PerKeyDispatcher.cs`.
2. Delete `Ananke.Abstractions/Channels/BackpressurePolicy.cs` and `Ananke.Abstractions/Channels/PerKeyBackpressure.cs`.
3. Remove `ChannelConfig.PerKey` and revert `MqttChannelReader.ConfigureAsync` to the single-`BackgroundProcessor` path.
4. Delete `tests/Ananke.MQTT.Tests` and remove its solution entry.
5. Drop the `InternalsVisibleTo` line from `Ananke.MQTT.csproj`.
6. Delete `tests/Ananke.Abstractions.Tests/Channels/PerKeyBackpressureTests.cs`.

**Replacement scope — scale-out, not in-process:**

| Task | File / location | Notes |
|---|---|---|
| `ChannelConfig.SharedSubscriptionGroup` | `Ananke.Abstractions/Config/ChannelConfig.cs` | Optional `string?`. When set, the MQTT reader subscribes as `$share/{group}/{topic}` so N replicas share work fairly via the broker. |
| `ChannelConfig.ReceiveMaximum` | same file | Optional `int?` exposing MQTT 5 flow control. Replaces an in-process bounded queue with the protocol's bounded queue. |
| `MqttChannelReader` wiring | `Ananke.MQTT/MqttChannelReader.cs` | Apply `SharedSubscriptionGroup` to the subscribe topic and `ReceiveMaximum` to connect options. No dispatchers, no extra threads, no TTL sweeps. |
| Tests | `tests/Ananke.Abstractions.Tests` | Config round-trip; topic-prefixing when `SharedSubscriptionGroup` is set. |
| Documentation | MQTT README section | "Per-key fairness → topic-per-key + broker routing, or sidecar consistent hashing." "Backpressure → shared subscriptions + autoscale on broker queue depth (KEDA)." |

Net result: ~20 lines of code, no new sync primitives, no operator-tuning knobs, real horizontal scaling. Per-key affinity, queue depth, autoscale, and session expiry stay where they belong — in the broker and deployment layer.

---

### Step 5b — Simplify event-time via `ITimestamped` marker  📌 Follow-up
**Projects:** `Ananke.Abstractions`, `Ananke.StateMachine`

**Background:** Step 5 introduced event-time correctly (event time belongs on the event, not on `IBaseContext`), but expressed it as a method parameter. That forced a new `TransitionAsync` overload, a default-interface-method on `ITransitionMiddleware`, and threading of `eventTime` through several internal methods — more surface than the concept warrants.

**Cleaner shape:** make event time an opt-in property of the payload itself.

```csharp
namespace Ananke.Abstractions;

public interface ITimestamped
{
    DateTimeOffset EventTime { get; }
}
```

In `AbstractStateMachine` the resolution is one line at entry:

```csharp
var eventTime = payload is ITimestamped t ? t.EventTime : DateTimeOffset.UtcNow;
```

Nothing leaks to public interfaces, nothing leaks to middleware, no new overloads. Callers that don't care: do nothing, get `UtcNow` as today. Callers that do care: implement one property on their payload type.

| Task | File / location | Notes |
|---|---|---|
| `ITimestamped` marker interface | `Ananke.Abstractions/ITimestamped.cs` | Single `DateTimeOffset EventTime` property. |
| Resolve at entry of `AbstractStateMachine` | `Ananke.StateMachine/AbstractStateMachine.cs` | Replace threaded `eventTime` parameter with payload check. |
| Drop `TransitionAsync(..., DateTimeOffset eventTime)` overload on `IActionStateMachine` | `Ananke.StateMachine/IStateMachine.cs` | Revert to two overloads (with and without payload). |
| Drop the default-interface-method `InvokeAsync(..., DateTimeOffset eventTime, ...)` on `ITransitionMiddleware` | `Ananke.StateMachine/Middleware/ITransitionMiddleware.cs` | Middleware reads `EventTimestamp` from the `TransitionResult` if needed. |
| **Keep** `TransitionResult<S>.EventTimestamp` | unchanged | Observers/audit logs still need the attributed time on the result. |
| Apply the same marker in MQTT envelopes | `Ananke.MQTT/IMqttContext.cs` (optional) | Lets `IMqttContext` carry broker-provided event time uniformly across subsystems. |
| Tests | `tests/Ananke.StateMachine.Tests` | Payload implementing `ITimestamped` flows through to `TransitionResult.EventTimestamp`; non-implementing payloads default to `UtcNow`. |

Small follow-up cleanup, also targeted at 0.8.4 since Step 5 has not yet shipped externally.
