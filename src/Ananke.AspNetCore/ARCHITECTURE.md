# Ananke.AspNetCore — Architecture

> ASP.NET Core integration — SSE streaming, chat sessions,
> and state machine endpoint helpers.

## Role

Bridges Ananke's orchestration and state machine engines to ASP.NET Core
web applications. Provides Server-Sent Events (SSE) streaming for
`ChatSessionEvent`, `WorkflowEvent`, and state machine transitions,
plus session management for multi-turn chat.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `ChatSession<TState, TAction>` — manages session lifecycle, binding an SSE transport to
   `StreamingChatWorkflow` and maintaining conversation history — `src/Ananke.AspNetCore/Sessions/ChatSession.cs`
2. `SseResponseExtensions` — low-level `HttpResponse.EnableSse()` / `.WriteSseAsync()`
   helpers that everything else in the package builds on — `src/Ananke.AspNetCore/Sse/SseResponseExtensions.cs`
3. `AgentModelFactory` — resolves an `IAgentModel` from configuration (provider + model
   name + key) — `src/Ananke.AspNetCore/Configuration/AgentModelFactory.cs`

---

## Dependencies

- `Ananke.Orchestration` (project)
- `Ananke.StateMachine` (project)
- `Microsoft.AspNetCore.App` (framework reference)

## Namespace → Folder Map

| Namespace | Contents |
|-----------|----------|
| `Ananke.AspNetCore.Sessions` | `ChatSession<TState, TAction>`, `InMemorySessionStore` |
| `Ananke.AspNetCore.Sse` | `SseResponseExtensions`, `ChatSessionEventSseExtensions`, `StateMachineSseExtensions` |
| `Ananke.AspNetCore.Configuration` | `AgentModelFactory` |

## Key Types

| Type | Kind | Purpose | Source |
|------|------|---------|--------|
| `ChatSession<TState, TAction>` | Class | Manages session lifecycle — binds an SSE transport to `StreamingChatWorkflow`, maintains conversation history | `src/Ananke.AspNetCore/Sessions/ChatSession.cs` |
| `InMemorySessionStore` | Class | In-memory session storage with configurable cleanup | `src/Ananke.AspNetCore/Sessions/InMemorySessionStore.cs` |
| `SseResponseExtensions` | Static class | `HttpResponse.EnableSse()` / `.WriteSseAsync(eventName, data)` — low-level SSE helpers | `src/Ananke.AspNetCore/Sse/SseResponseExtensions.cs` |
| `ChatSessionEventSseExtensions` | Static class | Maps `ChatSessionEvent` stream to SSE events | `src/Ananke.AspNetCore/Sse/ChatSessionEventSseExtensions.cs` |
| `StateMachineSseExtensions` | Static class | Maps state machine transitions to SSE events | `src/Ananke.AspNetCore/Sse/StateMachineSseExtensions.cs` |
| `AgentModelFactory` | Class | Resolves `IAgentModel` from configuration (provider + model name + key) | `src/Ananke.AspNetCore/Configuration/AgentModelFactory.cs` |
