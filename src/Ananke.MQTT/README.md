# Ananke.MQTT

[![NuGet](https://img.shields.io/nuget/v/Ananke.MQTT.svg)](https://www.nuget.org/packages/Ananke.MQTT)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

MQTT infrastructure for Ananke — `IChannelReader` and `IChannelWriter` backed by MQTTnet, with MessagePack binary serialization for efficient payloads.

## Install

```bash
dotnet add package Ananke.MQTT
```

## Quick start

```csharp
using Ananke.MQTT;

services.AddMqtt<MyContext, MyAction>(c =>
{
    c.Host = "localhost";
    c.Port = 1883;
    c.Namespace = "my-app";
});
```

Or with simple parameters:

```csharp
services.AddMqtt<MyContext, MyAction>("localhost", 1883, "my-app");
```

For a state machine, `UseMqtt` on the one-call `AddStateMachine<TImpl, TContext, TState, TTransition, TNotification>(b => b.UseMqtt(c => ...))`
runs `AddMqtt` for both its transition and its notification enum from one config (see the `Ananke.StateMachine` README).

Each `AddMqtt` configures its own named `ChannelConfig` options; pass them to `ConfigureAsync`:

```csharp
var config = provider.GetRequiredService<IOptionsMonitor<ChannelConfig>>()
    .Get(MqttChannelOptions.NameFor<MyContext, MyAction>());
await reader.ConfigureAsync(config, worker);
await writer.ConfigureAsync(config);
```

Over TLS, with a session the broker keeps:

```csharp
services.AddMqtt<MyContext, MyAction>(c =>
{
    c.Host = "broker.example.com";
    c.Port = 8883;
    c.UseTls = true;
    c.ClientId = "my-app-1";      // each client's id: its first 14 characters, '-', 8 hex digits of a hash
    c.CleanSession = false;       // needs a ClientId
    c.QualityOfService = ChannelQualityOfService.AtLeastOnce;  // default ExactlyOnce
});
```

## What it registers

| Service | Implementation |
|---|---|
| `IChannelReader<TContext, TAction>` | `MqttChannelReader` — subscribes to MQTT topics, deserializes with MessagePack |
| `IChannelWriter<TAction>` | `MqttChannelWriter` — publishes to MQTT topics, serializes with MessagePack |

## Features

- **Pub/sub messaging** — enum-based action types map to MQTT topic paths automatically
- **Binary serialization** — MessagePack for compact, fast payloads
- **Topic mapping** — `NamespaceMapper` converts `{namespace}/{ActionType}/{Action}` paths
- **Handoff channel** — `MqttHandoffChannel` for request-response patterns between agents
- **DI-friendly** — named `ChannelConfig` options per `AddMqtt`, or manual `ConfigureAsync()`
- **TLS, client id, session and QoS** — `UseTls`, `ClientId`, `CleanSession`, `QualityOfService` on `ChannelConfig`
- **A bad payload is dropped** — a message that can't be deserialized is logged, and the next one is delivered
- **Reconnects** — from `ConfigureAsync()` until disposed, each client retries a lost connection with
  one attempt per delay (1 s, doubling, capped at 30 s), and subscribes again on every connect

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
