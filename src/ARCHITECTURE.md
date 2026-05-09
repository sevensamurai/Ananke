# Ananke — Solution Architecture

> **Purpose of this file:** Machine-readable architecture reference for LLM/AI assistants.
> When working on this codebase, read this file first for the dependency graph,
> project roles, and key abstractions. Per-project `ARCHITECTURE.md` files contain
> detailed type inventories and integration points.

## Identity

| Property | Value |
|----------|-------|
| Name | Ananke |
| Type | .NET library suite for AI agent orchestration |
| Runtime | .NET 10.0 (libraries), .NET Standard 2.0 (analyzers) |
| Solution | `src/Ananke.slnx` |
| License | Apache-2.0 |
| Version | Single source in `Directory.Build.props` → `<VersionPrefix>` |

## Layer Map

```
┌──────────────────────────────────────────────────────────────────────────┐
│                          CLI / TOOLING LAYER                            │
│  nnke  ·  nnke-platform                               │
├──────────────────────────────────────────────────────────────────────────┤
│                         APPLICATION LAYER                               │
│  Ananke (meta-pkg)  ·  Ananke.Mesh (meta-pkg)                          │
│  Ananke.AspNetCore  ·  Ananke.Design                                    │
│  Ananke.Platforms.Slack  ·  Ananke.Platforms.Discord                   │
├──────────────────────────────────────────────────────────────────────────┤
│                        FEDERATION LAYER                                 │
│  Ananke.Federation  ·  Ananke.Federation.Anthropic                      │
│  Ananke.Federation.Azure  ·  Ananke.Federation.Google                   │
├──────────────────────────────────────────────────────────────────────────┤
│                         ORGANICS LAYER                                  │
│  Ananke.Organics  (Kernel, Division, Healing, Sensing, Lineage)         │
├──────────────────────────────────────────────────────────────────────────┤
│                       INTEGRATION LAYER                                 │
│  Ananke.MCP  ·  Ananke.A2A  ·  Ananke.Platforms                        │
│  Ananke.Skills  ·  Ananke.Documents                                     │
├──────────────────────────────────────────────────────────────────────────┤
│                      INTELLIGENCE LAYER                                 │
│  Ananke.Learning  ·  Ananke.Orchestration.OpenAI                        │
│  Ananke.Orchestration.Anthropic  ·  Ananke.Orchestration.Google         │
├──────────────────────────────────────────────────────────────────────────┤
│                          CORE LAYER                                     │
│  Ananke.Orchestration  ·  Ananke.StateMachine                           │
├──────────────────────────────────────────────────────────────────────────┤
│                      INFRASTRUCTURE LAYER                               │
│  Ananke.Redis  ·  Ananke.Qdrant  ·  Ananke.MQTT  ·  Ananke.OpenTelemetry│
├──────────────────────────────────────────────────────────────────────────┤
│                       FOUNDATION LAYER                                  │
│  Ananke.Abstractions  (zero dependencies)                               │
└──────────────────────────────────────────────────────────────────────────┘
```

## Dependency Graph (Mermaid)

```mermaid
graph TD
    subgraph Foundation
        ABS[Ananke.Abstractions]
    end

    subgraph Core
        OKN[Ananke.Orchestration.Knowledge]
        ORC[Ananke.Orchestration]
        SM[Ananke.StateMachine]
        ANZ[Ananke.Analyzers]
    end

    subgraph Intelligence
        LRN[Ananke.Learning]
        OAI[Ananke.Orchestration.OpenAI]
        ANT[Ananke.Orchestration.Anthropic]
        GOO[Ananke.Orchestration.Google]
    end

    subgraph Infrastructure
        RED[Ananke.Redis]
        QDR[Ananke.Qdrant]
        MQTT[Ananke.MQTT]
        OTEL[Ananke.OpenTelemetry]
    end

    subgraph Integration
        MCP[Ananke.MCP]
        A2A[Ananke.A2A]
        PLT[Ananke.Platforms]
        SKL[Ananke.Skills]
        DOC[Ananke.Documents]
    end

    subgraph Organics
        ORG[Ananke.Organics]
    end

    subgraph Federation
        FED[Ananke.Federation]
        FANT[Ananke.Federation.Anthropic]
        FAZR[Ananke.Federation.Azure]
        FGOO[Ananke.Federation.Google]
    end

    subgraph Application
        META[Ananke - meta]
        MESH[Ananke.Mesh - meta]
        ASP[Ananke.AspNetCore]
        DSG[Ananke.Design]
        SLK[Ananke.Platforms.Slack]
        DIS[Ananke.Platforms.Discord]
    end

    subgraph CLI
        CLI1[nnke]
        CLI2[nnke-platform]
    end

    %% Foundation → Core
    OKN --> ABS
    ORC --> ABS
    ORC --> OKN
    ORC -.->|bundles at pack| ANZ
    SM --> ABS

    %% Foundation → LLM adapters
    OAI --> ABS
    ANT --> ABS
    GOO --> ABS

    %% Core → Intelligence
    LRN --> ORC

    %% Core → Infrastructure
    RED --> ABS
    RED --> ORC
    MQTT --> ABS
    OTEL --> ABS

    %% Core → Integration
    MCP --> ORC
    A2A --> ORC
    PLT --> ORC
    SKL --> ORC
    DOC --> OKN

    %% Intelligence + Integration → Organics
    ORG --> LRN
    ORG --> DSG

    %% Core → Design
    DSG --> ORC

    %% Organics → Federation
    FED --> ORG
    FED --> DSG
    FANT --> FED
    FANT --> ANT
    FAZR --> FED
    FAZR --> OAI
    FGOO --> FED
    FGOO --> GOO

    %% Infrastructure (Qdrant depends on Organics)
    QDR --> OKN
    QDR --> LRN
    QDR --> ORG

    %% Integration → Application
    SLK --> PLT
    DIS --> PLT
    DIS --> ORC
    ASP --> ORC
    ASP --> SM
    ASP --> ORG

    %% Meta-packages
    META --> ORC
    META --> SM
    MESH --> ORC
    MESH --> LRN
    MESH --> ORG
    MESH --> DSG

    %% CLI
    CLI1 --> DSG
    CLI1 --> ORG
    CLI2 --> DSG
    CLI2 --> FED
    CLI2 --> ORC
```

