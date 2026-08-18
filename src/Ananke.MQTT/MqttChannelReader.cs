using Ananke.Abstractions.Channels;
using Ananke.Abstractions.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using System.Buffers;

namespace Ananke.MQTT;

/// <summary>
/// MQTT-backed implementation of <see cref="IChannelReader{M, A}"/>.
/// Subscribes to MQTT topics and dispatches messages to the provided background worker
/// via a <see cref="BackgroundProcessor{T}"/> that provides backpressure and error isolation.
/// </summary>
/// <remarks>
/// Three configuration modes:
/// <list type="bullet">
///   <item><see cref="ConfigureAsync(ChannelConfig, IBackgroundWorker{M, A}, CancellationToken)"/>
///   — typed-action delivery (preferred). The action enum is parsed from the MQTT topic and
///   delivered alongside the message. No <see cref="IMqttContext"/> needed.</item>
///   <item><see cref="ConfigureAsync(ChannelConfig, IBackgroundWorker{M}, CancellationToken)"/>
///   — wildcard subscription with untyped worker. If <typeparamref name="M"/> implements
///   <see cref="IMqttContext"/>, the action string is set on <see cref="IMqttContext.Command"/>
///   (legacy compatibility).</item>
///   <item><see cref="ConfigureAsync(ChannelConfig, IBackgroundWorker{M}, A, CancellationToken)"/>
///   — single-action subscription with untyped worker.</item>
/// </list>
/// </remarks>
/// <typeparam name="M">Message type (any class; no longer requires <see cref="IMqttContext"/>).</typeparam>
/// <typeparam name="A">Action/transition enum type used for topic routing.</typeparam>
public sealed class MqttChannelReader<M, A>(
    int queueCapacity = 1024,
    ILogger<MqttChannelReader<M, A>>? logger = null) : IChannelReader<M, A>
    where M : class
    where A : Enum
{
    private readonly ILogger<MqttChannelReader<M, A>> _logger = logger ?? NullLogger<MqttChannelReader<M, A>>.Instance;

    private IMqttClient? _client;
    private MqttClientOptions? _options;
    private string? _topic;
    private bool _linked;
    private bool _disposed;
    private IAsyncDisposable? _processor;

    private static byte[] GetPayloadBytes(ReadOnlySequence<byte> payload)
    {
        return payload.IsSingleSegment ? payload.FirstSpan.ToArray() : payload.ToArray();
    }

    /// <inheritdoc />
    public async Task<bool> ConfigureAsync(ChannelConfig config, IBackgroundWorker<M> consumer, CancellationToken token = default)
    {
        ValidateConfig(config);
        ArgumentNullException.ThrowIfNull(consumer);

        var topic = NamespaceMapper.GetTopicWildcard<A>(config.Namespace);
        if (!string.IsNullOrWhiteSpace(config.GroupName))
            topic = $"$share/{config.GroupName}/{topic}";

        var processor = new BackgroundProcessor<M>(
            consumer, queueCapacity,
            onError: (ex, _) => _logger.LogError(ex, "RX Channel worker failed to handle message of type {MessageType}", typeof(M).Name),
            onInfo: msg => _logger.LogDebug("{Message}", msg));

        return await SetupClient(config, topic, processor,
            onMessage: (message, _) => processor.EnqueueAsync(message),
            setCommandOnMessage: true, token: token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ConfigureAsync(ChannelConfig config, IBackgroundWorker<M> consumer, A action, CancellationToken token = default)
    {
        ValidateConfig(config);
        ArgumentNullException.ThrowIfNull(consumer);

        var topic = NamespaceMapper.GetTopic(config.Namespace, action);
        if (!string.IsNullOrWhiteSpace(config.GroupName))
            topic = $"$share/{config.GroupName}/{topic}";

        var processor = new BackgroundProcessor<M>(
            consumer, queueCapacity,
            onError: (ex, _) => _logger.LogError(ex, "RX Channel worker failed to handle message of type {MessageType}", typeof(M).Name),
            onInfo: msg => _logger.LogDebug("{Message}", msg));

        return await SetupClient(config, topic, processor,
            onMessage: (message, _) => processor.EnqueueAsync(message),
            setCommandOnMessage: false, token: token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ConfigureAsync(ChannelConfig config, IBackgroundWorker<M, A> consumer, CancellationToken token = default)
    {
        ValidateConfig(config);
        ArgumentNullException.ThrowIfNull(consumer);

        var topic = NamespaceMapper.GetTopicWildcard<A>(config.Namespace);
        if (!string.IsNullOrWhiteSpace(config.GroupName))
            topic = $"$share/{config.GroupName}/{topic}";

        var processor = new BackgroundProcessor<M, A>(
            consumer, queueCapacity,
            onError: (ex, _) => _logger.LogError(ex, "RX Channel worker failed to handle message of type {MessageType}", typeof(M).Name),
            onInfo: msg => _logger.LogDebug("{Message}", msg));

        return await SetupClient(config, topic, processor,
            onMessage: (message, action) => processor.EnqueueAsync(message, action),
            setCommandOnMessage: false, token: token).ConfigureAwait(false);
    }

    private async Task<bool> SetupClient(
        ChannelConfig config,
        string topic,
        IAsyncDisposable processor,
        Func<M, A, ValueTask> onMessage,
        bool setCommandOnMessage,
        CancellationToken token)
    {
        _topic = topic;

        // Dispose previous processor if ConfigureAsync is called again
        if (_processor is not null)
            await _processor.DisposeAsync().ConfigureAwait(false);

        _processor = processor;

        if (processor is BackgroundProcessor<M> p)
            p.Start(token);
        else if (processor is BackgroundProcessor<M, A> pa)
            pa.Start(token);

        _logger.LogInformation("RX Channel subscribing to {Topic}", _topic);
        _client = new MqttClientFactory().CreateMqttClient();

        _options = new MqttClientOptionsBuilder()
            .WithTcpServer(config.Host, config.Port)
            .WithCredentials(config.Username, config.Password)
            .WithWillQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithCleanSession()
            .Build();

        _client.ApplicationMessageReceivedAsync += async e =>
        {
            var data = GetPayloadBytes(e.ApplicationMessage.Payload);
            var message = DataSerializer.Deserialize<M>(data);

            if (message is null)
            {
                _logger.LogWarning("RX Channel failed to deserialize message from {Topic} ({Size}B), message dropped",
                    e.ApplicationMessage.Topic, data.Length);
                return;
            }

            var actionStr = NamespaceMapper.GetActionFromTopic(e.ApplicationMessage.Topic);

            // Legacy: set Command on IMqttContext if the message type supports it
            if (setCommandOnMessage && message is IMqttContext mqttCtx)
                mqttCtx.Command = actionStr;

            if (!Enum.TryParse(typeof(A), actionStr, ignoreCase: true, out var parsed) || parsed is not A action)
            {
                _logger.LogWarning("RX Channel could not parse action '{Action}' as {EnumType} from {Topic}",
                    actionStr, typeof(A).Name, e.ApplicationMessage.Topic);
                return;
            }

            _logger.LogDebug("Received message from {Topic}", e.ApplicationMessage.Topic);

            try
            {
                await onMessage(message, action).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("RX Channel enqueue cancelled for {Topic}", e.ApplicationMessage.Topic);
            }
        };

        _client.ConnectedAsync += async _ =>
        {
            _logger.LogDebug("RX Channel connected");
            if (_client is not null && !string.IsNullOrEmpty(_topic))
                await _client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(_topic).Build()).ConfigureAwait(false);
        };

        _client.DisconnectedAsync += async _ =>
        {
            _logger.LogWarning("RX Channel disconnected");
            if (!_disposed)
                await ReconnectAsync().ConfigureAwait(false);
        };

        return await ConnectAsync(token).ConfigureAwait(false);
    }

    private async Task<bool> ConnectAsync(CancellationToken token)
    {
        if (_client is null || _options is null)
        {
            _linked = false;
            return false;
        }

        try
        {
            var resp = await _client.ConnectAsync(_options, token).ConfigureAwait(false);
            _linked = resp.ResultCode == MqttClientConnectResultCode.Success;
            return _linked;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RX Channel unable to connect");
            _linked = false;
            return false;
        }
    }

    private async Task ReconnectAsync()
    {
        const int maxAttempts = 5;
        var delay = TimeSpan.FromSeconds(1);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (_disposed) return;

            try
            {
                await Task.Delay(delay).ConfigureAwait(false);
                if (await ConnectAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    _logger.LogInformation("RX Channel reconnected after {Attempt} attempt(s)", attempt);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RX Channel reconnection attempt {Attempt}/{MaxAttempts} failed",
                    attempt, maxAttempts);
            }

            delay *= 2;
        }

        _logger.LogError("RX Channel failed to reconnect after {MaxAttempts} attempts", maxAttempts);
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken ct = default)
    {
        if (_client is not null && _client.IsConnected)
        {
            if (!string.IsNullOrEmpty(_topic))
                await _client.UnsubscribeAsync(_topic, cancellationToken: ct).ConfigureAwait(false);
            await _client.DisconnectAsync(cancellationToken: ct).ConfigureAwait(false);
        }
        _linked = false;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_processor is not null)
            await _processor.DisposeAsync().ConfigureAwait(false);

        await ClearAsync().ConfigureAwait(false);

        if (_client is not null)
        {
            _client.Dispose();
            _client = null;
        }

        GC.SuppressFinalize(this);
    }

    private static void ValidateConfig(ChannelConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Host);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Namespace);
    }
}
