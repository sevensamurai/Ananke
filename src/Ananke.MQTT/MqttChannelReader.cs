using Ananke.Abstractions.Channels;
using Ananke.Abstractions.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using MQTTnet.Protocol;
using System.Buffers;
using System.Threading.Channels;

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
/// From <c>ConfigureAsync</c> until <see cref="ClearAsync"/> or dispose, a lost connection is retried with
/// a capped delay between attempts, and the topic is subscribed again, at the configured
/// <see cref="ChannelConfig.QualityOfService"/>, on every connect. A message whose payload can't be
/// deserialized is logged and dropped; the next one is delivered.
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

    private MqttConnection? _connection;
    private string? _topic;
    private MqttQualityOfServiceLevel _qualityOfService;
    private bool _keepSession;
    private bool _disposed;
    private IAsyncDisposable? _processor;

    /// <summary>The wait between reconnect attempts.</summary>
    internal MqttReconnectDelay ReconnectDelay { get; init; } = MqttReconnectDelay.Default;

    /// <summary>Replaces the validation of the broker's TLS certificate against the trusted roots.</summary>
    internal Func<MqttClientCertificateValidationEventArgs, bool>? CertificateValidation { get; init; }

    /// <summary>
    /// Whether the client is connected to the broker now and subscribed to its topic: false after a reconnect
    /// until the subscription is set up again.
    /// </summary>
    public bool IsConnected => _connection?.IsReady == true;

    /// <summary>How many connects have completed, each with its subscription.</summary>
    internal int ConnectCount => _connection?.ConnectCount ?? 0;

    private static byte[] GetPayloadBytes(ReadOnlySequence<byte> payload)
    {
        return payload.IsSingleSegment ? payload.FirstSpan.ToArray() : payload.ToArray();
    }

    /// <inheritdoc />
    public async Task<bool> ConfigureAsync(ChannelConfig config, IBackgroundWorker<M> consumer, CancellationToken token = default)
    {
        MqttConnection.Validate(config);
        ArgumentNullException.ThrowIfNull(consumer);

        var topic = NamespaceMapper.GetTopicWildcard<A>(config.Namespace);
        if (!string.IsNullOrWhiteSpace(config.GroupName))
            topic = $"$share/{config.GroupName}/{topic}";

        var processor = new BackgroundProcessor<M>(
            consumer, queueCapacity,
            onError: (ex, _) => _logger.LogError(ex, "RX Channel worker failed to handle message of type {MessageType}", typeof(M).Name),
            onInfo: msg => _logger.LogDebug("{Message}", msg));

        return await SetupClient(config, topic, processor,
            onMessage: (message, _, ct) => processor.EnqueueAsync(message, ct),
            setCommandOnMessage: true, token: token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ConfigureAsync(ChannelConfig config, IBackgroundWorker<M> consumer, A action, CancellationToken token = default)
    {
        MqttConnection.Validate(config);
        ArgumentNullException.ThrowIfNull(consumer);

        var topic = NamespaceMapper.GetTopic(config.Namespace, action);
        if (!string.IsNullOrWhiteSpace(config.GroupName))
            topic = $"$share/{config.GroupName}/{topic}";

        var processor = new BackgroundProcessor<M>(
            consumer, queueCapacity,
            onError: (ex, _) => _logger.LogError(ex, "RX Channel worker failed to handle message of type {MessageType}", typeof(M).Name),
            onInfo: msg => _logger.LogDebug("{Message}", msg));

        return await SetupClient(config, topic, processor,
            onMessage: (message, _, ct) => processor.EnqueueAsync(message, ct),
            setCommandOnMessage: false, token: token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ConfigureAsync(ChannelConfig config, IBackgroundWorker<M, A> consumer, CancellationToken token = default)
    {
        MqttConnection.Validate(config);
        ArgumentNullException.ThrowIfNull(consumer);

        var topic = NamespaceMapper.GetTopicWildcard<A>(config.Namespace);
        if (!string.IsNullOrWhiteSpace(config.GroupName))
            topic = $"$share/{config.GroupName}/{topic}";

        var processor = new BackgroundProcessor<M, A>(
            consumer, queueCapacity,
            onError: (ex, _) => _logger.LogError(ex, "RX Channel worker failed to handle message of type {MessageType}", typeof(M).Name),
            onInfo: msg => _logger.LogDebug("{Message}", msg));

        return await SetupClient(config, topic, processor,
            onMessage: (message, action, ct) => processor.EnqueueAsync(message, action, ct),
            setCommandOnMessage: false, token: token).ConfigureAwait(false);
    }

    private async Task<bool> SetupClient(
        ChannelConfig config,
        string topic,
        IAsyncDisposable processor,
        Func<M, A, CancellationToken, ValueTask> onMessage,
        bool setCommandOnMessage,
        CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // A second ConfigureAsync replaces the client, and its reconnect loop, with a new one.
        await StopConnectionAsync(CancellationToken.None).ConfigureAwait(false);

        // Throws when the client id is in use in the process, before anything is started.
        // A type's ToString() is its full name, with no assembly version in a generic type's arguments.
        var connection = new MqttConnection(
            MqttConnection.OptionsFor(config, ["reader", typeof(A).ToString(), typeof(M).ToString()], CertificateValidation),
            SubscribeAsync, ReconnectDelay, "RX Channel", _logger);
        // Held from here, so a clear or dispose frees its client id whatever throws below.
        _connection = connection;

        _topic = topic;
        _qualityOfService = MqttConnection.QualityOfServiceFor(config);
        _keepSession = !config.CleanSession;

        // Dispose previous processor if ConfigureAsync is called again
        if (_processor is not null)
            await _processor.DisposeAsync().ConfigureAwait(false);

        _processor = processor;

        if (processor is BackgroundProcessor<M> p)
            p.Start(token);
        else if (processor is BackgroundProcessor<M, A> pa)
            pa.Start(token);

        _logger.LogInformation("RX Channel subscribing to {Topic}", _topic);

        var lifetime = connection.Lifetime;
        connection.Client.ApplicationMessageReceivedAsync +=
            e => OnMessageReceivedAsync(e, onMessage, setCommandOnMessage, lifetime);

        return await connection.StartAsync(token).ConfigureAwait(false);
    }

    // Never throws: an exception here would reach the client's receive loop.
    private async Task OnMessageReceivedAsync(
        MqttApplicationMessageReceivedEventArgs e,
        Func<M, A, CancellationToken, ValueTask> onMessage,
        bool setCommandOnMessage,
        CancellationToken lifetime)
    {
        var topic = e.ApplicationMessage.Topic;
        var data = GetPayloadBytes(e.ApplicationMessage.Payload);

        M? message = null;
        Exception? error = null;
        try
        {
            message = DataSerializer.Deserialize<M>(data);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        if (message is null)
        {
            _logger.LogWarning(error, "RX Channel message from {Topic} ({Size}B) could not be deserialized, message dropped",
                topic, data.Length);
            return;
        }

        var actionStr = NamespaceMapper.GetActionFromTopic(topic);

        // Legacy: set Command on IMqttContext if the message type supports it
        if (setCommandOnMessage && message is IMqttContext mqttCtx)
            mqttCtx.Command = actionStr;

        if (!Enum.TryParse(typeof(A), actionStr, ignoreCase: true, out var parsed) || parsed is not A action)
        {
            _logger.LogWarning("RX Channel could not parse action '{Action}' as {EnumType} from {Topic}",
                actionStr, typeof(A).Name, topic);
            return;
        }

        _logger.LogDebug("Received message from {Topic}", topic);

        try
        {
            // Waits while the queue is full; the client's dispose cancels the wait.
            await onMessage(message, action, lifetime).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ChannelClosedException)
        {
            // Left unacknowledged: a kept session has the broker send it again on the next connect.
            e.AutoAcknowledge = false;
            _logger.LogDebug("RX Channel stopped before the message from {Topic} was queued; message not acknowledged", topic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RX Channel failed to queue the message from {Topic}; message dropped", topic);
        }
    }

    // Runs on every connect: a clean session starts with no subscriptions; a kept one is subscribed again unchanged.
    private async Task SubscribeAsync(IMqttClient client, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_topic)) return;

        var result = await client.SubscribeAsync(new MqttTopicFilterBuilder()
            .WithTopic(_topic)
            .WithQualityOfServiceLevel(_qualityOfService)
            .Build(), ct).ConfigureAwait(false);
        MqttConnection.EnsureGranted(result);
        _logger.LogDebug("RX Channel subscribed to {Topic}", _topic);
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken ct = default) => StopConnectionAsync(ct);

    // Unsubscribes (unless asked not to), then stops the reconnect loop and disconnects.
    private async Task StopConnectionAsync(CancellationToken ct, bool unsubscribe = true)
    {
        var connection = _connection;
        if (connection is null) return;
        _connection = null;

        try
        {
            if (unsubscribe && connection.IsConnected && !string.IsNullOrEmpty(_topic))
                await connection.Client.UnsubscribeAsync(_topic, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "RX Channel unsubscribe failed");
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_processor is not null)
            await _processor.DisposeAsync().ConfigureAwait(false);

        // A kept session stays subscribed, so the broker holds messages for it until the next connect.
        await StopConnectionAsync(CancellationToken.None, unsubscribe: !_keepSession).ConfigureAwait(false);

        GC.SuppressFinalize(this);
    }
}
