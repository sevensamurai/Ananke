using System.Buffers;
using System.Collections.Concurrent;
using System.Text.Json;
using Ananke.Abstractions.Channels;
using Ananke.Abstractions.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Protocol;

namespace Ananke.MQTT;

/// <summary>
/// MQTT-backed <see cref="IHandoffChannel"/> for production agent-to-agent handoff.
/// Uses JSON serialization over MQTT topics with the pattern:
/// <c>{namespace}/{topic}/request/{correlationId}</c> (outgoing) and
/// <c>{namespace}/{topic}/reply/{correlationId}</c> (incoming response).
/// </summary>
/// <remarks>
/// Call <see cref="ConfigureAsync"/> before use. The responder can be:
/// <list type="bullet">
///   <item>Another <see cref="MqttHandoffChannel"/> instance calling
///   <see cref="CompleteAsync{TResponse}"/>.</item>
///   <item>Any MQTT client that subscribes to <c>{namespace}/{topic}/request/#</c>,
///   processes the message, and publishes a reply to
///   <c>{namespace}/{topic}/reply/{correlationId}</c>.</item>
/// </list>
/// </remarks>
public sealed class MqttHandoffChannel(ILogger<MqttHandoffChannel>? logger = null) : IHandoffChannel, IAsyncDisposable
{
    private readonly ILogger<MqttHandoffChannel> _logger = logger ?? NullLogger<MqttHandoffChannel>.Instance;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _pending = new();
    private readonly ConcurrentDictionary<string, Func<string, byte[], CancellationToken, Task>> _subscriptions = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Subscription handlers run off the receive loop. They are cancelled and waited for on dispose,
    // so none outlives the channel it was delivered by.
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<Task, byte> _handlers = new();
    private static readonly TimeSpan HandlerDrain = TimeSpan.FromSeconds(5);

    private MqttConnection? _connection;
    private string _namespace = string.Empty;
    private MqttQualityOfServiceLevel _qualityOfService;
    private bool _disposed;

    /// <summary>
    /// Tells this channel's client id from that of another handoff channel in the process with the same
    /// <see cref="ChannelConfig.ClientId"/> and <see cref="ChannelConfig.Namespace"/>. Default none.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>The wait between reconnect attempts.</summary>
    internal MqttReconnectDelay ReconnectDelay { get; init; } = MqttReconnectDelay.Default;

    /// <summary>Replaces the validation of the broker's TLS certificate against the trusted roots.</summary>
    internal Func<MqttClientCertificateValidationEventArgs, bool>? CertificateValidation { get; init; }

    /// <summary>How many connects have completed, each with its subscriptions restored.</summary>
    internal int ConnectCount => _connection?.ConnectCount ?? 0;

    /// <summary>Whether the channel is connected to the MQTT broker.</summary>
    public bool IsConnected => _connection?.IsConnected == true;

    /// <summary>
    /// Connects to the MQTT broker. Must be called before <see cref="SendAsync{TMessage, TResponse}"/>
    /// or <see cref="CompleteAsync{TResponse}"/>.
    /// </summary>
    /// <remarks>
    /// From here until the channel is disposed, a lost connection is retried, with a capped delay
    /// between attempts, and every subscription and pending reply topic is subscribed again on connect.
    /// Requests and replies are published, and topics subscribed, at the configured
    /// <see cref="ChannelConfig.QualityOfService"/>.
    /// The retries start even when this first connect fails.
    /// </remarks>
    /// <returns>Whether the first connect succeeded.</returns>
    public async Task<bool> ConfigureAsync(ChannelConfig config, CancellationToken ct = default)
    {
        MqttConnection.Validate(config);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // A second ConfigureAsync replaces the client, and its reconnect loop, with a new one.
        if (_connection is not null)
            await _connection.DisposeAsync().ConfigureAwait(false);
        _connection = null;

        var connection = new MqttConnection(
            MqttConnection.OptionsFor(config, Name is null ? ["handoff"] : ["handoff", Name], CertificateValidation),
            ResubscribeAsync, ReconnectDelay, "Handoff channel", _logger);
        _namespace = config.Namespace;
        _qualityOfService = MqttConnection.QualityOfServiceFor(config);
        connection.Client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _connection = connection;

        var connected = await connection.StartAsync(ct).ConfigureAwait(false);
        if (connected)
            _logger.LogInformation("Handoff channel connected to {Host}:{Port}", config.Host, config.Port);
        return connected;
    }

