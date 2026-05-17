# Ananke.Abstractions

[![NuGet](https://img.shields.io/nuget/v/Ananke.Abstractions.svg)](https://www.nuget.org/packages/Ananke.Abstractions)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

Zero-dependency contracts and foundational value types shared across the Ananke ecosystem.

This package defines the stable seams between orchestration, state management, memory, graph analytics, tracing, and infrastructure adapters. Most users consume it transitively via higher-level packages such as `Ananke.Orchestration`, `Ananke.StateMachine`, `Ananke.Redis`, and `Ananke.MQTT`.

## Install

```bash
dotnet add package Ananke.Abstractions
```

## What this package contains

### Agent contracts

| Type | Description |
|---|---|
| `IAgentModel` | Core request/response abstraction for agent-capable models |
| `IAudioModel` | Speech/audio generation model contract |
| `IEmbeddingModel` | Embedding generator used by knowledge and learning packages |
| `AgentRequest`, `AgentResponse`, `AgentStreamChunk` | Standard request/response/streaming payloads |
| `AgentMessage`, `ContentPart`, `AgentToolCall`, `TokenUsage` | Multimodal message model and tool-call metadata |

### Messaging and handoff channels

| Type | Description |
|---|---|
| `IChannelReader<M>` | Subscribe to one message stream and dispatch items to a background worker |
| `IChannelReader<M, A>` | Subscribe to action-routed topics and deliver parsed enum actions alongside messages |
| `IChannelWriter` | Send untyped payloads over a configured channel |
| `IChannelWriter<A>` | Send payloads with an associated action/transition enum |
| `IBackgroundWorker<T>` | Consume items delivered by a channel reader |
| `IBackgroundWorker<T, A>` | Consume items together with a parsed action enum |
| `IHandoffChannel` | Correlated request/response handoff between agents or workflow jobs |
| `HandoffChannel`, `BackgroundProcessor` | Factory and helper infrastructure for channel-backed workflows |

### Distributed coordination and configuration

| Type | Description |
|---|---|
| `IDistributedLock` | Cross-process mutex abstraction |
| `IKeyValueDataAdapter` | Serialized key/value persistence abstraction |
| `InMemoryDistributedLock` | In-process lock implementation for tests and single-process use |
| `CacheConfig` | Shared cache/lock configuration |
| `ChannelConfig` | Shared channel transport configuration |

### Conversation and tool memory

| Type | Description |
|---|---|
| `IConversationMemory` | Session-scoped conversation history for multi-turn agents |
| `IToolMemory` | Semantic index for available tools |
| `ToolMemoryEntry`, `ToolHealth` | Tool identity, health, and recall metadata |
| `ISmartToolRouter` | Strategy for narrowing or re-ranking tools before a model turn |
| `ToolRoutingRequest`, `ToolRoutingDecision`, `RoutingConfidence` | Tool-routing request/decision model |

### Graph substrate

`Ananke.Abstractions.Graph` provides a lightweight, provenance-aware graph model used by packages such as `Ananke.Learning` and `Ananke.Organics`.

| Type | Description |
|---|---|
| `IKnowledgeGraph` | Upsert nodes/edges, query neighbours, and run bounded BFS expansion |
| `GraphNode` | Immutable typed node with `Id`, `Kind`, and metadata |
| `GraphEdge` | Immutable typed edge with relation, provenance, weight, and metadata |
| `EdgeProvenance` | Source classification for edges such as extracted vs inferred |
| `ICentralityScorer` | Rank nodes by structural importance |
| `ICommunityDetector` | Detect graph communities/clusters |
| `InMemoryKnowledgeGraph` | In-process graph implementation |
| `DegreeCentralityScorer`, `PageRankCentralityScorer` | Built-in centrality algorithms |
| `BreadthFirstExpansion` | Shared BFS traversal helper |

See [`Graph/README.md`](./Graph/README.md) for graph-specific guidance.

### Tracing and shared helpers

| Type | Description |
|---|---|
| `IWorkflowTracer`, `ITrace`, `ISpan`, `SpanKind` | Minimal tracing model for workflow, LLM, and tool spans |
| `JsonExtensions`, `AnankeJson` | Shared JSON helpers and serializer settings |
| `AnankeSourceNames` | Common source-name constants used across integrations |
| `IBaseContext`, `ITimestamped`, `IInterruptSink` | Small shared contracts used across higher-level packages |

## Design goals

- **Zero dependencies** so every package in the solution can safely reference it
- **Stable public contracts** for infrastructure and provider packages
- **In-memory defaults where helpful** for testing and local development
- **No orchestration or provider behavior** in this assembly; only contracts and shared primitives

## Related packages

| Package | What it adds |
|---|---|
| `Ananke.Orchestration` | Workflow engine, tool execution, and agent orchestration built on these contracts |
| `Ananke.StateMachine` | Stateful transition engine built on distributed locking abstractions |
| `Ananke.Learning` | Episode tracking, empirical memory, and graph-based learning over the shared substrate |
| `Ananke.Organics` | Colony topology projection and graph-based mesh analysis |
| `Ananke.Redis` | Redis-backed distributed lock, key/value, and conversation-memory implementations |
| `Ananke.MQTT` | MQTT-backed channel readers, writers, and handoff implementations |
| `Ananke.OpenTelemetry` | OpenTelemetry-backed tracing implementation |

## Documentation

Full docs, demos, and package architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
