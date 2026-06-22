# Ananke.Abstractions — Architecture

> Zero-dependency foundation contracts shared by all Ananke packages.

## Role

Defines interfaces and value types that establish the contract boundaries between
Ananke projects. It is the base of the dependency graph: higher-level packages
build on these contracts, but this assembly does not depend on any other Ananke
project or external package.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `IAgentModel` — the core model contract for request/response generation with tool
   calls and multimodal messages — `src/Ananke.Abstractions/Agents/IAgentModel.cs`
2. `IHandoffChannel` — correlated request-response transport for inter-agent/workflow
   handoff — `src/Ananke.Abstractions/Channels/IHandoffChannel.cs`
3. `IKnowledgeGraph` — provenance-aware graph substrate for topology and learning
   projections — `src/Ananke.Abstractions/Graph/IKnowledgeGraph.cs`
4. `IWorkflowTracer` — entry point for workflow execution traces and nested spans —
   `src/Ananke.Abstractions/Tracing/IWorkflowTracer.cs`

---

## Dependencies

None. This project has zero NuGet or project references.

## Namespace → Folder Map

| Namespace | Contents |
|-----------|----------|
| `Ananke.Abstractions` | `IBaseContext`, `ITimestamped`, `IInterruptSink`, shared JSON/source-name helpers |
| `Ananke.Abstractions.Agents` | `IAgentModel`, `IAudioModel`, `IEmbeddingModel`, `AgentRequest`, `AgentResponse`, `AgentMessage`, `AgentToolCall`, `AgentStreamChunk`, `ContentPart`, `TokenUsage`, `AudioOptions` |
| `Ananke.Abstractions.Channels` | `IChannelReader<M>`, `IChannelReader<M,A>`, `IChannelWriter`, `IChannelWriter<A>`, `IHandoffChannel`, `IBackgroundWorker<T>`, `IBackgroundWorker<T,A>`, `HandoffChannel`, in-memory reader/writer helpers |
| `Ananke.Abstractions.Config` | `ChannelConfig`, `CacheConfig` |
| `Ananke.Abstractions.Distributed` | `IDistributedLock`, `IKeyValueDataAdapter`, `InMemoryDistributedLock` |
| `Ananke.Abstractions.Extensions` | `JsonExtensions` |
| `Ananke.Abstractions.Memory` | `IConversationMemory` |
| `Ananke.Abstractions.Tools` | `IToolMemory`, `ToolMemoryEntry`, `ToolHealth` |
| `Ananke.Abstractions.Tools.Routing` | `ISmartToolRouter`, `ToolRoutingRequest`, `ToolRoutingDecision`, `RoutingConfidence`, `InvalidRoutingDecisionException` |
| `Ananke.Abstractions.Providers` | `ICredentialProvider`, `IJsonSchemaTranslator`, `IModelMapper`, `ISystemPromptCompiler`, `IToolSchemaTranslator`, `SystemPromptBuilder` — provider-agnostic credential resolution and schema/prompt translation contracts; implementations live in each `Ananke.Orchestration.{Provider}` package |
| `Ananke.Abstractions.Graph` | `IKnowledgeGraph`, `GraphNode`, `GraphEdge`, `EdgeProvenance`, `ICentralityScorer`, `ICommunityDetector`, graph algorithms |
| `Ananke.Abstractions.Tracing` | `IWorkflowTracer` |

## Key Types

| Type | Kind | Purpose | Source |
|------|------|---------|--------|
| `IAgentModel` | Interface | Core model contract for request/response generation with tool calls and multimodal messages | `src/Ananke.Abstractions/Agents/IAgentModel.cs` |
| `AgentMessage` | Sealed record | Multimodal message (text, images, audio, tool calls) shared by orchestration, memory, and providers | `src/Ananke.Abstractions/Agents/AgentMessage.cs` |
| `IChannelReader<M,A>` | Interface | Subscribe to action-routed channel topics and deliver typed enum actions with messages | `src/Ananke.Abstractions/Channels/IChannelReader.cs` |
| `IChannelWriter<A>` | Interface | Publish messages to a transport with typed action routing | `src/Ananke.Abstractions/Channels/IChannelWriter.cs` |
| `IHandoffChannel` | Interface | Correlated request-response transport for inter-agent/workflow handoff | `src/Ananke.Abstractions/Channels/IHandoffChannel.cs` |
| `IDistributedLock` | Interface | Distributed mutex abstraction for cross-process coordination | `src/Ananke.Abstractions/Distributed/IDistributedLock.cs` |
| `IConversationMemory` | Interface | Persistent conversation history keyed by session ID | `src/Ananke.Abstractions/Memory/IConversationMemory.cs` |
| `IToolMemory` | Interface | Semantic memory/index of available tools and their health | `src/Ananke.Abstractions/Tools/IToolMemory.cs` |
| `IKnowledgeGraph` | Interface | Provenance-aware graph substrate for topology and learning projections | `src/Ananke.Abstractions/Graph/IKnowledgeGraph.cs` |
| `IWorkflowTracer` | Interface | Entry point for workflow execution traces and nested spans | `src/Ananke.Abstractions/Tracing/IWorkflowTracer.cs` |

## Design Rules

- **No types from other assemblies** may be placed in `Ananke.Abstractions` namespaces
- In-memory implementations (e.g. `InMemoryDistributedLock`, `InMemoryKnowledgeGraph`) are provided to enable tests and local execution without infrastructure
- Contracts here must stay vendor-agnostic and orchestration-agnostic
- `ChannelConfig` captures shared transport concepts; concrete adapters may introduce richer provider-specific options outside this package
