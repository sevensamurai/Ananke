using Ananke.Abstractions.Channels;
using Ananke.Abstractions.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using MQTTnet.Protocol;

namespace Ananke.MQTT;

/// <summary>
/// MQTT-backed implementation of <see cref="IChannelWriter{A}"/>.
/// Publishes messages to MQTT topics derived from the action enum.
/// </summary>
/// <remarks>
/// From <see cref="ConfigureAsync"/> until <see cref="ClearAsync"/> or dispose, a lost connection is
/// retried with a capped delay between attempts; <see cref="IsConnected"/> is the client's own state, and
/// <see cref="SendAsync"/> fails while it is false. Messages are published at the configured
/// <see cref="ChannelConfig.QualityOfService"/>.
/// </remarks>
/// <typeparam name="A">Action/transition enum type used for topic routing.</typeparam>
public sealed class MqttChannelWriter<A>(ILogger<MqttChannelWriter<A>>? logger = null) : IChannelWriter<A> where A : Enum
{
    private readonly ILogger<MqttChannelWriter<A>> _logger = logger ?? NullLogger<MqttChannelWriter<A>>.Instance;

    private MqttConnection? _connection;
    private string _namespace = string.Empty;
    private MqttQualityOfServiceLevel _qualityOfService;
    private bool _disposed;

    /// <summary>The wait between reconnect attempts.</summary>
    internal MqttReconnectDelay ReconnectDelay { get; init; } = MqttReconnectDelay.Default;

    /// <summary>Replaces the validation of the broker's TLS certificate against the trusted roots.</summary>
    internal Func<MqttClientCertificateValidationEventArgs, bool>? CertificateValidation { get; init; }

    /// <summary>How many connects have completed.</summary>
    internal int ConnectCount => _connection?.ConnectCount ?? 0;

    public bool IsConnected => _connection?.IsConnected == true;

    public async Task<bool> ConfigureAsync(ChannelConfig credentials, CancellationToken token = default)
    {
        MqttConnection.Validate(credentials);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // A second ConfigureAsync replaces the client, and its reconnect loop, with a new one.
        await ClearAsync(CancellationToken.None).ConfigureAwait(false);

        var connection = new MqttConnection(
            MqttConnection.OptionsFor(credentials, ["writer", typeof(A).ToString()], CertificateValidation),
            (_, _) => Task.CompletedTask, ReconnectDelay, "TX Channel", _logger);
        _namespace = credentials.Namespace;
        _qualityOfService = MqttConnection.QualityOfServiceFor(credentials);
        _connection = connection;
        return await connection.StartAsync(token).ConfigureAwait(false);
    }

    public async Task<ChannelSendResult> SendAsync(object message, A action, CancellationToken ct = default)
    {
        var connection = _connection;
        if (connection is null)
            return ChannelSendResult.Failed("Client not configured");

        if (!connection.IsConnected)
            return ChannelSendResult.Failed("Client not connected");

        try
        {
            var payload = DataSerializer.Serialize(message);
            var topic = NamespaceMapper.GetTopic(_namespace, action);

            var applicationMessage = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(_qualityOfService)
                .Build();

            var resp = await connection.Client.PublishAsync(applicationMessage, ct).ConfigureAwait(false);
            _logger.LogDebug("TX Channel sent to {Topic} ({Size}B) -> {Status}", topic, payload.Length, resp.ReasonCode);

            return resp.ReasonCode == MqttClientPublishReasonCode.Success
                ? ChannelSendResult.Succeeded()
                : ChannelSendResult.Failed($"MQTT publish failed: {resp.ReasonCode}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "TX Channel send error");
            return ChannelSendResult.Failed(ex.Message);
        }
    }

    /// <summary>Stops the reconnect loop and disconnects.</summary>
    public async Task ClearAsync(CancellationToken ct = default)
    {
        var connection = _connection;
        if (connection is null) return;
        _connection = null;
        await connection.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await ClearAsync().ConfigureAwait(false);

        GC.SuppressFinalize(this);
    }
}
