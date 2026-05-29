using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlackNet;
using SlackNet.Events;
using SlackNet.Interaction;
using SlackNet.SocketMode;
using AssistantThreadContextChanged = SlackNet.Events.AssistantThreadContextChanged;
using AssistantThreadStarted = SlackNet.Events.AssistantThreadStarted;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Ananke.Platforms.Slack;

/// <summary>
/// <see cref="IMessagePlatformAdapter"/> implementation for Slack.
/// Connects via Socket Mode (WebSocket) or Events API (HTTP) and dispatches
/// incoming messages to the registered <see cref="IPlatformMessageHandler"/>.
/// </summary>
public sealed class SlackAdapter : IMessagePlatformAdapter
{
    private readonly SlackAdapterOptions _options;
    private readonly IPlatformMessageHandler _handler;
    private readonly ILogger _logger;
    private readonly ISlackServiceProvider _slackServices;
    private readonly HttpClient? _httpClient;
    private readonly BoundedDispatcher _dispatcher;
    private ISlackSocketModeClient? _socketClient;
    private SlackResponseSink? _responseSink;
    private bool _disposed;

    /// <summary>Creates a new Slack adapter.</summary>
    public SlackAdapter(
        SlackAdapterOptions options,
        IPlatformMessageHandler handler,
        ISlackServiceProvider slackServices,
        HttpClient? httpClient = null,
        ILogger<SlackAdapter>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(slackServices);

        _options = options;
        _handler = handler;
        _slackServices = slackServices;
        _httpClient = httpClient;
        _logger = logger ?? NullLogger<SlackAdapter>.Instance;
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

        _responseSink = new SlackResponseSink(_slackServices.GetApiClient(), _httpClient, _logger, _options);
        await _dispatcher.StartAsync(ct).ConfigureAwait(false);

        if (_options.UseSocketMode)
        {
            if (string.IsNullOrWhiteSpace(_options.AppToken))
                throw new InvalidOperationException(
                    "AppToken is required for Socket Mode. " +
                    "Set SlackAdapterOptions.AppToken to your xapp-… token.");

            _socketClient = _slackServices.GetSocketModeClient();
            await _socketClient.Connect(new SocketModeConnectionOptions(), ct).ConfigureAwait(false);
            IsConnected = true;
            _logger.LogInformation("Slack adapter connected via Socket Mode");
        }
        else
        {
            // Events API mode — the adapter itself doesn't open a connection.
            // An ASP.NET Core middleware or endpoint should receive HTTP events
            // and call DispatchAsync. Mark as connected since the sink is ready.
            IsConnected = true;
            _logger.LogInformation("Slack adapter started in Events API mode (HTTP)");
        }
    }

    /// <summary>
    /// Enqueues a Slack <see cref="MessageEvent"/> for dispatch through the
    /// <see cref="BoundedDispatcher"/>. Called from <see cref="SlackMessageEventHandler"/>.
    /// </summary>
    internal void EnqueueDispatch(MessageEvent messageEvent) =>
        _dispatcher.Enqueue(ct => DispatchAsync(messageEvent, ct));

    /// <summary>
    /// Enqueues a Slack <see cref="AppMention"/> for dispatch through the
    /// <see cref="BoundedDispatcher"/>.
    /// </summary>
    internal void EnqueueDispatch(AppMention appMention) =>
        _dispatcher.Enqueue(ct => DispatchAsync(appMention, ct));

    /// <summary>
    /// Enqueues a Slack <see cref="ReactionAdded"/> for dispatch through the
    /// <see cref="BoundedDispatcher"/>.
    /// </summary>
    internal void EnqueueReaction(ReactionAdded reactionEvent) =>
        _dispatcher.Enqueue(ct => DispatchReactionAsync(reactionEvent, ct));

    /// <summary>
    /// Enqueues a Slack <see cref="SlashCommand"/> for dispatch through the
    /// <see cref="BoundedDispatcher"/>.
    /// </summary>
    internal void EnqueueSlashCommand(SlashCommand command) =>
        _dispatcher.Enqueue(ct => DispatchSlashCommandAsync(command, ct));

    /// <summary>
    /// Enqueues a Slack <see cref="BlockActionRequest"/> for dispatch through the
    /// <see cref="BoundedDispatcher"/>.
    /// </summary>
    internal void EnqueueInteraction(BlockActionRequest request) =>
        _dispatcher.Enqueue(ct => DispatchInteractionAsync(
            SlackMessageMapper.FromBlockActionRequest(request), ct));

    /// <summary>
    /// Enqueues a Slack <see cref="ViewSubmission"/> for dispatch through the
    /// <see cref="BoundedDispatcher"/>.
    /// </summary>
    internal void EnqueueInteraction(ViewSubmission viewSubmission) =>
        _dispatcher.Enqueue(ct => DispatchInteractionAsync(
            SlackMessageMapper.FromViewSubmission(viewSubmission, viewSubmission), ct));

