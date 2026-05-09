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

### Graph substrate (`Ananke.Abstractions.Graph`)

A small, zero-dependency typed graph used by `Ananke.Learning` and `Ananke.Organics` to project tag/episode/cell structure into a queryable shape.

| Type | Description |
|---|---|
| `IKnowledgeGraph` | Upsert nodes/edges, query neighbours, k-hop BFS expansion |
| `ICentralityScorer` | Rank nodes by centrality; default: `DegreeCentralityScorer` |
| `ICommunityDetector` | Detect topic/domain clusters; no default implementation in v1 |
| `GraphNode` | Immutable typed node with `Id`, `Kind`, and property bag |
| `GraphEdge` | Immutable typed edge with `Relation`, `Provenance`, `Weight` |
| `EdgeProvenance` | `Extracted` / `Inferred` / `Ambiguous` |
| `InMemoryKnowledgeGraph` | Default in-process implementation |
| `DegreeCentralityScorer` | Normalised in+out degree |
| `PageRankCentralityScorer` | Iterative PageRank (damping = 0.85) |

See [`Graph/README.md`](./Graph/README.md) for the full consumer guide, including why no external graph library was adopted.

## Related packages

| Package | What it adds |
|---|---|
| `Ananke.StateMachine` | FSM engine built on `IDistributedLock` |
| `Ananke.Orchestration` | Workflow engine built on `IWorkflowTracer` |
| `Ananke.Learning` | Knowledge-graph builders and analytics over the graph substrate |
| `Ananke.Organics` | Colony topology projection and god-node detection over the graph substrate |
| `Ananke.Redis` | Redis implementations of `IDistributedLock` and `IKeyValueDataAdapter` |
| `Ananke.MQTT` | MQTTnet implementations of `IChannelReader` / `IChannelWriter` |
| `Ananke.OpenTelemetry` | OpenTelemetry implementation of `IWorkflowTracer` |

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