    // Runs on every connect: a clean session starts with no subscriptions.
    private async Task ResubscribeAsync(IMqttClient client, CancellationToken ct)
    {
        string[] topics = [.. _subscriptions.Keys.Select(prefix => prefix + "#"), .. _pending.Keys];
        if (topics.Length == 0) return;

        var builder = new MqttClientSubscribeOptionsBuilder();
        foreach (var topic in topics)
            builder.WithTopicFilter(TopicFilter(topic));

        var result = await client.SubscribeAsync(builder.Build(), ct).ConfigureAwait(false);
        MqttConnection.EnsureGranted(result);
        _logger.LogDebug("Handoff channel subscribed to {Count} topic(s)", topics.Length);
    }

    /// <inheritdoc />
    public async Task<TResponse> SendAsync<TMessage, TResponse>(
        string topic,
        string correlationId,
        TMessage message,
        TimeSpan timeout,
        CancellationToken ct = default)
        where TMessage : class
        where TResponse : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var client = _connection?.Client;
        if (client is null || !IsConnected)
            throw new InvalidOperationException("Handoff channel is not configured. Call ConfigureAsync first.");

        var requestTopic = $"{_namespace}/{topic}/request/{correlationId}";
        var replyTopic = $"{_namespace}/{topic}/reply/{correlationId}";

        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[replyTopic] = tcs;

        try
        {
            // Subscribe to reply topic before publishing the request
            await client.SubscribeAsync(
                TopicFilter(replyTopic), ct);

            // Publish the request
            var payload = JsonSerializer.SerializeToUtf8Bytes(message, _jsonOptions);
            await client.PublishAsync(new MqttApplicationMessageBuilder()
                .WithTopic(requestTopic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(_qualityOfService)
                .Build(), ct);

            _logger.LogDebug("Handoff request published to {Topic}", requestTopic);

            // Wait for the reply with timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            await using var reg = cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));

            var responseBytes = await tcs.Task;

            return JsonSerializer.Deserialize<TResponse>(responseBytes, _jsonOptions)
                ?? throw new InvalidOperationException(
                    $"Failed to deserialize handoff response for topic '{topic}'.");
        }
        finally
        {
            _pending.TryRemove(replyTopic, out _);

            try
            {
                await client.UnsubscribeAsync(replyTopic, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to unsubscribe from reply topic {Topic}", replyTopic);
            }
        }
    }

    /// <inheritdoc />
    public async Task CompleteAsync<TResponse>(
        string topic,
        string correlationId,
        TResponse response,
        CancellationToken ct = default)
        where TResponse : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var client = _connection?.Client;
        if (client is null || !IsConnected)
            throw new InvalidOperationException("Handoff channel is not configured. Call ConfigureAsync first.");

        var replyTopic = $"{_namespace}/{topic}/reply/{correlationId}";
        var payload = JsonSerializer.SerializeToUtf8Bytes(response, _jsonOptions);

        await client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(replyTopic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(_qualityOfService)
            .Build(), ct);

        _logger.LogDebug("Handoff response published to {Topic}", replyTopic);
    }

