# Ananke.MQTT — Architecture

> MQTT transport — pub/sub channels and handoff (request/response)
> via MQTTnet with MessagePack serialization.

## Role

Provides `IChannelReader/Writer` and `IHandoffChannel` implementations
over MQTT topics. Used for distributed state machine coordination,
inter-service messaging, and agent-to-agent handoffs.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `MqttChannelReader<TContext, TAction>` — `IChannelReader` over MQTT topics; subscribes and
   deserializes messages — `src/Ananke.MQTT/MqttChannelReader.cs`
2. `MqttChannelWriter<TAction>` — `IChannelWriter` over MQTT topics; publishes and serializes
   with MessagePack — `src/Ananke.MQTT/MqttChannelWriter.cs`
3. `MqttHandoffChannel` — `IHandoffChannel` implementing request/response over MQTT with
   correlation IDs — `src/Ananke.MQTT/MqttHandoffChannel.cs`
4. `ServiceCollectionExtensions` — `services.AddMqtt<TContext, TAction>(config)` DI registration,
   the usual starting point for wiring this package in — `src/Ananke.MQTT/ServiceCollectionExtensions.cs`

---

## Dependencies

- `Ananke.Abstractions` (project)
- `MQTTnet`
- `MessagePack`

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `MqttChannelReader<TContext, TAction>` | Class | `IChannelReader` — subscribes to MQTT topics, deserializes messages |
| `MqttChannelWriter<TAction>` | Class | `IChannelWriter` — publishes to MQTT topics, serializes with MessagePack |
| `MqttHandoffChannel` | Class | `IHandoffChannel` — request/response over MQTT with correlation IDs |
| `ServiceCollectionExtensions` | Static class | `services.AddMqtt<TContext, TAction>(config)` DI registration |
| `MqttChannelOptions` | Static class | `NameFor<TContext, TAction>()` — the name of the `ChannelConfig` options `AddMqtt` configures |

## Connection

Reader, writer and handoff channel each own one `MqttConnection`: it connects in `ConfigureAsync`
and runs one reconnect loop until the channel is disposed (or, for reader and writer, cleared). A
disconnect only signals the loop, which makes one attempt per `MqttReconnectDelay` (1 s, doubling,
capped at 30 s, less up to a quarter at random). After every connect the owner subscribes again:
the reader its topic, the handoff channel every request subscription and pending reply topic, at
`ChannelConfig.QualityOfService` (default exactly once), which publishes use too. The reader's
`IsConnected` means connected and subscribed: after a reconnect it stays false until the topic is
subscribed again. The writer's and the handoff channel's are the client's own state.

`ChannelConfig` also sets `UseTls` (the broker's certificate is validated against the machine's
trusted roots), `CleanSession` (default true; false needs a `ClientId`) and `ClientId`. A kept
session is asked for with the maximum session expiry interval (MQTT 5 ends a session at disconnect
without one), and a reader's dispose leaves its topic subscribed.

### Client ids

With a `ClientId` set, each client's id is `{ClientId cut to 14 characters}-{8 hex digits}`, the hex
digits the start of the SHA-256 of the whole `ClientId` and what the client is, joined by newlines:

| Client | Hashed after the `ClientId` |
|---|---|
| `MqttChannelReader<M, A>` | `reader`, the full names of `A` and `M`, the namespace |
| `MqttChannelWriter<A>` | `writer`, the full name of `A`, the namespace |
| `MqttHandoffChannel` | `handoff`, its `Name` when set, the namespace |

An id has at most 23 characters, the length the MQTT spec requires every broker to accept, and is
the same in every process, so a kept session is found again after a restart. Renaming a type or the
namespace changes the id; the broker keeps the session of the old one. `MqttConnection` holds
its id from construction until it is disposed; a second connection with an id in use in the process
throws `InvalidOperationException` naming it, rather than both clients disconnecting each other at
the broker. Two processes need two `ClientId`s. Without a
`ClientId`, MQTTnet's random id is used.

A received payload that can't be deserialized is logged and dropped. The reader hands each message
to its queue with the connection's lifetime token, so a full queue doesn't hold up a clear or a
dispose.

## Topic Convention

```
{namespace}/{action_enum_name}
```

Messages are serialized with MessagePack for compact binary transport.

## DI Registration

```csharp
services.AddMqtt<MyContext, MyAction>(options =>
{
    options.Host = "localhost";
    options.Port = 1883;
    options.Namespace = "myapp";
});

// Each AddMqtt configures its own named options; read them to configure the channels.
var config = provider.GetRequiredService<IOptionsMonitor<ChannelConfig>>()
    .Get(MqttChannelOptions.NameFor<MyContext, MyAction>());
await reader.ConfigureAsync(config, worker);
await writer.ConfigureAsync(config);
```