    /// <summary>
    /// Enqueues a Slack <see cref="ViewClosed"/> for dispatch through the
    /// <see cref="BoundedDispatcher"/>.
    /// </summary>
    internal void EnqueueInteraction(ViewClosed viewClosed) =>
        _dispatcher.Enqueue(ct => DispatchInteractionAsync(
            SlackMessageMapper.FromViewClosed(viewClosed, viewClosed), ct));

    /// <summary>
    /// Enqueues a Slack <see cref="AssistantThreadStarted"/> event for dispatch.
    /// </summary>
    internal void EnqueueAssistantThread(AssistantThreadStarted slackEvent) =>
        _dispatcher.Enqueue(ct => DispatchAssistantThreadAsync(
            SlackMessageMapper.FromAssistantThreadStarted(slackEvent), ct));

    /// <summary>
    /// Enqueues a Slack <see cref="AssistantThreadContextChanged"/> event for dispatch.
    /// </summary>
    internal void EnqueueAssistantThread(AssistantThreadContextChanged slackEvent) =>
        _dispatcher.Enqueue(ct => DispatchAssistantThreadAsync(
            SlackMessageMapper.FromAssistantThreadContextChanged(slackEvent), ct));

    /// <summary>
    /// Dispatches a Slack <see cref="MessageEvent"/> to the registered handler.
    /// Called internally by the <see cref="SlackMessageEventHandler"/> and can also
    /// be called from Events API HTTP endpoints.
    /// </summary>
    public async Task DispatchAsync(MessageEvent messageEvent, CancellationToken ct = default)
    {
        var message = SlackMessageMapper.FromSlackEvent(messageEvent);
        await DispatchMessageAsync(message, messageEvent.User, messageEvent.Channel, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Dispatches a normalized <see cref="PlatformMessage"/> to the registered handler.
    /// </summary>
    public async Task DispatchAsync(PlatformMessage message, CancellationToken ct = default)
    {
        if (_responseSink is null)
            throw new InvalidOperationException("Adapter not started.");

        await _handler.HandleAsync(message, _responseSink, ct).ConfigureAwait(false);
    }

    private async Task DispatchAsync(AppMention appMention, CancellationToken ct)
    {
        var message = SlackMessageMapper.FromAppMention(appMention);
        await DispatchMessageAsync(message, appMention.User, appMention.Channel, ct).ConfigureAwait(false);
    }

    private async Task DispatchMessageAsync(
        PlatformMessage message,
        string? userId,
        string? channelId,
        CancellationToken ct)
    {
        try
        {
            await DispatchAsync(message, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Slack message from user {User} in {Channel}",
                userId, channelId);
        }
    }

    private async Task DispatchReactionAsync(ReactionAdded reactionEvent, CancellationToken ct)
    {
        if (_responseSink is null)
            throw new InvalidOperationException("Adapter not started.");

        try
        {
            var reaction = SlackMessageMapper.FromReactionAdded(reactionEvent);
            await _handler.OnReactionAsync(reaction, _responseSink, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Slack reaction {Reaction} from user {User}",
                reactionEvent.Reaction, reactionEvent.User);
        }
    }

    private async Task DispatchSlashCommandAsync(SlashCommand command, CancellationToken ct)
    {
        if (_responseSink is null)
            throw new InvalidOperationException("Adapter not started.");

        try
        {
            var normalized = SlackMessageMapper.FromSlashCommand(command);
            await _handler.OnSlashCommandAsync(normalized, _responseSink, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Slack slash command {Command} from user {User}",
                command.Command, command.UserId);
        }
    }

    private async Task DispatchInteractionAsync(PlatformInteractionEvent interaction, CancellationToken ct)
    {
        if (_responseSink is null)
            throw new InvalidOperationException("Adapter not started.");

        try
        {
            await _handler.OnInteractionAsync(interaction, _responseSink, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Slack interaction {Kind} action {ActionId} from user {User}",
                interaction.Kind, interaction.ActionId, interaction.UserId);
        }
    }

    private async Task DispatchAssistantThreadAsync(PlatformAssistantThreadEvent threadEvent, CancellationToken ct)
    {
        if (_responseSink is null)
            throw new InvalidOperationException("Adapter not started.");

        try
        {
            await _handler.OnAssistantThreadAsync(threadEvent, _responseSink, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Slack assistant thread event {Kind} for user {User} in {Channel}",
                threadEvent.Kind, threadEvent.UserId, threadEvent.ChannelId);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken ct = default)
    {
        IsConnected = false;
        _socketClient?.Disconnect();
        _socketClient = null;
        await _dispatcher.StopAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Slack adapter stopped");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _socketClient?.Dispose();
        }

        await _dispatcher.DisposeAsync().ConfigureAwait(false);
    }
}
