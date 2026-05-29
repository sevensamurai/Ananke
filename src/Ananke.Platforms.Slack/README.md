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
3. Add Bot Token Scopes (see [Required Scopes](#required-scopes) below)
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

## Inbound events

### Slash commands

Enable with `SlackAdapterOptions.EnableSlashCommands = true` (requires `SigningSecret`).
Incoming payloads are normalised to `PlatformSlashCommand` and dispatched via
`IPlatformMessageHandler.OnSlashCommandAsync(...)`.

```csharp
services.AddAnankeSlack(options =>
{
    options.EnableSlashCommands = true;
    options.SigningSecret = config["Slack:SigningSecret"]!;
});
```

Map the endpoint (Events API mode only):

```csharp
app.MapAnankeSlackEvents(); // registers /slack/events, /slack/commands, /slack/interactivity
```

### Block-action / view-submission interactivity

Enable with `SlackAdapterOptions.EnableInteractivity = true`.
Payloads are normalised to `PlatformInteractionEvent` (`ActionId`, `Value`, `TriggerId`,
`UserId`, `ChannelId`, `ThreadId`) and dispatched via
`IPlatformMessageHandler.OnInteractionAsync(...)`.

Use [`SlackApprovalBlocks`](#approval-blocks) to render Approve / Revise / Reject buttons, and
`SlackApprovalCallback` (from `Ananke.Roles.Slack`) to bridge the interaction to a
`CallbackWorkReviewGate`.

### Assistant pane (Agents & AI Apps)

Enable with `SlackAdapterOptions.EnableAssistant = true`.
`assistant_thread_started` and `assistant_thread_context_changed` events are normalised
to `PlatformAssistantThreadEvent` and dispatched via
`IPlatformMessageHandler.OnAssistantThreadAsync(...)`.

```csharp
options.EnableAssistant = true;
options.AssistantStatusLabel = "thinking…"; // default
```

The response sink exposes two Assistant-specific helpers:

```csharp
await sink.SetAssistantStatusAsync(channelId, threadTs, "loading context…", ct);
await sink.SetSuggestedPromptsAsync(channelId, threadTs,
    new[] { "Summarise the backlog", "What needs review?" }, ct);
```

## Outbound operations

### Modals

```csharp
var slackSink = (ISlackResponseSink)responseSink;
await slackSink.OpenViewAsync(triggerId, modalView, ct);
await slackSink.UpdateViewAsync(viewId, modalView, ct);
```

### Message metadata

Attach traceability metadata on `chat.postMessage`:

```csharp
await slackSink.SendBlocksWithMetadataAsync(channelId, blocks,
    metadata: new SlackMessageMetadata { EventType = "ananke_review", Payload = ... }, ct);
```

### Approval blocks

`SlackApprovalBlocks.Build(workItem)` returns a Block Kit layout with Approve / Revise /
Reject buttons wired to the canonical action ids (`ananke_approve`, `ananke_revise`,
`ananke_reject`). Post the blocks, then use `SlackApprovalCallback` on the
`OnInteractionAsync` path to resolve the pending `IWorkReviewGate` decision.

### File upload mode

Control how large files are uploaded via `SlackAdapterOptions.UploadMode`:

| Value | Behaviour |
|---|---|
| `ExternalUrlV2` (default) | Uses `files.getUploadURLExternal` + `files.completeUploadExternal`. Retries automatically on `expired_url`. |
| `LegacyFilesUpload` | Falls back to the deprecated `files.upload` API for compatibility. |

## Required scopes

| Scope | Required for |
|---|---|
| `app_mentions:read` | Receiving `app_mention` events |
| `assistant:write` | Setting Assistant pane status and suggested prompts |
| `chat:write` | Posting messages |
| `chat:write.public` | Posting in channels the bot hasn't joined |
| `commands` | Receiving slash commands |
| `files:write` | Uploading files |
| `reactions:write` | Adding emoji reactions |
| `reactions:read` | Receiving `reaction_added` events |
| `channels:history` | Reading channel message history |
| `groups:history` | Reading private channel history |
| `im:history` | Reading DM history |
| `metadata.message:read` | Reading message metadata payloads |

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
