# ADR-006: Extract Reusable Web Patterns into Ananke.AspNetCore

| Field         | Value                                                          |
|---------------|----------------------------------------------------------------|
| **Status**    | Accepted — All priorities (P0–P3) implemented                  |
| **Date**      | 2025-07-26                                                     |
| **Authors**   | —                                                              |
| **Deciders**  | Ananke maintainers                                             |
| **Tags**      | aspnetcore, sse, integration, DX, library-extraction            |
| **Relates to**| ADR-005 (layered simplification), `StreamingChatWorkflow`, `ChatSessionEvent` |

---

## Context

Multiple demo projects (`PetAdoptionDemo`, `AgenticWebDemo`) and integration
tests duplicate the same ASP.NET Core patterns:

1. **SSE response setup** — setting `text/event-stream` + `no-cache` headers.
2. **SSE writing** — serialising JSON and writing `event: {name}\ndata: {json}\n\n`
   with a flush. Duplicated in 4+ locations across 2 demos.
3. **`ChatSessionEvent` → SSE bridge** — mapping `TextDeltaEvent`, `ToolCallEvent`,
   `ToolResultEvent`, `CompletedEvent`, `ErrorEvent` to named SSE events. Identical
   in `AdoptionSession.StreamAsync` and `ChatWithInterruptionTests.ConsumeStreamAsync`.
4. **`PatchOrphanedToolCalls`** — inserting synthetic tool results for cancelled
   tool calls so LLM APIs accept the history. Duplicated verbatim in
   `InterruptPhase.cs` and `ChatWithInterruptionTests.cs`.
5. **State-machine endpoint loop** — the `while` loop that awaits `CurrentWork`,
   retries on null (race during interrupt transitions), and breaks when done.
   Duplicated in `ChatEndpoint.cs` and `ChatWithInterruptionTests.RunEndpointLoop`.
6. **Provider configuration** — reading `Provider`, `ApiKey`, `Model` from
   `IConfiguration` and creating the appropriate `IStreamingAgentModel`.
7. **Session store** — `ConcurrentDictionary`-backed session management.

### The problem

Each new demo or real application re-implements these patterns, risking subtle
bugs (especially in the interrupt race-condition handling and orphaned tool-call
patching). The patterns are transport-specific (ASP.NET Core) so they don't
belong in `Ananke.Orchestration`, but they're framework-level concerns that
shouldn't live in demo code.

---

## Decision

Create **`Ananke.AspNetCore`** — a thin ASP.NET Core integration library that
provides reusable building blocks for Ananke-powered web applications.

### Dependency graph

```
Ananke.AspNetCore
  ├── FrameworkReference: Microsoft.AspNetCore.App
  ├── ProjectReference:   Ananke.Orchestration
  └── ProjectReference:   Ananke.StateMachine
```

### What goes into Ananke.AspNetCore

| Component | Namespace | Description |
|---|---|---|
| `SseResponseExtensions` | `Ananke.AspNetCore.Sse` | `EnableSse()` + `WriteSseAsync(name, data)` on `HttpResponse` |
| `ChatSessionEventSseExtensions` | `Ananke.AspNetCore.Sse` | `WriteSseAsync(IAsyncEnumerable<ChatSessionEvent>, HttpResponse)` |
| `StateMachineSseExtensions` | `Ananke.AspNetCore.Sse` | `RunSseLoopAsync` — the reusable await-CurrentWork loop |

### What moves to Ananke.Orchestration

| Component | Namespace | Description |
|---|---|---|
| `PatchOrphanedToolCalls` | `Ananke.Orchestration.Agents` | `AgentMessageExtensions.PatchOrphanedToolCalls(List<AgentMessage>)` |

### What stays in demos

| Component | Reason |
|---|---|
| Phase definitions (Search, Paperwork, Payment, Interrupt) | Domain-specific |
| State machine topology (`AdoptionMachine`, `TicketLifecycleMachine`) | Domain-specific |
| Domain tools (`StockTools`, `KnowledgeBootstrap`) | Domain-specific |
| Workflow job graphs (`TradeApprovalWorkflow`) | Domain-specific |
| Request/response models (`ChatRequest`, `ChatModels`) | Differ per demo (audio, sessions, etc.) |
| `ProviderSettings` / `AgentConfig` | Low duplication count (2 demos), provider-specific construction varies |

---

## Implementation plan

### P0 — Core SSE infrastructure (Ananke.AspNetCore)

1. **`Ananke.AspNetCore.csproj`** — project scaffold with framework ref + project refs.
2. **`SseResponseExtensions`** — `EnableSse()` and `WriteSseAsync(name, data)`.
3. **`ChatSessionEventSseExtensions`** — consumes `IAsyncEnumerable<ChatSessionEvent>`
   and writes SSE events to an `HttpResponse`.
4. **`StateMachineSseExtensions`** — reusable state-machine endpoint loop.
5. Refactor both web demos to consume the new library.

### P1 — PatchOrphanedToolCalls → Ananke.Orchestration

1. Move `PatchOrphanedToolCalls` to `AgentMessageExtensions` in `Ananke.Orchestration`.
2. Update `PetAdoptionDemo/InterruptPhase.cs` and `ChatWithInterruptionTests.cs` to
   call the library method.

### P2 — Provider configuration

1. Optional: `ProviderConfiguration` in `Ananke.AspNetCore` or leave in demos.

### P3 — Session store

1. Optional: Generic `ISessionStore<T>` + `InMemorySessionStore<T>`.

---

## Consequences

### Positive

- **Single source of truth** for SSE writing, event mapping, and interrupt-race handling.
- **Reduced onboarding cost** — new demos/apps get correct patterns for free.
- **Testable** — library methods can be unit-tested independently.
- **Clear layering** — ASP.NET Core concerns stay out of `Ananke.Orchestration`.

### Negative

- **New NuGet package** to maintain and version.
- **Coupling** — demos now depend on `Ananke.AspNetCore`, which is a larger surface.

### Neutral

- Demo code becomes thinner, which is good for readability but means readers must
  look at the library to understand the full flow.
