using Ananke.Platforms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlackNet;
using SlackNet.Events;
using SlackNet.SocketMode;
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
    private ISlackSocketModeClient? _socketClient;
    private SlackResponseSink? _responseSink;
    private bool _disposed;

    /// <summary>Creates a new Slack adapter.</summary>
    public SlackAdapter(
        SlackAdapterOptions options,
        IPlatformMessageHandler handler,
        ISlackServiceProvider slackServices,
        ILogger<SlackAdapter>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(slackServices);

        _options = options;
        _handler = handler;
        _slackServices = slackServices;
        _logger = logger ?? NullLogger<SlackAdapter>.Instance;
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

        _responseSink = new SlackResponseSink(_slackServices.GetApiClient(), _logger);

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
    /// Dispatches a Slack <see cref="MessageEvent"/> to the registered handler.
    /// Called internally by the <see cref="SlackMessageEventHandler"/> and can also
    /// be called from Events API HTTP endpoints.
    /// </summary>
    public async Task DispatchAsync(MessageEvent messageEvent, CancellationToken ct = default)
    {
        if (_responseSink is null)
            throw new InvalidOperationException("Adapter not started.");

        try
        {
            var message = SlackMessageMapper.FromSlackEvent(messageEvent);
            await _handler.HandleAsync(message, _responseSink, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Slack message from user {User} in {Channel}",
                messageEvent.User, messageEvent.Channel);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct = default)
    {
        IsConnected = false;
        _socketClient?.Disconnect();
        _socketClient = null;
        _logger.LogInformation("Slack adapter stopped");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _socketClient?.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
