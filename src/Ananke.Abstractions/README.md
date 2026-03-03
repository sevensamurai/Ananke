# Ananke.Abstractions

[![NuGet](https://img.shields.io/nuget/v/Ananke.Abstractions.svg)](https://www.nuget.org/packages/Ananke.Abstractions)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

Shared interfaces and contracts for the Ananke distributed framework. This is the common surface between core packages and infrastructure providers.

You rarely need to install this package directly. It is a transitive dependency of `Ananke.StateMachine`, `Ananke.Orchestration`, `Ananke.Redis`, `Ananke.MQTT`, and `Ananke.OpenTelemetry`.

## Install

```bash
dotnet add package Ananke.Abstractions
```

## What is included

### Distributed coordination

| Type | Description |
|---|---|
| `IDistributedLock` | Acquire / release named locks across processes |
| `IKeyValueDataAdapter` | Get / set / remove / exists for serialized key-value storage |
| `InMemoryDistributedLock` | Default in-process implementation (replaced by `Ananke.Redis` when added) |

### Messaging channels

| Type | Description |
|---|---|
| `IChannelReader<TContext, TAction>` | Subscribe to incoming messages, deserialize to typed context |
| `IChannelWriter<TAction>` | Publish typed action messages to a channel |
| `IHandoffChannel<TRequest, TResponse>` | Request-response handoff between agents |
| `IBackgroundWorker` | Lifecycle interface for long-running channel workers |

### Tracing

| Type | Description |
|---|---|
| `IWorkflowTracer` | Emit spans for workflow job start/end and state transitions |

### Configuration

| Type | Description |
|---|---|
| `CacheConfig` | Redis connection settings (host, port, password, lock expiry) |
| `ChannelConfig` | MQTT connection settings (host, port, namespace, credentials) |

### Utilities

| Type | Description |
|---|---|
| `IBaseContext` | Marker interface for MQTT message context types |
| `JsonExtensions` | `ToJson` / `FromJson` helpers built on `System.Text.Json` |

## Related packages

| Package | What it adds |
|---|---|
| `Ananke.StateMachine` | FSM engine built on `IDistributedLock` |
| `Ananke.Orchestration` | Workflow engine built on `IWorkflowTracer` |
| `Ananke.Redis` | Redis implementations of `IDistributedLock` and `IKeyValueDataAdapter` |
| `Ananke.MQTT` | MQTTnet implementations of `IChannelReader` / `IChannelWriter` |
| `Ananke.OpenTelemetry` | OpenTelemetry implementation of `IWorkflowTracer` |

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
