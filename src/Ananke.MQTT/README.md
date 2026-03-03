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
- **DI-friendly** — `IOptions<ChannelConfig>` pattern, or manual `ConfigureAsync()`

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
