using Ananke.Abstractions;
using Ananke.Abstractions.Channels;
using Ananke.Abstractions.Config;
using Ananke.Abstractions.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using System.Buffers;

namespace Ananke.MQTT;

/// <summary>
/// MQTT-backed implementation of <see cref="IChannelReader{M, A}"/>.
/// Subscribes to MQTT topics and dispatches messages to the provided <see cref="IBackgroundWorker{T}"/>.
/// </summary>
/// <typeparam name="M">Message type implementing <see cref="IBaseContext"/>.</typeparam>
/// <typeparam name="A">Action/transition enum type used for topic routing.</typeparam>
public sealed class MqttChannelReader<M, A> : IChannelReader<M, A>, IAsyncDisposable
    where M : class, IBaseContext
    where A : Enum
{
    private readonly ILogger<MqttChannelReader<M, A>> _logger;

    private IMqttClient? _client;
    private IBackgroundWorker<M>? _worker;
    private MqttClientOptions? _options;
    private string? _topic;
    private bool _linked;
    private bool _disposed;

    public MqttChannelReader(ILogger<MqttChannelReader<M, A>>? logger = null)
    {
        _logger = logger ?? NullLogger<MqttChannelReader<M, A>>.Instance;
    }

    private static byte[] GetPayloadBytes(ReadOnlySequence<byte> payload)
    {
        return payload.IsSingleSegment ? payload.FirstSpan.ToArray() : payload.ToArray();
    }

    public async Task<bool> ConfigureAsync(ChannelConfig config, IBackgroundWorker<M> consumer, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Host);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Namespace);

        var topic = NamespaceMapper.GetTopicWildcard<A>(config.Namespace);
        if (!string.IsNullOrWhiteSpace(config.GroupName))
            topic = $"$share/{config.GroupName}/{topic}";

        return await SetupClient(config, consumer, topic, useAction: false, token: token);
    }

    public async Task<bool> ConfigureAsync(ChannelConfig config, IBackgroundWorker<M> consumer, A action, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Host);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Namespace);

        var topic = NamespaceMapper.GetTopic(config.Namespace, action);
        if (!string.IsNullOrWhiteSpace(config.GroupName))
            topic = $"$share/{config.GroupName}/{topic}";

        return await SetupClient(config, consumer, topic, useAction: true, token: token);
    }

    private async Task<bool> SetupClient(ChannelConfig config, IBackgroundWorker<M> consumer, string topic, bool useAction, CancellationToken token)
    {
        _topic = topic;
        _worker = consumer;

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

            if (!useAction)
            {
                var action = NamespaceMapper.GetActionFromTopic(e.ApplicationMessage.Topic);
                message.Command = action;
            }
            _logger.LogDebug("Received message from {Topic}", e.ApplicationMessage.Topic);
            await _worker.HandleAsync(message, token);
        };

        _client.ConnectedAsync += async _ =>
        {
            _logger.LogDebug("RX Channel connected");
            if (_client is not null && !string.IsNullOrEmpty(_topic))
                await _client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(_topic).Build());
        };

        _client.DisconnectedAsync += async _ =>
        {
            _logger.LogWarning("RX Channel disconnected");
            if (!_disposed)
                await ReconnectAsync();
        };

        return await ConnectAsync(token);
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
            var resp = await _client.ConnectAsync(_options, token);
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
                await Task.Delay(delay);
                if (await ConnectAsync(CancellationToken.None))
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

    public async Task Clear()
    {
        if (_client is not null)
        {
            if (!string.IsNullOrEmpty(_topic))
                await _client.UnsubscribeAsync(_topic);
            await _client.DisconnectAsync();
        }
        _linked = false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await Clear();

        if (_client is not null)
        {
            _client.Dispose();
            _client = null;
        }

        GC.SuppressFinalize(this);
    }
}
