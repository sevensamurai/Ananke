# StateMachineDemo — Car Engine IoT (Distributed FSM)

A runnable demonstration of **Ananke's distributed state machine** applied to a car engine domain. It walks through four self-contained sections that progressively introduce more features, from in-memory channels through a live MQTT broker.

## State diagram

```
Parked --[Start]--> Running --[Drive]--> Moving
  ^                   ^                    |
  |               [Resume]             [Halt]
  |                   |                    |
  +---[Park]--- Idle <---------------------+
```

| State     | Meaning                          |
|-----------|----------------------------------|
| `Parked`  | Engine off, vehicle stationary   |
| `Running` | Engine on, vehicle stationary    |
| `Moving`  | Engine on, vehicle in motion     |
| `Idle`    | Engine on, vehicle stopped mid-trip |

| Transition | From      | To        | Guard                  |
|------------|-----------|-----------|------------------------|
| `Start`    | Parked    | Running   | —                      |
| `Drive`    | Running   | Moving    | `FuelLevel > 0`        |
| `Halt`     | Moving    | Idle      | —                      |
| `Resume`   | Idle      | Running   | —                      |
| `Park`     | Idle      | Parked    | —                      |

## Demo sections

### 1 · In-memory channel — full engine lifecycle

Creates an `InMemoryChannelReader` / `InMemoryChannelWriter` pair and a `StateMachineChannelWorker` bridge. Drives `CAR-001` through a complete trip (`Parked → Running → Moving → Idle → Parked`), then prints a `TripReporter` summary: engine sessions, trip segments, distance and engine time.

### 2 · Guard condition — fuel required to Drive

Starts `CAR-002` with `FuelLevel = 0`. The `Drive` transition is blocked by the guard. After a simulated refuel the guard passes and the trip completes normally.

### 3 · Fault / Reset — engine malfunction

Demonstrates `FaultAsync` and `ResetAsync` on a fresh `CarEngineStateMachine`. While the machine is faulted, all transitions return a blocked result. After `ResetAsync` the machine accepts transitions again.

### 4 · MQTT-driven transitions *(opt-in, requires Docker)*

Re-uses the same `CarEngineStateMachine` and `CarContext` over a real MQTT broker (`MqttChannelReader` / `MqttChannelWriter`). The section is skipped gracefully when the broker is unavailable.

## Running the demo

### Sections 1–3 (no broker required)

```bash
dotnet run --project demos/StateMachineDemo
```

### Sections 1–4 (MQTT broker required)

Start the broker first:

```bash
docker compose -f demos/StateMachineDemo/docker-compose.yml up -d
```

Then run:

```bash
dotnet run --project demos/StateMachineDemo -- --mqtt
```

Stop the broker when done:

```bash
docker compose -f demos/StateMachineDemo/docker-compose.yml down
```

## Project structure

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point; wires infrastructure and runs sections 1–4 |
| `CarEngineStateMachine.cs` | `AbstractStateMachine` subclass with transition table, lifecycle hooks and guard state |
| `MqttSection.cs` | Section 4 — MQTT-specific setup and sends |
| `TripReporter.cs` | Observer that records per-car trip metrics |
| `DemoConsole.cs` | Coloured console output helpers |
| `docker-compose.yml` | Eclipse Mosquitto 2 broker for section 4 |

## Key concepts illustrated

- **`AbstractStateMachine<TCtx, TState, TTransition, TNotification>`** — base class that manages state, the distributed lock and the key-value store.
- **`StateMachineChannelWorker`** — generic bridge between a channel reader and the FSM; no hand-written worker loop needed.
- **Guard conditions** — per-transition `.When(Func<bool>)` predicates evaluated at dispatch time.
- **Lifecycle hooks** — `.OnEnter` / `.OnExit` callbacks declared fluently in the transition builder.
- **Middleware** — `LoggingMiddleware` attached via `UseMiddleware` for cross-cutting concerns.
- **`FaultAsync` / `ResetAsync`** — operational status management; faulted machines reject all transitions.
- **Transport portability** — `InMemoryChannelReader/Writer` and `MqttChannelReader/Writer` share the same FSM and context type without modification.
