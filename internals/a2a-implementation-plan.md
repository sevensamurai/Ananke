# Implementation Plan: Ananke.A2A

> Implements [ADR-001: Adopt the A2A Protocol](./adr-001-adopt-a2a-protocol.md)

## Overview

Create an `Ananke.A2A` NuGet package that integrates the A2A protocol into Ananke, following the same pattern established by `Ananke.MCP`. The package enables Ananke agents to **call** remote A2A agents (client) and **be called** by external A2A clients (server).

## Phases

### Phase 1 — Project Scaffolding & Client Side

**Goal:** Enable any Ananke workflow to delegate to a remote A2A agent via `IAgentModel`.

#### 1.1 Project Setup

- [ ] Create `Ananke.A2A/Ananke.A2A.csproj` targeting `net10.0`
- [ ] Add NuGet dependency on `A2A` (pin to latest stable, e.g. `0.x.x`)
- [ ] Add project reference to `Ananke.Orchestration`
- [ ] Add to `Ananke.sln`
- [ ] Mirror package metadata conventions from `Ananke.MCP.csproj`

#### 1.2 A2A Client — `A2AAgentModel`

Implement `IAgentModel` and `IStreamingAgentModel` backed by `A2AClient`:

```
Ananke.A2A/
└── Client/
    ├── A2AAgentModel.cs
    └── A2AAgentModelOptions.cs
```

**`A2AAgentModel`** responsibilities:
- Wraps an `A2AClient` instance targeting a single remote agent endpoint
- `GenerateAsync()`:
  - Maps `AgentRequest` → A2A `MessageSendParams` (user messages → `TextPart`)
  - Calls `A2AClient.SendMessageAsync()`
  - Maps returned `Task.Artifacts[].Parts` → `AgentResponse.Text`
  - Maps `input_required` task state → special response or exception for multi-turn
- `GenerateStreamAsync()`:
  - Calls `A2AClient.SendStreamingMessageAsync()`
  - Yields `AgentStreamChunk` for each `TaskArtifactUpdateEvent`
  - Final chunk carries the assembled `AgentResponse`

**`A2AAgentModelOptions`**:
- `Uri AgentUrl` — the remote agent's A2A endpoint
- `HttpClient? HttpClient` — optional shared client
- `TimeSpan Timeout` — default request timeout
- `IReadOnlyList<string>? AcceptedOutputModes` — media types the client accepts

#### 1.3 Agent Discovery — `A2AAgentDiscovery`

```
Ananke.A2A/
└── Client/
    └── A2AAgentDiscovery.cs
```

- Wraps `A2ACardResolver` to fetch and cache `AgentCard` from a remote endpoint
- Provides `DiscoverAsync(Uri baseUri)` → `AgentCardInfo` (Ananke-friendly DTO)
- Maps `AgentCard.Skills` → descriptors usable by `CapabilityModelRouter`
- Maps `AgentCard.Capabilities` → feature flags (streaming, push notifications)

#### 1.4 Integration with `CapabilityModelRouter`

- Add extension method: `ModelRouter.WithA2AAgent(Uri endpoint)` that:
  1. Resolves the AgentCard
  2. Creates an `A2AAgentModel` from the card
  3. Registers a route predicate based on the card's skills/capabilities

### Phase 2 — Server Side

**Goal:** Expose any Ananke workflow or `StreamingChatWorkflow` as an A2A-compliant endpoint.

#### 2.1 ASP.NET Core Integration Project

- [ ] Create `Ananke.A2A.AspNetCore/Ananke.A2A.AspNetCore.csproj`
- [ ] Add NuGet dependency on `A2A.AspNetCore`
- [ ] Add project reference to `Ananke.A2A` and `Ananke.Orchestration`

Alternatively, this can live inside `Ananke.A2A` if the `A2A` NuGet package already includes the ASP.NET Core types. Evaluate package split at implementation time.

#### 2.2 Workflow ↔ TaskManager Adapter

```
Ananke.A2A/
└── Server/
    ├── WorkflowTaskAdapter.cs
    └── AgentCardBuilder.cs
```

**`WorkflowTaskAdapter`** responsibilities:
- Bridges `TaskManager.OnMessageReceived` → `Workflow<TState>.RunAsync()`
- Maps incoming A2A `Message` → `AgentRequest` → workflow execution
- Maps `WorkflowResult` / `ExecutionStatus` → A2A `Task` state transitions:

  | `ExecutionStatus` | A2A `TaskState` |
  |---|---|
  | Running | `working` |
  | Completed | `completed` |
  | Failed | `failed` |
  | (handoff awaiting input) | `input_required` |

- Stores execution results as A2A `Artifact` parts
- Supports both synchronous (complete immediately) and async (long-running) workflows

