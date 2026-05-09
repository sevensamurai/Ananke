# ChannelsDemo — Ananke Agent as a Discord or Slack Bot

An Ananke tool-calling agent deployed as a long-running bot on **Discord** or **Slack** using the `Ananke.Platforms` adapters.

---

## Quick Start

```bash
cd demos/06-interop-and-channels/ChannelsDemo
# Populate secrets.json (see below)
dotnet run -- --platform discord
# or
dotnet run -- --platform slack
```

---

## Secrets

Create `secrets.json` in the demo folder.

**Discord:**
```json
{
  "OpenAI": { "ApiKey": "sk-...", "Model": "gpt-4.1-mini" },
  "Discord": { "BotToken": "your-bot-token" }
}
```

**Slack:**
```json
{
  "OpenAI": { "ApiKey": "sk-...", "Model": "gpt-4.1-mini" },
  "Slack": { "BotToken": "xoxb-...", "AppToken": "xapp-..." }
}
```

---

## Platform Setup

### Discord

1. Create an application at <https://discord.com/developers/applications>
2. **Bot → Reset Token** — copy the bot token
3. **Bot → Privileged Gateway Intents** — enable **Message Content Intent**
4. **OAuth2 → URL Generator** — scopes: `bot`; bot permissions: Send Messages, Read Message History, Add Reactions
5. Use the generated URL to invite the bot to your server

### Slack

1. Create a Slack app at <https://api.slack.com/apps>
2. **Socket Mode** — enable and generate an App-Level Token (`xapp-…`)
3. **OAuth & Permissions → Bot Token Scopes** — add: `chat:write`, `reactions:write`, `app_mentions:read`, `channels:history`, `groups:history`, `im:history`, `mpim:history`
4. **Install App to Workspace** — copy the Bot User OAuth Token (`xoxb-…`)

---

## What the Demo Shows

| Concept | How |
|---|---|
| **Platform adapters** | `AddAnankeDiscord` / `AddAnankeSlack` register the channel adapter in the .NET generic host DI container |
| **Message handling** | `IPlatformMessageHandler` implementations (`DiscordAgentHandler`, `SlackAgentHandler`) receive incoming messages and stream responses back |
| **Conversation memory** | `InMemoryConversationMemory` (1-hour TTL) preserves context across turns per user/channel |
| **Tool calling** | A `ToolKit` with `current_time` and `echo` tools is wired to the agent |
| **Generic host** | `Host.CreateApplicationBuilder` + `host.RunAsync()` — the bot runs until Ctrl+C |

---

## Project Structure

| File | Purpose |
|---|---|
| `Program.cs` | Entry point; parses `--platform`, configures DI, starts the host |

---

## Key Concepts

- **`Ananke.Platforms`** — platform-agnostic `IPlatformMessageHandler` contract and `SessionContext` management
- **`Ananke.Platforms.Discord`** — Discord.Net-backed adapter; supports slash-command tool exposure via `SlashCommandTools`
- **`Ananke.Platforms.Slack`** — Slack Socket Mode adapter; no public HTTP endpoint required
- **`IConversationMemory`** — shared across turns so the bot remembers prior messages in the same channel/thread
