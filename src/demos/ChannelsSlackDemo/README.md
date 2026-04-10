# ChannelsSlackDemo

Minimal Slack bot powered by Ananke's `StreamingChatWorkflow` and the `Ananke.Platforms.Slack` adapter. Messages sent to the bot are routed to an OpenAI model, and the response streams back using Slack's post-then-edit pattern with automatic debouncing. Connects via **Socket Mode** — no public URL required.

## Prerequisites

| Requirement | Notes |
|---|---|
| .NET 10 SDK | `dotnet --version` ≥ 10.0 |
| OpenAI API key | Any key with chat-completion access ([platform.openai.com/api-keys](https://platform.openai.com/api-keys)) |
| Slack workspace | You need permission to install apps |

## Slack setup (step-by-step)

### 1. Create a Slack App

1. Go to [api.slack.com/apps](https://api.slack.com/apps)
2. Click **Create New App → From scratch**
3. Name it (e.g. "Ananke Bot"), select your workspace, click **Create App**

### 2. Enable Socket Mode

1. In the left sidebar, click **Socket Mode**
2. Toggle **Enable Socket Mode** to ON
3. When prompted, name the App-Level Token (e.g. "socket-token") and add scope `connections:write`
4. Click **Generate** → **copy the `xapp-…` token** → click **Done**

### 3. Add Bot Token Scopes

1. In the left sidebar, click **OAuth & Permissions**
2. Scroll to **Scopes → Bot Token Scopes** and add:
   - `chat:write` — send and edit messages
   - `reactions:write` — add emoji reactions
   - `channels:history` — read public channel messages
   - `groups:history` — read private channel messages
   - `im:history` — read DM messages
   - `mpim:history` — read group DMs

### 4. Subscribe to Events

1. In the left sidebar, click **Event Subscriptions**
2. Toggle **Enable Events** to ON
3. Under **Subscribe to bot events**, add:
   - `message.channels` — messages in public channels
   - `message.groups` — messages in private channels
   - `message.im` — direct messages
   - `message.mpim` — group DMs
4. Click **Save Changes**

### 5. Install the App to your workspace

1. In the left sidebar, click **Install App**
2. Click **Install to Workspace** → **Allow**
3. **Copy the Bot User OAuth Token** (`xoxb-…`)

### 6. Create `secrets.json`

In the `demos/ChannelsSlackDemo/` directory, create a file named `secrets.json`:

```json
{
  "OpenAI": {
    "ApiKey": "sk-...",
    "Model": "gpt-4.1-mini"
  },
  "Slack": {
    "BotToken": "xoxb-your-bot-token",
    "AppToken": "xapp-your-app-level-token"
  }
}
```

> **Tip:** `secrets.json` is listed in `.gitignore` — it will not be committed.

You can also use environment variables instead:

```bash
export OpenAI__ApiKey="sk-..."
export Slack__BotToken="xoxb-..."
export Slack__AppToken="xapp-..."
```

## Run

```bash
cd src/demos/ChannelsSlackDemo
dotnet run
```

You should see:

```
═══════════════════════════════════════════════════════════
  Ananke — Slack Bot Demo (Socket Mode)
═══════════════════════════════════════════════════════════
  Model: gpt-4.1-mini
  Send a message to your bot in Slack to start chatting.
  Press Ctrl+C to stop.
```

Once connected, the console will also show:

```
Slack adapter connected via Socket Mode
```

Now send a DM to your bot in Slack, or invite it to a channel and send a message.

> **Tip:** To invite the bot to a channel, type `/invite @YourBotName` in the channel.

## How it works

```
Slack Socket Mode (WebSocket)
    │
    ▼
SlackAdapter (receives MessageEvent via SlackNet)
    │
    ▼
SlackMessageMapper (MessageEvent → PlatformMessage)
    │
    ▼
SlackAgentHandler (IPlatformMessageHandler)
    │
    ├─ StreamingMessageBridge ← posts "…" placeholder, then edits with accumulated text
    │
    ├─ StreamingChatWorkflow  ← runs the OpenAI agent loop with tool support
    │
    └─ SlackResponseSink      ← chat.postMessage / chat.update / reactions.add via Slack API
```

## Streaming behavior

Slack does not support true server-push streaming into a message. The adapter uses the **post-then-edit** pattern:

1. On the first text delta, a placeholder message (`…`) is posted
2. As deltas arrive, the message is edited with accumulated text (debounced at 300 ms to respect Slack's rate limits)
3. On completion, a final edit flushes the complete text

## Thread support

Slack threads are automatically handled. If a user starts a thread (or the bot's reply creates one), subsequent messages in that thread are routed to the same conversation context.

## Troubleshooting

| Issue | Fix |
|---|---|
| `Slack:BotToken not found` | Add the token to `secrets.json` or set the `Slack__BotToken` environment variable |
| `AppToken is required for Socket Mode` | Add the `xapp-…` token to `secrets.json` under `Slack:AppToken` |
| Bot doesn't respond in a channel | Invite the bot first: `/invite @YourBotName` |
| Bot responds to its own messages | This shouldn't happen — `SlackMessageEventHandler` filters out messages with empty `User` field (bot messages). If it does, check event subscriptions |
| `chat:write` error posting messages | Ensure the `chat:write` scope was added (step 3) and the app was reinstalled after adding scopes |
