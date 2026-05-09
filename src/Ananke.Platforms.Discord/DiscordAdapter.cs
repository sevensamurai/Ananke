using Ananke.Orchestration.Tools;
using Ananke.Platforms;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Ananke.Platforms.Discord;

/// <summary>
/// <see cref="IMessagePlatformAdapter"/> implementation for Discord.
/// Connects via the Discord Gateway (WebSocket) and dispatches incoming messages
/// to the registered <see cref="IPlatformMessageHandler"/>.
/// </summary>
public sealed class DiscordAdapter : IMessagePlatformAdapter
{
    private readonly DiscordAdapterOptions _options;
    private readonly IPlatformMessageHandler _handler;
    private readonly DiscordSocketClient _client;
    private readonly ILogger _logger;
    private readonly BoundedDispatcher _dispatcher;
    private DiscordResponseSink? _responseSink;
    private bool _disposed;

    /// <summary>Creates a new Discord adapter.</summary>
    public DiscordAdapter(
        DiscordAdapterOptions options,
        IPlatformMessageHandler handler,
        DiscordSocketClient client,
        ILogger<DiscordAdapter>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(client);

        _options = options;
        _handler = handler;
        _client = client;
        _logger = logger ?? NullLogger<DiscordAdapter>.Instance;
        _dispatcher = new BoundedDispatcher(logger: _logger);
    }

    /// <inheritdoc />
    public bool IsConnected { get; private set; }

    /// <inheritdoc />
    public IPlatformResponseSink ResponseSink =>
        _responseSink ?? throw new InvalidOperationException("Adapter not started. Call StartAsync first.");

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _responseSink = new DiscordResponseSink(_client, _logger);
        _client.MessageReceived += OnMessageReceivedAsync;
        _client.SlashCommandExecuted += OnSlashCommandExecutedAsync;
        _client.Ready += OnReadyAsync;
        _client.Disconnected += OnDisconnectedAsync;

