# Ananke.Platforms.Discord — Architecture

> Discord adapter — planned for Phase 2.

## Role

Will implement `IMessagePlatformAdapter` and `IPlatformResponseSink` for Discord
using Discord.Net, supporting Gateway (WebSocket) connections.

## Status

**Skeleton project — no source files yet.** See ADR-U003 for the design plan.

## Planned Dependencies

- `Ananke.Platforms` (project)
- `Discord.Net` (NuGet)
- `Microsoft.Extensions.Hosting.Abstractions`

## Planned Types

| Type | Kind | Purpose |
|------|------|---------|
| `DiscordAdapter` | Class | `IMessagePlatformAdapter` via Discord Gateway WebSocket |
| `DiscordResponseSink` | Class | `IPlatformResponseSink` — maps to Discord message create/edit/react |
| `DiscordAdapterOptions` | Class | Configuration: bot token, gateway intents |
| `DiscordMessageMapper` | Class | Discord events → `PlatformMessage` |
| `ServiceCollectionExtensions` | Static class | `services.AddAnankeDiscord(options => ...)` |
