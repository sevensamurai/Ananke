using Ananke.Abstractions.Channels;
using Ananke.Abstractions.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;

namespace Ananke.MQTT;

/// <summary>
/// MQTT-backed implementation of <see cref="IChannelWriter{A}"/>.
/// Publishes messages to MQTT topics derived from the action enum.
/// </summary>
/// <typeparam name="A">Action/transition enum type used for topic routing.</typeparam>
public sealed class MqttChannelWriter<A>(ILogger<MqttChannelWriter<A>>? logger = null) : IChannelWriter<A> where A : Enum
{
    private readonly ILogger<MqttChannelWriter<A>> _logger = logger ?? NullLogger<MqttChannelWriter<A>>.Instance;

    private IMqttClient? _client;
    private MqttClientOptions? _options;
    private string _namespace = string.Empty;
    private bool _linked;
    private bool _disposed;

    public bool IsConnected => _linked && _client?.IsConnected == true;

    public async Task<bool> ConfigureAsync(ChannelConfig credentials, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentials.Host);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentials.Namespace);

        _namespace = credentials.Namespace;
        _client = new MqttClientFactory().CreateMqttClient();

        _options = new MqttClientOptionsBuilder()
            .WithTcpServer(credentials.Host, credentials.Port)
            .WithCredentials(credentials.Username, credentials.Password)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithCleanSession()
            .Build();

        _client.DisconnectedAsync += async _ =>
        {
            _logger.LogWarning("TX Channel disconnected");
            if (!_disposed)
                await ReconnectAsync();
        };

        return await ConnectAsync(token);
    }

    public async Task<ChannelSendResult> SendAsync(object message, A action)
    {
        if (_client is null)
            return ChannelSendResult.Failed("Client not configured");

        if (!IsConnected)
            return ChannelSendResult.Failed("Client not connected");

        try
        {
            var payload = DataSerializer.Serialize(message);
            var topic = NamespaceMapper.GetTopic(_namespace, action);

            var applicationMessage = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce)
                .Build();

            var resp = await _client.PublishAsync(applicationMessage);
            _logger.LogDebug("TX Channel sent to {Topic} ({Size}B) -> {Status}", topic, payload.Length, resp.ReasonCode);

            return resp.ReasonCode == MqttClientPublishReasonCode.Success
                ? ChannelSendResult.Succeeded()
                : ChannelSendResult.Failed($"MQTT publish failed: {resp.ReasonCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TX Channel send error");
            return ChannelSendResult.Failed(ex.Message);
        }
    }

    public async Task ClearAsync()
    {
        if (_client is not null)
            await _client.DisconnectAsync();
        _linked = false;
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
            _logger.LogError(ex, "TX Channel unable to connect");
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
                    _logger.LogInformation("TX Channel reconnected after {Attempt} attempt(s)", attempt);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TX Channel reconnection attempt {Attempt}/{MaxAttempts} failed",
                    attempt, maxAttempts);
            }

            delay *= 2;
        }

        _logger.LogError("TX Channel failed to reconnect after {MaxAttempts} attempts", maxAttempts);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_client is not null)
        {
            await _client.DisconnectAsync();
            _client.Dispose();
            _client = null;
        }

        GC.SuppressFinalize(this);
    }
}
