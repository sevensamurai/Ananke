# Ananke.Platforms.Slack — Architecture

> Slack adapter — bridges Slack Bot events to Ananke agent workflows
> via Socket Mode (WebSocket) or Events API (HTTP).

## Role

Implements `IMessagePlatformAdapter` and `IPlatformResponseSink` for Slack
using the SlackNet library. Normalizes Slack `MessageEvent`s into
`PlatformMessage`, provides response capabilities (post, edit, react),
and registers as an `IHostedService` for automatic start/stop.

## Dependencies

- `Ananke.Platforms` (project)
- `SlackNet.Extensions.DependencyInjection` (NuGet — includes core SlackNet)
- `Microsoft.Extensions.Hosting.Abstractions`

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `SlackAdapter` | Sealed class | `IMessagePlatformAdapter` — connects via Socket Mode or Events API, dispatches to handler. Takes `ISlackServiceProvider` from DI. |
| `SlackResponseSink` | Internal class | `IPlatformResponseSink` — maps to `chat.postMessage`, `chat.update`, `reactions.add` |
| `SlackAdapterOptions` | Sealed class | Configuration: `BotToken`, `AppToken`, `UseSocketMode`, `SigningSecret`, `StreamingOptions` |
| `SlackMessageMapper` | Internal static class | Converts `SlackNet.Events.MessageEvent` → `PlatformMessage` |
| `SlackMessageEventHandler` | Internal class | `IEventHandler<MessageEvent>` (SlackNet) — bridges to `SlackAdapter.DispatchAsync` |
| `ServiceCollectionExtensions` | Static class | `services.AddAnankeSlack(options => ...)` — registers adapter, SlackNet DI, hosted service |
| `SlackHostedService` | Internal class | `IHostedService` — starts/stops the adapter with the host |

## Connection Modes

| Mode | Transport | Public URL Required | Config |
|------|-----------|-------------------|--------|
| Socket Mode | WebSocket | No | `BotToken` + `AppToken` |
| Events API | HTTP webhook | Yes | `BotToken` + `SigningSecret` (adapter is passive — external endpoint calls `DispatchAsync`) |

## DI Registration

```csharp
services.AddAnankeSlack(options =>
{
    options.BotToken = "xoxb-...";
    options.AppToken = "xapp-...";    // Socket Mode
    options.UseSocketMode = true;
});
services.AddSingleton<IPlatformMessageHandler, MyHandler>();
```

## Message Flow (Socket Mode)

```
Slack WebSocket → SlackNet Gateway
  → SlackMessageEventHandler.Handle(MessageEvent)
    → SlackAdapter.DispatchAsync(MessageEvent)
      → SlackMessageMapper.FromSlackEvent() → PlatformMessage
      → IPlatformMessageHandler.HandleAsync(message, SlackResponseSink)
        → (user code: StreamingChatWorkflow, etc.)
        → SlackResponseSink.SendMessageAsync / UpdateMessageAsync
          → SlackNet ISlackApiClient → Slack Web API
```
