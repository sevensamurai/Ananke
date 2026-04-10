# ChannelsDiscordDemo

Minimal Discord bot powered by Ananke's `StreamingChatWorkflow`. Messages sent to the bot in any server channel (or DM) are routed to an OpenAI model, and the response streams back using Discord's post-then-edit pattern with automatic debouncing.

## Prerequisites

| Requirement | Notes |
|---|---|
| .NET 10 SDK | `dotnet --version` ≥ 10.0 |
| OpenAI API key | Any key with chat-completion access ([platform.openai.com/api-keys](https://platform.openai.com/api-keys)) |
| Discord Application + Bot | Created in the steps below |

## Discord setup (step-by-step)

### 1. Create a Discord Application

1. Go to [discord.com/developers/applications](https://discord.com/developers/applications)
2. Click **New Application**, give it a name, and click **Create**

### 2. Create the Bot and copy the token

1. In the left sidebar, click **Bot**
2. Click **Reset Token** → confirm → **copy the token** immediately (you won't see it again)
3. Keep this token for `secrets.json` below

### 3. Enable privileged intents

Still on the **Bot** page, scroll down to **Privileged Gateway Intents** and enable:

- [x] **Message Content Intent** — required so the bot can read message text

Click **Save Changes**.

### 4. Generate an invite URL and add the bot to your server

1. In the left sidebar, click **OAuth2 → URL Generator**
2. Under **Scopes**, check: `bot` and `applications.commands`
3. Under **Bot Permissions**, check:
   - `Send Messages`
   - `Read Message History`
   - `Add Reactions`
   - `Use Slash Commands`
4. Copy the generated URL at the bottom
5. Open the URL in a browser → select your server → **Authorize**

The bot should now appear in your server's member list (offline until you run the demo).

### 5. Create `secrets.json`

In the `demos/ChannelsDiscordDemo/` directory, create a file named `secrets.json`:

```json
{
  "OpenAI": {
    "ApiKey": "sk-...",
    "Model": "gpt-4.1-mini"
  },
  "Discord": {
    "BotToken": "your-bot-token-from-step-2"
  }
}
```

> **Tip:** `secrets.json` is listed in `.gitignore` — it will not be committed.

You can also use environment variables instead:

```bash
export OpenAI__ApiKey="sk-..."
export Discord__BotToken="your-bot-token"
```

## Run

```bash
cd src/demos/ChannelsDiscordDemo
dotnet run
```

You should see:

```
═══════════════════════════════════════════════════════════
  Ananke — Discord Bot Demo (Gateway)
═══════════════════════════════════════════════════════════
  Model: gpt-4.1-mini
  Send a message to your bot in Discord to start chatting.
  Press Ctrl+C to stop.
```

Once connected, the console will also show:

```
Discord adapter connected — logged in as YourBotName
```

Now send any message in a channel where the bot is present. The bot responds to **all** messages from non-bot users in channels it can see.

> **Tip:** To restrict the bot to only respond when mentioned, add a check in the message handler for `message.Message.Content.Contains(botMention)`.

## How it works

```
Discord Gateway (WebSocket)
    │
    ▼
DiscordAdapter (receives SocketMessage events)
    │
    ▼
DiscordMessageMapper (SocketMessage → PlatformMessage)
    │
    ▼
DiscordAgentHandler (IPlatformMessageHandler)
    │
    ├─ StreamingMessageBridge ← posts "…" placeholder, then edits with accumulated text
    │
    ├─ StreamingChatWorkflow  ← runs the OpenAI agent loop with tool support
    │
    └─ DiscordResponseSink    ← SendMessage / UpdateMessage / AddReaction via Discord API
```

## Streaming behavior

Discord does not support true server-push streaming into a message. The adapter uses the **post-then-edit** pattern:

1. On the first text delta, a placeholder message (`…`) is posted
2. As deltas arrive, the message is edited with accumulated text (debounced at 200 ms to stay within Discord's ~5 edits/second rate limit)
3. On completion, a final edit flushes the complete text

## Thread support

Messages sent inside a Discord thread are automatically detected. Responses are posted back into the same thread. In regular channels, responses go to the channel directly.

## Slash commands

This demo registers every tool in the `ToolKit` as a Discord slash command. After the bot connects, type `/` in any channel to see the commands with full autocomplete:

| Command | What it does |
|---|---|
| `/current_time` | Returns the current UTC date and time (no parameters) |
| `/echo text:hello` | Echoes the input text back |

Slash commands execute tools **directly** — the LLM is not involved. This is useful for utility commands, diagnostics, and admin actions that don't need AI reasoning.

> **Note:** Global slash commands can take up to an hour to propagate after first registration. For instant results during development, set `options.TestGuildId` to your server's ID (right-click server name → Copy Server ID with Developer Mode enabled).

## Troubleshooting

| Issue | Fix |
|---|---|
| `Discord:BotToken not found` | Add the token to `secrets.json` or set the `Discord__BotToken` environment variable |
| Bot appears online but doesn't respond | Ensure **Message Content Intent** is enabled (step 3) and the bot has `Read Message History` + `Send Messages` permissions in the channel |
| `Discord channel ... not found` | The bot can't see the channel — check that it has been invited to the server and has channel access |
| Rate-limited edits (messages update slowly) | Increase `StreamingBridgeOptions.DebounceInterval` in the adapter options |
| Slash commands don't appear after starting | Global commands take up to an hour to propagate. Set `options.TestGuildId` for instant guild-scoped registration |