## Project Inventory

### Foundation

| Project | NuGet ID | Role | Dependencies |
|---------|----------|------|-------------|
| `Ananke.Abstractions` | `Ananke.Abstractions` | Zero-dep contracts: `IBaseContext`, `IDistributedLock`, `IKeyValueDataAdapter`, `IChannelReader/Writer`, `IHandoffChannel`, `AgentMessage`, `ContentPart`, `ChannelConfig`, `IAgentModel`, `IStreamingAgentModel`, `AgentRequest`, `AgentResponse`, `AgentStreamChunk`, `TokenUsage`, `IEmbeddingModel` | None |

### Core

| Project | NuGet ID | Role | Dependencies |
|---------|----------|------|-------------|
| `Ananke.Orchestration` | `Ananke.Orchestration` | Workflow engine, tool system, streaming chat, agentic patterns, checkpointing, middleware | `Ananke.Abstractions`, `Ananke.Orchestration.Knowledge`, Polly |
| `Ananke.Orchestration.Knowledge` | `Ananke.Orchestration.Knowledge` | Knowledge pipeline — vector stores, document processing, chunking, embedding abstractions, knowledge catalog, document linking | `Ananke.Abstractions` |
| `Ananke.StateMachine` | `Ananke.StateMachine` | Distributed state machine with transition guards, middleware, fault/reset, channel-driven transitions | `Ananke.Abstractions` |
| `Ananke.Analyzers` | _(bundled in Orchestration)_ | Roslyn analyzer — validates `Job`/`Then`/`Decide` name references at compile time | .NET Standard 2.0, Microsoft.CodeAnalysis |

### Intelligence

| Project | NuGet ID | Role | Dependencies |
|---------|----------|------|-------------|
| `Ananke.Learning` | `Ananke.Learning` | Empirical memory, episodes, reward propagation, exploration strategies, skill packaging, entity-scoped memory, offline learning | `Ananke.Orchestration` |
| `Ananke.Organics` | `Ananke.Organics` | Organic workflow host: Kernel, Division (+ approval), Healing/pruning, Sensing/routing, Lineage, Snapshots | `Ananke.Learning`, `Ananke.Design` |
| `Ananke.Orchestration.OpenAI` | `Ananke.Orchestration.OpenAI` | `IStreamingAgentModel` via OpenAI ChatClient (also works with Ollama, LM Studio, vLLM, Azure OpenAI) | `Ananke.Abstractions`, OpenAI SDK |
| `Ananke.Orchestration.Anthropic` | `Ananke.Orchestration.Anthropic` | `IStreamingAgentModel` via Anthropic SDK | `Ananke.Abstractions`, Anthropic SDK |
| `Ananke.Orchestration.Google` | `Ananke.Orchestration.Google` | `IStreamingAgentModel` via Google GenAI SDK | `Ananke.Abstractions`, Google.GenAI |

### Infrastructure

