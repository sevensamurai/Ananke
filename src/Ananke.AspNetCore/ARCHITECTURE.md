# Ananke.AspNetCore — Architecture

> ASP.NET Core integration — SSE streaming, chat sessions,
> and state machine endpoint helpers.

## Role

Bridges Ananke's orchestration and state machine engines to ASP.NET Core
web applications. Provides Server-Sent Events (SSE) streaming for
`ChatSessionEvent`, `WorkflowEvent`, and state machine transitions,
plus session management for multi-turn chat.

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

| Type | Kind | Purpose |
|------|------|---------|
| `ChatSession<TState, TAction>` | Class | Manages session lifecycle — binds an SSE transport to `StreamingChatWorkflow`, maintains conversation history |
| `InMemorySessionStore` | Class | In-memory session storage with configurable cleanup |
| `SseResponseExtensions` | Static class | `HttpResponse.WriteSseEventAsync()` — low-level SSE helpers |
| `ChatSessionEventSseExtensions` | Static class | Maps `ChatSessionEvent` stream to SSE events |
| `StateMachineSseExtensions` | Static class | Maps state machine transitions to SSE events |
| `AgentModelFactory` | Class | Resolves `IAgentModel` from configuration (provider + model name + key) |
