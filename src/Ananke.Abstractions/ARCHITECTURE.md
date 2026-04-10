# Ananke.Abstractions — Architecture

> Zero-dependency foundation contracts shared by all Ananke packages.

## Role

Defines interfaces and value types that establish the contract boundaries between
Ananke projects. No project except this one may be depended upon by every other
package — it is the leaf of the dependency graph.

## Dependencies

None. This project has zero NuGet or project references.

## Namespace → Folder Map

| Namespace | Contents |
|-----------|----------|
| `Ananke.Abstractions` | `IBaseContext` (entity identity), `IInterruptSink<T>` |
| `Ananke.Abstractions.Agents` | `AgentMessage`, `AgentRole`, `AgentToolCall`, `ContentPart` (text/image/audio) |
| `Ananke.Abstractions.Channels` | `IChannelReader<M>`, `IChannelWriter<A>`, `IHandoffChannel`, `IBackgroundWorker<T>`, `HandoffChannel` (factory), `InMemoryChannelReader/Writer` |
| `Ananke.Abstractions.Config` | `ChannelConfig`, `CacheConfig` |
| `Ananke.Abstractions.Distributed` | `IDistributedLock`, `IKeyValueDataAdapter`, `InMemoryDistributedLock` |
| `Ananke.Abstractions.Extensions` | `JsonExtensions` |
| `Ananke.Abstractions.Memory` | `IConversationMemory` |
| `Ananke.Abstractions.Tracing` | `IWorkflowTracer` |

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `IBaseContext` | Interface | Entity identity (`Id` string) — used as partition key for all persistent state |
| `AgentMessage` | Sealed record | Multimodal message (text, images, audio, tool calls). Factory methods: `User()`, `System()`, `Assistant()` |
| `ContentPart` | Abstract record | Base for `TextPart`, `ImagePart`, `AudioPart` — multimodal content |
| `IChannelReader<M>` | Interface | Subscribe to messages from a transport (MQTT, in-memory) |
| `IChannelWriter<A>` | Interface | Publish messages to a transport, routed by action enum `A` |
| `IHandoffChannel` | Interface | Request-response over topics with correlation IDs |
| `IDistributedLock` | Interface | Distributed mutex — implemented by Redis (RedLock) |
| `IKeyValueDataAdapter` | Interface | Key-value persistence — implemented by Redis |
| `IConversationMemory` | Interface | Store/retrieve conversation history by session ID |
| `IWorkflowTracer` | Interface | Span creation for workflow/job execution tracing |

## Design Rules

- **No types from other assemblies** may be placed in `Ananke.Abstractions` namespaces
- In-memory implementations (e.g. `InMemoryDistributedLock`) are provided for testing only
- `ChannelConfig` is MQTT-oriented; platform-specific adapters define their own options types