| Project | NuGet ID | Role | Dependencies |
|---------|----------|------|-------------|
| `Ananke.Redis` | `Ananke.Redis` | `IDistributedLock` (RedLock), `IKeyValueDataAdapter`, `ICheckpointStore`, `IConversationMemory` via Redis | `Ananke.Abstractions`, `Ananke.Orchestration`, StackExchange.Redis, RedLock.net |
| `Ananke.Qdrant` | `Ananke.Qdrant` | `IKnowledgeStore`, `IEmpiricalMemory`, `IEpisodeStore`, `IKnowledgeCatalog` via Qdrant vector DB | `Ananke.Orchestration.Knowledge`, `Ananke.Learning`, `Ananke.Organics`, Qdrant.Client |
| `Ananke.MQTT` | `Ananke.MQTT` | `IChannelReader/Writer`, `IHandoffChannel` via MQTT (MQTTnet) | `Ananke.Abstractions`, MQTTnet, MessagePack |
| `Ananke.OpenTelemetry` | `Ananke.OpenTelemetry` | `IWorkflowTracer` via OpenTelemetry, OTLP export builder | `Ananke.Abstractions`, OpenTelemetry SDK |

### Integration

| Project | NuGet ID | Role | Dependencies |
|---------|----------|------|-------------|
| `Ananke.MCP` | `Ananke.MCP` | Expose `ToolKit`/`Workflow` as MCP server tools; import MCP tools into `ToolKit` | `Ananke.Orchestration`, ModelContextProtocol SDK |
| `Ananke.A2A` | `Ananke.A2A` | Agent-to-Agent protocol — call remote A2A agents as `IAgentModel`; expose workflows as A2A endpoints | `Ananke.Orchestration`, A2A SDK |
| `Ananke.Platforms` | `Ananke.Platforms` | Conversational adapter contracts: `IMessagePlatformAdapter`, `IPlatformResponseSink`, `IPlatformMessageHandler`, `PlatformMessage`, `StreamingMessageBridge` | `Ananke.Orchestration` |
| `Ananke.Skills` | `Ananke.Skills` | External skill catalog — discover, score, install CLI tools as `ToolDefinition` entries | `Ananke.Orchestration` |
| `Ananke.Documents` | `Ananke.Documents` | `IDocumentExtractor` for PDF, Markdown, plain text | `Ananke.Orchestration`, PdfPig, Markdig |

### Application

| Project | NuGet ID | Role | Dependencies |
|---------|----------|------|-------------|
| `Ananke` | `Ananke` | Meta-package — bundles StateMachine + Orchestration in one step | `Ananke.Orchestration`, `Ananke.StateMachine` |
| `Ananke.Mesh` | `Ananke.Mesh` | Meta-package — full growth-aware mesh story: Orchestration + Learning + Organics + Design | `Ananke.Orchestration`, `Ananke.Learning`, `Ananke.Organics`, `Ananke.Design` |
| `Ananke.AspNetCore` | `Ananke.AspNetCore` | SSE streaming, `ChatSession`, state machine endpoint helpers, organic-colony inspection endpoints | `Ananke.Orchestration`, `Ananke.StateMachine`, `Ananke.Organics`, ASP.NET Core |
| `Ananke.Design` | `Ananke.Design` | YAML DSL → `Workflow` import, Mermaid diagram export | `Ananke.Orchestration` |
| `Ananke.Platforms.Slack` | `Ananke.Platforms.Slack` | Slack adapter via SlackNet (Socket Mode + Events API) | `Ananke.Platforms`, SlackNet |
| `Ananke.Platforms.Discord` | `Ananke.Platforms.Discord` | Discord adapter via Discord.Net (Gateway) — **Phase 2, skeleton** | `Ananke.Orchestration`, `Ananke.Platforms`, Discord.Net |

### Federation

| Project | NuGet ID | Role | Dependencies |
|---------|----------|------|-------------|
| `Ananke.Federation` | `Ananke.Federation` | Core deployer, registry, credential provider, validator, and monitor abstractions for multi-provider agent deployment | `Ananke.Organics`, `Ananke.Design` |
| `Ananke.Federation.Anthropic` | `Ananke.Federation.Anthropic` | Claude federation adapter — deploy/teardown Claude-backed agents | `Ananke.Federation`, `Ananke.Orchestration.Anthropic` |
| `Ananke.Federation.Azure` | `Ananke.Federation.Azure` | Azure OpenAI federation adapter | `Ananke.Federation`, `Ananke.Orchestration.OpenAI` |
| `Ananke.Federation.Google` | `Ananke.Federation.Google` | Google GenAI federation adapter | `Ananke.Federation`, `Ananke.Orchestration.Google` |

### CLI Tools

