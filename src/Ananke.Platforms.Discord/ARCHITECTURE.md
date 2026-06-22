# Ananke.Platforms.Discord — Architecture

> Discord adapter — bridges Discord Gateway events to Ananke agent workflows,
> with optional native slash-command tool invocation.

## Role

Implements `IMessagePlatformAdapter` and `IPlatformResponseSink` for Discord using
Discord.Net's WebSocket Gateway client. Normalizes incoming Discord messages into
`PlatformMessage`, provides response capabilities (post, edit, react), and registers
as an `IHostedService` for automatic start/stop. Can optionally expose a `ToolKit`'s
tools as native Discord slash commands, bypassing the LLM entirely for direct tool
invocation.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `DiscordAdapter` — `IMessagePlatformAdapter` — connects via the Discord Gateway
   (WebSocket) and dispatches to the handler — `src/Ananke.Platforms.Discord/DiscordAdapter.cs`
2. `DiscordResponseSink` — `IPlatformResponseSink` — maps to Discord `SendMessageAsync`,
   `ModifyAsync`, `AddReactionAsync` — `src/Ananke.Platforms.Discord/DiscordResponseSink.cs`
3. `DiscordMessageMapper` — converts `Discord.WebSocket.SocketMessage` to `PlatformMessage` —
   `src/Ananke.Platforms.Discord/DiscordMessageMapper.cs`

---

## Dependencies

- `Ananke.Platforms` (project) — `IMessagePlatformAdapter`, `IPlatformResponseSink`, `BoundedDispatcher`
- `Ananke.Orchestration` (project) — `ToolKit`, `ToolDefinition` (slash-command tool mapping)
- `Discord.Net.WebSocket` (NuGet — includes core Discord.Net)
- `Microsoft.Extensions.Hosting.Abstractions`

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `DiscordAdapter` | Sealed class | `IMessagePlatformAdapter` — connects via Gateway WebSocket, dispatches incoming messages through a `BoundedDispatcher`; also drives optional slash-command registration/handling |
| `DiscordResponseSink` | Internal sealed class | `IPlatformResponseSink` — `SendMessageAsync` / `UpdateMessageAsync` / `SendTypingAsync` / `AddReactionAsync`; encodes `channelId:messageId` as a composite ID |
| `DiscordAdapterOptions` | Sealed class | Configuration: `BotToken` (required), `GatewayIntents`, `StreamingOptions`, `SlashCommandTools` (optional `ToolKit`), `TestGuildId` |
| `DiscordMessageMapper` | Internal static class | `SocketMessage` → `PlatformMessage`; resolves thread channels to their parent channel + thread ID |
| `DiscordSlashCommandMapper` | Internal static class | `ToolDefinition` ↔ Discord slash command — builds `SlashCommandBuilder`s and extracts invocation args |
| `ServiceCollectionExtensions` | Static class | `services.AddAnankeDiscord(options => ...)` — registers `DiscordSocketClient`, `DiscordAdapter`, hosted service |
| `DiscordHostedService` | Internal sealed class | `IHostedService` — starts/stops the adapter with the host |

## Native Slash Commands (optional)

When `DiscordAdapterOptions.SlashCommandTools` is set, every `ToolDefinition` in that
`ToolKit` is registered as a Discord slash command on `Ready` — `/tool_name param:value`
invokes the tool directly; **the LLM is not involved**. This is a separate interaction
path from `IPlatformMessageHandler` (used for free-text messages).

- `TestGuildId` set → commands registered to that guild only (`BulkOverwriteApplicationCommandAsync`); propagate instantly — use during development
- `TestGuildId` unset → commands registered globally (`BulkOverwriteGlobalApplicationCommandsAsync`); can take up to an hour to propagate
- A tool parameter's `JsonType` maps to a Discord option type: `integer` / `number` / `boolean` map directly, everything else becomes `String`
- Tool/parameter names are lowercased and truncated to Discord's 32-character limit; descriptions truncate to 100 characters
- Responses defer immediately (`DeferAsync`) to avoid Discord's 3-second interaction timeout, then `FollowupAsync` with the tool result (truncated to Discord's 2000-character message limit)

## Message Flow (free-text)

```
Discord Gateway → MessageReceived event
  → DiscordAdapter.OnMessageReceivedAsync (filters bot/system messages)
    → BoundedDispatcher.Enqueue(...)
      → DiscordAdapter.DispatchAsync(SocketMessage)
        → DiscordMessageMapper.FromDiscordMessage() → PlatformMessage
        → IPlatformMessageHandler.HandleAsync(message, DiscordResponseSink)
          → (user code: StreamingChatWorkflow, etc.)
          → DiscordResponseSink.SendMessageAsync / UpdateMessageAsync
            → Discord.Net DiscordSocketClient → Discord Gateway/REST API
```

## DI Registration

```csharp
services.AddAnankeDiscord(options =>
{
    options.BotToken = config["Discord:BotToken"]!;
    options.TestGuildId = 123456789012345678; // optional — instant propagation during dev
});
services.AddSingleton<IPlatformMessageHandler, MyHandler>();
```
