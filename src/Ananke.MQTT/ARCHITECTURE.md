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
```
