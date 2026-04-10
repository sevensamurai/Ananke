# Ananke.Platforms

[![NuGet](https://img.shields.io/nuget/v/Ananke.Platforms.svg)](https://www.nuget.org/packages/Ananke.Platforms)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

Conversational platform adapter contracts for Ananke - defines the interfaces and base classes for bridging messaging platforms (Slack, Discord, etc.) to Ananke agent workflows.

## Install

```bash
dotnet add package Ananke.Platforms
```

> Most users will install a platform-specific package instead, which includes this package transitively:
> - [Ananke.Platforms.Slack](../Ananke.Platforms.Slack/README.md)
> - [Ananke.Platforms.Discord](../Ananke.Platforms.Discord/README.md)

## Core types

| Type | Purpose |
|------|---------|
| `IMessagePlatformAdapter` | Platform connection lifecycle - `StartAsync`, `StopAsync`, `IsConnected` |
| `IPlatformResponseSink` | Send responses: `SendMessageAsync`, `UpdateMessageAsync`, `SendTypingAsync`, `AddReactionAsync` |
| `IPlatformMessageHandler` | Business logic hook: route incoming messages to workflows |
| `PlatformMessage` | Normalized incoming message: `ChannelId`, `ThreadId`, `UserId`, `Message` |
| `StreamingMessageBridge` | Bridges `StreamingChatWorkflow.OnTextDelta` to the post-then-edit pattern |
| `ConversationalMessageHandler` | Abstract base class - session-aware, memory-integrated handler |
| `SessionKeyBuilder` | Derives collision-free session keys from platform message properties |

## ConversationalMessageHandler

The recommended way to handle platform messages. Eliminates boilerplate by wiring up `StreamingChatWorkflow` with conversation memory, streaming bridge, and session management.

### What it does automatically

1. Sends a typing indicator to the platform
2. Derives a session key from the message's channel/thread identifiers
3. Loads conversation history from `IConversationMemory`
4. Runs `StreamingChatWorkflow` with streaming bridged via post-then-edit
5. Persists new messages back to memory after completion

### Customization points

| Virtual member | Default | Override to |
|---|---|---|
| `SystemPrompt` | null | Set the LLM system prompt |
| `WorkflowName` | platform-chat | Customize trace/log identity |
| `StreamingOptions` | null (300ms debounce) | Adjust debounce interval or placeholder text |
| `ContextStrategy` | null | Apply sliding window or summarization |
| `SendTypingIndicator` | true | Disable the initial typing indicator |
| `GetSessionId(message)` | channelId:threadId | Add platform prefix, use per-user scoping |
| `ConfigureWorkflow(builder, message)` | pass-through | Add custom callbacks, metadata |

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