CLI packages use a **namespace exception** — see [CLI Namespace Exception](#cli-namespace-exception) below.

| Project | Tool ID | Role | Dependencies |
|---------|---------|------|-------------|
| `nnke` | `nnke` | `dotnet ananke` global tool — agent scaffold, design pipeline, organics commands | `Ananke.Design`, `Ananke.Organics`, ModelContextProtocol, System.CommandLine |
| `nnke-platform` | `nnke-platform` | `dotnet ananke-platform` global tool — federation deploy/teardown/status commands | `Ananke.Design`, `Ananke.Federation`, `Ananke.Orchestration`, System.CommandLine |

## Key Abstractions (Cross-Project)

### Agent Model Pipeline

```
IAgentModel / IStreamingAgentModel           (Ananke.Abstractions)
  ├── OpenAIChatAgentModel                   (Ananke.Orchestration.OpenAI)
  ├── AnthropicAgentModel                    (Ananke.Orchestration.Anthropic)
  ├── GoogleAgentModel                       (Ananke.Orchestration.Google)
  ├── A2AAgentModel                          (Ananke.A2A)
  ├── MiddlewareAgentModel                   (Ananke.Orchestration — wraps with middleware)
  ├── CachingAgentModel                      (Ananke.Orchestration)
  └── ResilientAgentModel                    (Ananke.Orchestration — Polly retry)
```

### Workflow Engine

```
Workflow<TState>                             (Ananke.Orchestration)
  ├── Job / AgentJob / HandoffJob / SubFlowJob
  ├── Then / Decide / Chain / Fork / Join
  ├── AgenticPattern (ReviewCritique, IterativeRefinement)
  └── StreamingChatWorkflow                  (pre-built streaming agent loop)
```

### State Machine Engine

```
StateMachine<TState, TAction>                (Ananke.StateMachine)
  ├── TransitionBuilder (fluent guards + actions)
  ├── ITransitionMiddleware
  └── AbstractStateMachine<C, S, T, N>       (persistent, distributed)
```

### Memory & Knowledge

```
IConversationMemory                          (Ananke.Abstractions)
  ├── InMemoryConversationMemory             (Ananke.Orchestration)
  └── RedisConversationMemory                (Ananke.Redis)

IKnowledgeStore                              (Ananke.Orchestration)
  ├── InMemoryKnowledgeStore                 (Ananke.Orchestration)
  └── QdrantKnowledgeStore                   (Ananke.Qdrant)

IEmpiricalMemory                             (Ananke.Learning)
  ├── InMemoryEmpiricalMemory                (Ananke.Learning)
  └── QdrantEmpiricalMemory                  (Ananke.Qdrant)
```

### Channel & Transport

```
IChannelReader<M> / IChannelWriter<A>        (Ananke.Abstractions — pub/sub)
  ├── InMemoryChannelReader/Writer           (Ananke.Abstractions)
  └── MqttChannelReader/Writer               (Ananke.MQTT)

IHandoffChannel                              (Ananke.Abstractions — request/response)
  ├── InMemoryHandoffChannel                 (Ananke.Orchestration)
  └── MqttHandoffChannel                     (Ananke.MQTT)

IMessagePlatformAdapter                      (Ananke.Platforms — conversational)
  ├── SlackAdapter                           (Ananke.Platforms.Slack)
  └── DiscordAdapter                         (Ananke.Platforms.Discord — Phase 2)
```

## Build & Test

```bash
# Build everything
dotnet build src/Ananke.slnx

# Run all tests
dotnet test src/Ananke.slnx

# Test a specific project
dotnet test src/tests/Ananke.Orchestration.Tests
```

- `TreatWarningsAsErrors` is on globally — zero warnings allowed
- Roslyn analyzer (`Ananke.Analyzers`) is bundled into `Ananke.Orchestration` NuGet package
- Central Package Management via `Directory.Packages.props`

## CLI Namespace Exception

The repository rule requires namespaces to match assembly + folder path. CLI tool packages are an explicit documented exception:

- `nnke` → namespace root `Ananke.Tool`
- `nnke-platform` → namespace root `Ananke.Tool.Platform`

Rationale: CLI entry-point projects are never referenced as libraries. The `Tool` namespace segment signals clearly that these types are not part of the public library API and avoids accidental name collisions if types were ever co-located with library types.

## Roslyn Analyzer Packaging Note

`Ananke.Analyzers` is a Roslyn analyzer (`IsRoslynComponent=true`, `IsPackable=false`). It is **not** a runtime dependency. The `<ProjectReference>` in `Ananke.Orchestration.csproj` exists solely so the analyzer is packed as an analyzer asset inside the `Ananke.Orchestration` NuGet package. Consumers of `Ananke.Orchestration` receive the analyzer automatically; they do not reference `Ananke.Analyzers` directly.

## Coding Conventions

- File-scoped namespaces matching assembly + folder path
- `sealed record` for immutable data; `required` for mandatory fields
- Primary constructors for DI
- `IReadOnlyList<T>` / `IReadOnlyDictionary<TK, TV>` in public APIs
- XML doc comments on all public APIs
- NUnit + Shouldly for tests
- See `.github/copilot-instructions.md` for full rules
