using Discord;
using Ananke.Orchestration.Tools;

namespace Ananke.Platforms.Discord;

/// <summary>
/// Configuration options for the Discord adapter.
/// </summary>
public sealed class DiscordAdapterOptions
{
    /// <summary>
    /// Bot token from the Discord Developer Portal → Bot → Token.
    /// </summary>
    public required string BotToken { get; set; }

    /// <summary>
    /// Gateway intents controlling which events the bot receives.
    /// Default includes <see cref="GatewayIntents.Guilds"/>, <see cref="GatewayIntents.GuildMessages"/>,
    /// <see cref="GatewayIntents.DirectMessages"/>, and <see cref="GatewayIntents.MessageContent"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="GatewayIntents.MessageContent"/> is a privileged intent — enable it in
    /// the Discord Developer Portal under Bot → Privileged Gateway Intents.
    /// </remarks>
    public GatewayIntents GatewayIntents { get; set; } =
        GatewayIntents.Guilds |
        GatewayIntents.GuildMessages |
        GatewayIntents.DirectMessages |
        GatewayIntents.MessageContent;

    /// <summary>
    /// Streaming bridge options controlling debounce interval and thinking placeholder.
    /// Default debounce is 200 ms (Discord allows ~5 edits/second).
    /// </summary>
    public StreamingBridgeOptions StreamingOptions { get; set; } = new()
    {
        DebounceInterval = TimeSpan.FromMilliseconds(200)
    };

    /// <summary>
    /// When set, each <see cref="ToolDefinition"/> in the kit is registered as a
    /// Discord slash command on <c>Ready</c>. Users invoke tools directly via
    /// <c>/tool_name param:value</c> — the LLM is not involved.
    /// </summary>
    /// <remarks>
    /// Commands are registered using <c>BulkOverwriteGlobalApplicationCommandsAsync</c>,
    /// which atomically replaces all commands. Stale commands from previous runs are
    /// removed automatically. Set <see cref="TestGuildId"/> for instant propagation
    /// during development (global commands can take up to an hour to appear).
    /// </remarks>
    public ToolKit? SlashCommandTools { get; set; }

    /// <summary>
    /// When set, slash commands are registered to this guild instead of globally.
    /// Guild commands propagate instantly — use this during development.
    /// Set to <see langword="null"/> (default) for global commands in production.
    /// </summary>
    /// <remarks>
    /// To find your guild (server) ID: enable Developer Mode in Discord settings,
    /// then right-click your server name → Copy Server ID.
    /// </remarks>
    public ulong? TestGuildId { get; set; }
}
