using System.Buffers;
using System.Collections.Concurrent;
using System.Text.Json;
using Ananke.Abstractions.Channels;
using Ananke.Abstractions.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;

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

    private const int MaxReconnectAttempts = 10;

    private IMqttClient? _client;
    private MqttClientOptions? _options;
    private string _namespace = string.Empty;
    private bool _configured;
    private bool _disposed;
    private bool _reconnectFailed;

    /// <summary>Whether the channel is connected to the MQTT broker.</summary>
    public bool IsConnected => _configured && !_reconnectFailed && _client?.IsConnected == true;

    /// <summary>
    /// Connects to the MQTT broker. Must be called before <see cref="SendAsync{TMessage, TResponse}"/>
    /// or <see cref="CompleteAsync{TResponse}"/>.
    /// </summary>
    public async Task<bool> ConfigureAsync(ChannelConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Host);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Namespace);

        _namespace = config.Namespace;
        _client = new MqttClientFactory().CreateMqttClient();

        _options = new MqttClientOptionsBuilder()
            .WithTcpServer(config.Host, config.Port)
            .WithCredentials(config.Username, config.Password)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithCleanSession()
            .Build();

        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;

        _client.DisconnectedAsync += async _ =>
        {
            _logger.LogDebug("Handoff channel disconnected");
            if (_disposed) return;

            var delay = TimeSpan.FromSeconds(1);
            var maxDelay = TimeSpan.FromSeconds(30);
            var attempt = 0;

            while (!_disposed && attempt < MaxReconnectAttempts)
            {
                try
                {
                    attempt++;
                    await Task.Delay(delay);
                    await _client.ConnectAsync(_options, CancellationToken.None);
                    _logger.LogInformation("Handoff channel reconnected after {Attempt} attempt(s)", attempt);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Handoff channel reconnection attempt {Attempt}/{Max} failed",
                        attempt, MaxReconnectAttempts);
                    delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, maxDelay.Ticks));
                }
            }

            if (!_disposed)
            {
                _reconnectFailed = true;
                _logger.LogError(
                    "Handoff channel permanently disconnected after {Max} reconnect attempts",
                    MaxReconnectAttempts);

                // Fail all pending requests so callers are not blocked indefinitely
                foreach (var kvp in _pending)
                    kvp.Value.TrySetException(
                        new InvalidOperationException("MQTT handoff channel lost connection permanently."));
            }
        };

        try
        {
            var resp = await _client.ConnectAsync(_options, ct);
            _configured = resp.ResultCode == MqttClientConnectResultCode.Success;
            _logger.LogInformation("Handoff channel connected to {Host}:{Port}", config.Host, config.Port);
            return _configured;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handoff channel failed to connect to {Host}:{Port}", config.Host, config.Port);
            return false;
        }
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

        if (_client is null || !IsConnected)
            throw new InvalidOperationException("Handoff channel is not configured. Call ConfigureAsync first.");

        var requestTopic = $"{_namespace}/{topic}/request/{correlationId}";
        var replyTopic = $"{_namespace}/{topic}/reply/{correlationId}";

        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[replyTopic] = tcs;

        try
        {
            // Subscribe to reply topic before publishing the request
            await _client.SubscribeAsync(
                new MqttTopicFilterBuilder().WithTopic(replyTopic).Build(), ct);

            // Publish the request
            var payload = JsonSerializer.SerializeToUtf8Bytes(message, _jsonOptions);
            await _client.PublishAsync(new MqttApplicationMessageBuilder()
                .WithTopic(requestTopic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce)
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
                await _client.UnsubscribeAsync(replyTopic, ct);
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

        if (_client is null || !IsConnected)
            throw new InvalidOperationException("Handoff channel is not configured. Call ConfigureAsync first.");

        var replyTopic = $"{_namespace}/{topic}/reply/{correlationId}";
        var payload = JsonSerializer.SerializeToUtf8Bytes(response, _jsonOptions);

        await _client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(replyTopic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce)
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
        Func<TMessage, Task<TResponse>> handler,
        CancellationToken ct = default)
        where TMessage : class
        where TResponse : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(handler);

        if (_client is null || !IsConnected)
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

            var response = await handler(message);
            await CompleteAsync(topic, correlationId, response, token);
        };

        await _client.SubscribeAsync(
            new MqttTopicFilterBuilder().WithTopic(requestTopicFilter).Build(), ct);

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
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await handler(topic, payload, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Handoff subscription handler failed for {Topic}", topic);
                    }
                });
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

    private static byte[] GetPayloadBytes(ReadOnlySequence<byte> payload) =>
        payload.IsSingleSegment ? payload.FirstSpan.ToArray() : payload.ToArray();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var kvp in _pending)
            kvp.Value.TrySetCanceled();
        _pending.Clear();
        _subscriptions.Clear();

        if (_client is not null)
        {
            try { await _client.DisconnectAsync(); }
            catch (Exception ex) { _logger.LogDebug(ex, "MQTT disconnect failed during dispose — ignoring"); }
            _client.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
