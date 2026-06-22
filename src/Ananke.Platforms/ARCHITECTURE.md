# Ananke.Platforms — Architecture

> Conversational adapter contracts for bridging messaging platforms
> (Slack, Discord, Teams) to Ananke agent workflows.

## Role

Defines the platform-agnostic adapter layer that sits between messaging
platforms and Ananke's orchestration engine. Normalizes incoming messages
into `PlatformMessage`, provides a response sink interface, and includes
`StreamingMessageBridge` for the post-then-edit streaming pattern used
by platforms that don't support true server-push.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `IMessagePlatformAdapter` — platform connection lifecycle (`StartAsync`, `StopAsync`,
   `IsConnected`); the contract every platform package implements — `src/Ananke.Platforms/IMessagePlatformAdapter.cs`
2. `IPlatformResponseSink` — send responses back to the platform (`SendMessageAsync`,
   `UpdateMessageAsync`, `SendTypingAsync`, `AddReactionAsync`) — `src/Ananke.Platforms/IPlatformResponseSink.cs`
3. `IPlatformMessageHandler` — the business logic hook that routes messages to workflows —
   `src/Ananke.Platforms/IPlatformMessageHandler.cs`
4. `ConversationalMessageHandler` — session-aware, memory-integrated base handler wiring
   `StreamingChatWorkflow` + `IConversationMemory` + `StreamingMessageBridge` — `src/Ananke.Platforms/Sessions/ConversationalMessageHandler.cs`

---

## Dependencies

- `Ananke.Orchestration` (project) — for `StreamingChatWorkflow`, `IConversationMemory`, `ToolKit`, `IContextStrategy`
- `Microsoft.Extensions.Logging.Abstractions`

## Relationship to `Ananke.Abstractions.Channels`

| Package | Scope |
|---------|-------|
| `Ananke.Abstractions.Channels` | **Pub/sub transport** — `IChannelReader/Writer`, `IHandoffChannel` for machine-to-machine messaging (MQTT, Redis) |
| `Ananke.Platforms` | **Conversational adapters** — `IMessagePlatformAdapter`, `IPlatformResponseSink` for human-facing messaging platforms (Slack, Discord) |

Different concern levels. This package sits above the transport layer.

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `PlatformMessage` | Sealed record | Normalized incoming message: `ChannelId`, `ThreadId`, `UserId`, `UserName`, `Message` (AgentMessage), `PlatformMessageId`, `PlatformContext` |
| `IPlatformResponseSink` | Interface | Send responses back to the platform: `SendMessageAsync` (returns message ID), `UpdateMessageAsync`, `SendTypingAsync`, `AddReactionAsync` |
| `IMessagePlatformAdapter` | Interface | Platform connection lifecycle: `StartAsync`, `StopAsync`, `IsConnected`, `ResponseSink`. Implements `IAsyncDisposable`. |
| `IPlatformMessageHandler` | Interface | Business logic hook: `HandleAsync(PlatformMessage, IPlatformResponseSink, CancellationToken)` — route messages to workflows |
| `StreamingMessageBridge` | Sealed class | Bridges `StreamingChatWorkflow.OnTextDelta` to the post-then-edit pattern. Configurable debounce interval and thinking placeholder. |
| `StreamingBridgeOptions` | Sealed record | Configuration: `DebounceInterval` (default 300ms), `ThinkingPlaceholder` (default "…") |
| `ConversationalMessageHandler` | Abstract class | Session-aware, memory-integrated base handler. Wires `StreamingChatWorkflow` + `IConversationMemory` + `StreamingMessageBridge`. Subclasses override `SystemPrompt`, `GetSessionId()`, etc. |
| `SessionKeyBuilder` | Static class | Derives collision-free session keys from `PlatformMessage`: thread-scoped (`Build`) or user-scoped (`BuildPerUser`), with optional platform prefix. |

## Streaming Bridge Pattern

```
User message arrives
  → IPlatformMessageHandler.HandleAsync()
    → Create StreamingMessageBridge(sink, channelId, threadId)
    → StreamingChatWorkflow.OnTextDelta(delta => bridge.AppendAsync(delta))
      1st delta: Posts "…" placeholder → gets messageId
      Subsequent deltas: Debounced UpdateMessageAsync (edits in-place)
    → bridge.FinalizeAsync() — flush final text
```

## Extension Points

- Implement `IMessagePlatformAdapter` + `IPlatformResponseSink` for new platforms
- Implement `IPlatformMessageHandler` to customize workflow routing
- Extend `ConversationalMessageHandler` for session-managed handlers with custom prompts, tools, and memory scoping
- Override `GetSessionId()` for per-user, per-guild, or cross-platform session scoping
- `PlatformMessage.PlatformContext` carries platform-native objects for advanced scenarios

## Session Management

`ConversationalMessageHandler` (in `Ananke.Platforms.Sessions`) encapsulates the
standard pattern of bridging platform messages to `StreamingChatWorkflow`:

```
PlatformMessage arrives
  → GetSessionId(message)         // derive {channelId}:{threadId} key
  → SendTypingAsync               // show typing indicator
  → StreamingChatWorkflow.Create()
      .WithMemory(memory)         // auto-load/persist history
      .WithTools(tools)
      .OnTextDelta → bridge       // post-then-edit streaming
      .RunAsync(sessionId, [message])
  → bridge.FinalizeAsync()
```

Session keys are built by `SessionKeyBuilder`:

| Method | Format | Use case |
|--------|--------|----------|
| `Build(msg)` | `{channelId}:{threadId}` | Thread-scoped — shared history per thread |
| `Build(msg, "slack")` | `slack:{channelId}:{threadId}` | Cross-platform — prevents key collisions |
| `BuildPerUser(msg)` | `{channelId}:{userId}` | User-scoped — isolated history per user |