        await _client.LoginAsync(TokenType.Bot, _options.BotToken).ConfigureAwait(false);
        await _client.StartAsync().ConfigureAwait(false);
        await _dispatcher.StartAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Discord adapter started, connecting to Gateway...");
    }

    /// <summary>
    /// Dispatches a Discord <see cref="SocketMessage"/> to the registered handler.
    /// Called internally from the Gateway <c>MessageReceived</c> event.
    /// </summary>
    public async Task DispatchAsync(SocketMessage socketMessage, CancellationToken ct = default)
    {
        if (_responseSink is null)
            throw new InvalidOperationException("Adapter not started.");

        try
        {
            var message = DiscordMessageMapper.FromDiscordMessage(socketMessage);
            await _handler.HandleAsync(message, _responseSink, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Discord message from user {User} in channel {Channel}",
                socketMessage.Author.Id, socketMessage.Channel.Id);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken ct = default)
    {
        _client.MessageReceived -= OnMessageReceivedAsync;
        _client.SlashCommandExecuted -= OnSlashCommandExecutedAsync;
        _client.Ready -= OnReadyAsync;
        _client.Disconnected -= OnDisconnectedAsync;

        await _dispatcher.StopAsync(ct).ConfigureAwait(false);
        await _client.StopAsync().ConfigureAwait(false);
        await _client.LogoutAsync().ConfigureAwait(false);
        IsConnected = false;
        _logger.LogInformation("Discord adapter stopped");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _client.MessageReceived -= OnMessageReceivedAsync;
            _client.SlashCommandExecuted -= OnSlashCommandExecutedAsync;
            _client.Ready -= OnReadyAsync;
            _client.Disconnected -= OnDisconnectedAsync;
            _client.Dispose();
        }

        await _dispatcher.DisposeAsync().ConfigureAwait(false);
    }

    private Task OnMessageReceivedAsync(SocketMessage socketMessage)
    {
        // Ignore bot messages (including our own) and system messages
        if (socketMessage.Author.IsBot)
            return Task.CompletedTask;

        if (socketMessage is not SocketUserMessage)
            return Task.CompletedTask;

        // 5.4: Route through BoundedDispatcher instead of raw fire-and-forget so memory
        // growth is bounded when messages arrive faster than the handler processes them.
        _dispatcher.Enqueue(ct => DispatchAsync(socketMessage, ct));
        return Task.CompletedTask;
    }

    private Task OnReadyAsync()
    {
        IsConnected = true;
        _logger.LogInformation("Discord adapter connected — logged in as {BotUser}", _client.CurrentUser?.Username);

        if (_options.SlashCommandTools is not null)
            _ = RegisterSlashCommandsAsync();

        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(Exception ex)
    {
        IsConnected = false;
        _logger.LogWarning(ex, "Discord adapter disconnected");
        return Task.CompletedTask;
    }

    // ── Slash commands ──────────────────────────────────────────────

    private async Task RegisterSlashCommandsAsync()
    {
        try
        {
            var toolKit = _options.SlashCommandTools!;
            var commands = toolKit.Tools.Values
                .Select(t => DiscordSlashCommandMapper.ToSlashCommand(t).Build())
                .ToArray();

            if (_options.TestGuildId is { } guildId)
            {
                var guild = _client.GetGuild(guildId);
                if (guild is not null)
                {
                    await guild.BulkOverwriteApplicationCommandAsync(commands).ConfigureAwait(false);
                    _logger.LogInformation(
                        "Discord: registered {Count} slash command(s) in guild {GuildId} from ToolKit '{Name}'",
                        commands.Length, guildId, toolKit.Name);
                }
                else
                {
                    // 5.4: Treat guild-not-found as a registration fault so it surfaces
                    // at the same severity as any other registration failure.
                    _logger.LogError(
                        "Discord: slash command registration failed — guild {GuildId} not found. " +
                        "Ensure the bot has been added to the guild before starting.",
                        guildId);
                }
            }
            else
            {
                await _client.BulkOverwriteGlobalApplicationCommandsAsync(commands).ConfigureAwait(false);
                _logger.LogInformation(
                    "Discord: registered {Count} global slash command(s) from ToolKit '{Name}'",
                    commands.Length, toolKit.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Discord: failed to register slash commands");
        }
    }

    private Task OnSlashCommandExecutedAsync(SocketSlashCommand command)
    {
        // 5.4: Route through BoundedDispatcher for bounded memory and structured error handling.
        _dispatcher.Enqueue(ct => HandleSlashCommandAsync(command, ct));
        return Task.CompletedTask;
    }

    private async Task HandleSlashCommandAsync(SocketSlashCommand command, CancellationToken ct = default)
    {
        if (_options.SlashCommandTools is not { } toolKit
            || !toolKit.Tools.TryGetValue(command.Data.Name, out var tool))
        {
            try
            {
                await command.RespondAsync($"Unknown command: `/{command.Data.Name}`", ephemeral: true)
                    .ConfigureAwait(false);
            }
            catch { /* interaction may have expired */ }
            return;
        }

        try
        {
            // Defer immediately to avoid the 3-second interaction timeout.
            // Discord shows a "thinking…" indicator until we follow up.
            await command.DeferAsync().ConfigureAwait(false);

            var args = DiscordSlashCommandMapper.ExtractArgs(command.Data.Options);
            var result = await tool.ExecuteAsync(args).ConfigureAwait(false);

            var text = result.IsError ? $"❌ {result.Value}" : result.Value;

            // Discord messages are limited to 2000 characters
            if (text.Length > 2000)
                text = string.Concat(text.AsSpan(0, 1997), "…");

            await command.FollowupAsync(text).ConfigureAwait(false);
            _logger.LogDebug("Discord: /{Command} → {Status}", command.Data.Name, result.IsError ? "error" : "ok");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing slash command /{Command}", command.Data.Name);

            try
            {
                await command.FollowupAsync($"❌ {ex.Message}").ConfigureAwait(false);
            }
            catch { /* interaction may have expired */ }
        }
    }
}