    /// <summary>
    /// Subscribes to incoming handoff requests on the specified topic and dispatches them
    /// to the <paramref name="handler"/>. The handler processes each request and returns a
    /// response, which is automatically published to the reply topic.
    /// </summary>
    /// <remarks>
    /// This is the responder-side counterpart to <see cref="SendAsync{TMessage, TResponse}"/>.
    /// Mirrors <c>InMemoryHandoffChannel.RegisterHandler</c> for MQTT deployments.
    /// The subscription uses MQTT <c>#</c> wildcard to match multi-segment correlation IDs.
    /// </remarks>
    /// <typeparam name="TMessage">The incoming request message type.</typeparam>
    /// <typeparam name="TResponse">The response type to send back.</typeparam>
    /// <param name="topic">The topic to listen on (e.g. "specialist-queue").</param>
    /// <param name="handler">Async function that processes the request and returns a response.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task SubscribeAsync<TMessage, TResponse>(
        string topic,
        Func<TMessage, CancellationToken, Task<TResponse>> handler,
        CancellationToken ct = default)
        where TMessage : class
        where TResponse : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(handler);

        var client = _connection?.Client;
        if (client is null || !IsConnected)
            throw new InvalidOperationException("Handoff channel is not configured. Call ConfigureAsync first.");

        var requestTopicPrefix = $"{_namespace}/{topic}/request/";
        var requestTopicFilter = $"{_namespace}/{topic}/request/#";

        _subscriptions[requestTopicPrefix] = async (fullTopic, payload, token) =>
        {
            var correlationId = fullTopic[requestTopicPrefix.Length..];
            var message = JsonSerializer.Deserialize<TMessage>(payload, _jsonOptions)
                ?? throw new InvalidOperationException(
                    $"Failed to deserialize handoff request on topic '{fullTopic}'.");

            _logger.LogDebug("Handoff request received on {Topic} (correlation: {CorrelationId})",
                fullTopic, correlationId);

            var response = await handler(message, token);
            await CompleteAsync(topic, correlationId, response, token);
        };

        await client.SubscribeAsync(
            TopicFilter(requestTopicFilter), ct);

        _logger.LogInformation("Handoff subscription active on {TopicFilter}", requestTopicFilter);
    }

    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        var payload = GetPayloadBytes(e.ApplicationMessage.Payload);

        // Check subscription handlers first (responder side)
        foreach (var (prefix, handler) in _subscriptions)
        {
            if (topic.StartsWith(prefix, StringComparison.Ordinal))
            {
                var running = Task.Run(async () =>
                {
                    try
                    {
                        await handler(topic, payload, _lifetime.Token);
                    }
                    catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                    {
                        // The channel was disposed while the handler ran.
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Handoff subscription handler failed for {Topic}", topic);
                    }
                });

                _handlers.TryAdd(running, 0);
                _ = running.ContinueWith(
                    finished => _handlers.TryRemove(finished, out _), TaskScheduler.Default);
                return Task.CompletedTask;
            }
        }

        // Check pending requests (requester side)
        if (_pending.TryRemove(topic, out var tcs))
        {
            tcs.TrySetResult(payload);
            _logger.LogDebug("Handoff reply received on {Topic} ({Size}B)", topic, payload.Length);
        }
        else
        {
            _logger.LogWarning("Received message on unmatched handoff topic {Topic}", topic);
        }

        return Task.CompletedTask;
    }

    private MqttTopicFilter TopicFilter(string topic) =>
        new MqttTopicFilterBuilder().WithTopic(topic).WithQualityOfServiceLevel(_qualityOfService).Build();

    private static byte[] GetPayloadBytes(ReadOnlySequence<byte> payload) =>
        payload.IsSingleSegment ? payload.FirstSpan.ToArray() : payload.ToArray();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _lifetime.CancelAsync();

        var running = _handlers.Keys.ToArray();
        if (running.Length > 0)
        {
            try
            {
                await Task.WhenAll(running).WaitAsync(HandlerDrain);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning(
                    "{Count} handoff subscription handler(s) still running {Seconds}s after dispose",
                    running.Length, HandlerDrain.TotalSeconds);
            }
        }

        foreach (var kvp in _pending)
            kvp.Value.TrySetCanceled();
        _pending.Clear();
        _subscriptions.Clear();

        if (_connection is not null)
            await _connection.DisposeAsync();

        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