**`AgentCardBuilder`** — fluent builder to generate an `AgentCard`:
- `.WithName()`, `.WithDescription()`, `.WithVersion()`
- `.WithSkillsFrom(ToolKit toolkit)` — maps `ToolDefinition` names/descriptions → `AgentSkill`
- `.WithStreamingSupport()` — sets `Capabilities.Streaming = true`
- `.Build(string agentUrl)` → `AgentCard`

#### 2.3 DI Extensions

```
Ananke.A2A/
└── A2ABuilderExtensions.cs
```

Following the `Ananke.MCP` pattern (`AnankeMcpServerBuilderExtensions`):

```csharp
// Server side — expose a workflow as an A2A endpoint
app.MapA2A(taskManager, "/my-agent");

// Or via builder:
builder.Services.AddAnankeA2AServer(options => { ... });

// Client side — register an A2A agent as an IAgentModel
services.AddAnankeA2AClient(options =>
{
    options.AgentUrl = new Uri("https://remote-agent.example.com/a2a");
});
```

### Phase 3 — HandoffJob Integration

**Goal:** Enable `HandoffJob` to use A2A as a transport.

#### 3.1 `A2AHandoffChannel`

```
Ananke.A2A/
└── Channels/
    └── A2AHandoffChannel.cs
```

Implements `IHandoffChannel` using `A2AClient` as the transport:
- `PublishAsync()` → `A2AClient.SendMessageAsync()`
- `SubscribeAsync()` → polls `A2AClient.GetTaskAsync()` or uses streaming
- Correlation ID mapped to A2A `contextId`

This enables existing `HandoffJob`-based workflows to transparently delegate to remote A2A agents without code changes — just swap the channel implementation.

### Phase 4 — Demo & Tests

#### 4.1 Demo

- [ ] Create `demos/A2ADemo/` showing:
  - **Server**: An Ananke `StreamingChatWorkflow` exposed as an A2A agent
  - **Client**: Another Ananke workflow discovering and delegating to the server agent
  - Two-process setup demonstrating cross-agent communication

#### 4.2 Tests

- [ ] Create `tests/Ananke.A2A.Tests/`
- [ ] Unit tests for `A2AAgentModel` (mock `A2AClient` responses)
- [ ] Unit tests for `WorkflowTaskAdapter` (verify state mapping)
- [ ] Unit tests for `AgentCardBuilder`
- [ ] Integration test with in-process A2A server + client

## File Structure Summary

```
Ananke.A2A/
├── Ananke.A2A.csproj
├── Client/
│   ├── A2AAgentModel.cs              # IAgentModel + IStreamingAgentModel over A2A
│   ├── A2AAgentModelOptions.cs       # Configuration record
│   └── A2AAgentDiscovery.cs          # AgentCard resolution + caching
├── Server/
│   ├── WorkflowTaskAdapter.cs        # Workflow ↔ TaskManager bridge
│   └── AgentCardBuilder.cs           # Fluent AgentCard generation
├── Channels/
│   └── A2AHandoffChannel.cs          # IHandoffChannel over A2A
└── A2ABuilderExtensions.cs           # DI registration extensions

demos/A2ADemo/
├── A2ADemo.csproj
└── Program.cs

tests/Ananke.A2A.Tests/
├── Ananke.A2A.Tests.csproj
├── A2AAgentModelTests.cs
├── WorkflowTaskAdapterTests.cs
└── AgentCardBuilderTests.cs
```

## Dependency Graph

```
Ananke.A2A
├── Ananke.Orchestration  (project reference)
├── A2A                   (NuGet — core protocol)
└── A2A.AspNetCore        (NuGet — server-side, optional)

demos/A2ADemo
├── Ananke.A2A            (project reference)
└── Ananke.Orchestration.OpenAI  (or any model provider)
```

## Sequencing & Priority

| Phase | Priority | Effort | Dependencies |
|---|---|---|---|
| Phase 1 — Client | **P0** | ~2–3 days | None |
| Phase 2 — Server | **P1** | ~2–3 days | Phase 1 |
| Phase 3 — Handoff | **P2** | ~1 day | Phase 1 |
| Phase 4 — Demo & Tests | **P1** | ~2 days | Phase 1 + 2 |

**Recommended start:** Phase 1 (client) delivers immediate value — any Ananke workflow can call external A2A agents.

## Open Questions

1. **Package split**: Should server-side (ASP.NET Core dependency) be a separate `Ananke.A2A.AspNetCore` package, or bundled? Follow the same decision `A2A` / `A2A.AspNetCore` made upstream.
2. **Multi-part content**: Phase 1 supports `TextPart` only. When should file/data part mapping be added?
3. **Push notifications**: Deferred — is there a future scenario in Ananke that benefits from webhooks over streaming?
4. **SDK version pinning**: Pin to latest release at implementation time. Establish a policy for tracking updates (e.g. Dependabot).
5. **gRPC binding**: The A2A SDK currently focuses on JSON-RPC/HTTP. Should Ananke's server side also expose gRPC? Defer until SDK supports it.
