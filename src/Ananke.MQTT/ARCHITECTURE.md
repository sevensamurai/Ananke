# Ananke.MQTT — Architecture

> MQTT transport — pub/sub channels and handoff (request/response)
> via MQTTnet with MessagePack serialization.

## Role

Provides `IChannelReader/Writer` and `IHandoffChannel` implementations
over MQTT topics. Used for distributed state machine coordination,
inter-service messaging, and agent-to-agent handoffs.

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
