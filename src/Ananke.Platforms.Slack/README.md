# Ananke.Platforms.Slack

[![NuGet](https://img.shields.io/nuget/v/Ananke.Platforms.Slack.svg)](https://www.nuget.org/packages/Ananke.Platforms.Slack)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

Slack adapter for Ananke - bridges Slack Bot events (Socket Mode and Events API) to Ananke agent workflows via `IMessagePlatformAdapter`. Supports streaming chat responses using the post-then-edit pattern with automatic debouncing.

## Install

```bash
dotnet add package Ananke.Platforms.Slack
```

## Prerequisites

1. Create a Slack App at [api.slack.com/apps](https://api.slack.com/apps)
2. Enable **Socket Mode** and generate an App-Level Token (`xapp-…`)
3. Add Bot Token Scopes: `chat:write`, `reactions:write`, `channels:history`, `groups:history`, `im:history`, `mpim:history`
4. Subscribe to bot events: `message.channels`, `message.groups`, `message.im`, `message.mpim`
5. Install the app to your workspace and copy the Bot User OAuth Token (`xoxb-…`)

## Quick start

```csharp
using Ananke.Platforms;
using Ananke.Platforms.Slack;

services.AddAnankeSlack(options =>
{
    options.BotToken = config["Slack:BotToken"]!;
    options.AppToken = config["Slack:AppToken"]!;
    options.UseSocketMode = true;
});

services.AddSingleton<IPlatformMessageHandler, MyAgentHandler>();
```

## What it registers

| Service | Implementation |
|---|---|
| `IMessagePlatformAdapter` | `SlackAdapter` — Socket Mode (WebSocket) or Events API (HTTP), dispatches incoming messages |
| `IPlatformResponseSink` | `SlackResponseSink` — `chat.postMessage` / `chat.update` / `reactions.add` via Slack Web API |
| `IHostedService` | `SlackHostedService` — starts/stops the adapter with the application host |

## Connection modes

| Mode | Config | Public URL required | Best for |
|---|---|---|---|
| **Socket Mode** | `UseSocketMode = true` + `AppToken` | No | Development, internal bots |
| **Events API** | `UseSocketMode = false` + `SigningSecret` | Yes | Production, high-traffic |

## Streaming behavior

Slack does not support server-push streaming into a message. The adapter uses the **post-then-edit** pattern:

1. On the first text delta, a placeholder message (`…`) is posted
2. As deltas arrive, the message is edited via `chat.update` (debounced at 300 ms)
3. On completion, a final edit flushes the complete text

## Thread support

Slack threads are handled automatically. Messages with a `thread_ts` are routed with `PlatformMessage.ThreadId` set, and responses are posted back into the same thread.

## Slash commands — limitations

Unlike Discord, where slash commands can be registered programmatically at runtime, **Slack slash commands have significant limitations** that prevent automatic `ToolKit` → `/command` bridging:

| Limitation | Detail |
|---|---|
| **No programmatic registration** | Slack requires slash commands to be manually configured in the App dashboard (api.slack.com/apps → Slash Commands). There is no API to create, update, or remove commands at runtime. Every tool must be added by hand. |
| **No structured parameters** | Slack slash commands accept a single free-text string after the command name (e.g., `/echo hello world`). There are no typed parameters, no autocomplete, and no validation. The handler must parse the text manually. |
| **3-second response timeout** | Slack's HTTP request must be acknowledged within 3 seconds. For tools that take longer, you must send an immediate acknowledgment and post the result later via `response_url` or `chat.postMessage`. |
| **No parameter autocomplete** | Discord shows parameter names, types, and descriptions in the command picker. Slack provides nothing — just a text input box. |
| **Separate handler registration** | SlackNet uses `ISlashCommandHandler` for slash commands, which is a different registration path from the `IEventHandler<MessageEvent>` used for messages. Commands and chat messages don't share a handler pipeline. |

### What works today

Slack chat messages (DMs and channel messages) flow through `SlackAdapter` → `IPlatformMessageHandler` → `StreamingChatWorkflow` with full streaming and tool support. The LLM invokes tools naturally during conversation — this covers the primary use case.

### Future possibilities

If Slack introduces a richer command model (or if demand justifies the manual-registration workflow), a future version could provide:

- A `SlackSlashCommandHandler` that maps incoming `/command text` to `ToolKit.Tools[name].ExecuteAsync`
- Helper scripts for bulk-configuring Slack commands from a `ToolKit` manifest
- `response_url`-based async responses for long-running tools

For now, the recommendation is to use **Discord for slash-command-style tool invocation** and **Slack for conversational agent workflows**.

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
