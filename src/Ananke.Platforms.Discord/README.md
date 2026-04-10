# Ananke.Platforms.Discord

[![NuGet](https://img.shields.io/nuget/v/Ananke.Platforms.Discord.svg)](https://www.nuget.org/packages/Ananke.Platforms.Discord)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

Discord adapter for Ananke - bridges Discord Gateway events to Ananke agent workflows via `IMessagePlatformAdapter`. Supports streaming chat responses using the post-then-edit pattern with automatic debouncing.

## Install

```bash
dotnet add package Ananke.Platforms.Discord
```

## Prerequisites

1. Create a Discord Application at [discord.com/developers](https://discord.com/developers/applications)
2. Under **Bot**, click **Reset Token** and copy the bot token
3. Under **Bot > Privileged Gateway Intents**, enable **Message Content Intent**
4. Under **OAuth2 > URL Generator**, select scopes `bot` + permissions `Send Messages`, `Read Message History`, `Add Reactions`
5. Use the generated URL to invite the bot to your server

## Quick start

```csharp
using Ananke.Platforms;
using Ananke.Platforms.Discord;

services.AddAnankeDiscord(options =>
{
    options.BotToken = config["Discord:BotToken"]!;
});

services.AddSingleton<IPlatformMessageHandler, MyAgentHandler>();
```

## What it registers

| Service | Implementation |
|---|---|
| `IMessagePlatformAdapter` | `DiscordAdapter` - connects via Discord Gateway (WebSocket), dispatches incoming messages |
| `IPlatformResponseSink` | `DiscordResponseSink` - sends/edits messages, typing indicators, reactions via Discord API |
| `IHostedService` | `DiscordHostedService` - starts/stops the adapter with the application host |

## Thread support

Discord threads are mapped automatically:

| Discord context | `PlatformMessage.ChannelId` | `PlatformMessage.ThreadId` |
|---|---|---|
| Regular channel message | Channel ID | `null` |
| Thread message | Parent channel ID | Thread channel ID |

## Slash commands — ToolKit → `/command` bridge

When `SlashCommandTools` is set, every tool in the kit is registered as a Discord slash command on startup. Users invoke tools directly — the LLM is not involved.

```csharp
var tools = new ToolKit("my-tools")
    .AddTool("current_time", "Returns the current UTC time.",
        () => ToolResult.Ok(DateTime.UtcNow.ToString("u")))
    .AddTool("echo", "Echoes input back.", b => b
        .Param("text", "The text to echo")
        .OnExecute(async args => ToolResult.Ok(args.Get("text"))));

services.AddAnankeDiscord(options =>
{
    options.BotToken = config["Discord:BotToken"]!;
    options.SlashCommandTools = tools;

    // Optional: register to a test guild for instant propagation during dev
    // (global commands can take up to an hour to appear)
    // options.TestGuildId = 123456789012345678;
});
```

This gives users `/current_time` and `/echo text:hello` in the Discord command picker with full autocomplete and type validation.

### How it works

| Step | Detail |
|---|---|
| **Registration** | On `Ready`, tools are registered via `BulkOverwriteGlobalApplicationCommandsAsync` (atomic — stale commands from previous runs are removed) |
| **Execution** | `SlashCommandExecuted` → extract args → `tool.ExecuteAsync(args)` → respond with result |
| **Timeout** | Uses `DeferAsync` + `FollowupAsync` — no 3-second limit on tool execution |

### Parameter type mapping

| `ToolParameter.JsonType` | Discord option type | Discord validates |
|---|---|---|
| `"string"` | String | Free text |
| `"integer"` | Integer | Whole numbers only |
| `"number"` | Number | Decimal numbers |
| `"boolean"` | Boolean | True/False toggle |

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
