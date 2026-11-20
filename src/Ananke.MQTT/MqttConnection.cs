using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Ananke.Abstractions.Config;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;

namespace Ananke.MQTT;

/// <summary>
/// One MQTT client and the one reconnect loop that keeps it connected until the connection is disposed.
/// </summary>
/// <remarks>
/// <see cref="StartAsync"/> connects once and starts the loop. The client's <c>DisconnectedAsync</c> only
/// signals the loop, which then makes one attempt per <see cref="MqttReconnectDelay"/> until the client is
/// connected and <c>onConnected</c> has completed; nothing else calls <c>ConnectAsync</c>, so attempts
/// never overlap. After every connect, first or later, <c>onConnected</c> runs (to subscribe); if it
/// throws or is cancelled, the client is disconnected and the loop tries again.
/// A connection holds its client id from construction until it is disposed: constructing a second one with an
/// id in use in the process throws.
/// </remarks>
internal sealed class MqttConnection(
    MqttClientOptions options,
    Func<IMqttClient, CancellationToken, Task> onConnected,
    MqttReconnectDelay delay,
    string name,
    ILogger logger) : IAsyncDisposable
{
    private const int ClientIdPrefixLength = 14;

    // The client ids of the connections in this process that are not disposed.
    private static readonly ConcurrentDictionary<string, byte> ClientIdsInUse = new();

    // First, so a client id already in use throws before anything else is created.
    private readonly string? _clientId = Claim(options.ClientId);

    private readonly CancellationTokenSource _lifetime = new();
    private readonly Channel<bool> _disconnected = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private Task _loop = Task.CompletedTask;
    private int _connectCount;
    private int _disposed;
    private volatile bool _ready;

    /// <summary>The client. Publish and subscribe on it; connect and disconnect through this connection.</summary>
    public IMqttClient Client { get; } = new MqttClientFactory().CreateMqttClient();

    public bool IsConnected => Client.IsConnected;

    /// <summary>How many connects have completed, <c>onConnected</c> included.</summary>
    public int ConnectCount => Volatile.Read(ref _connectCount);

    /// <summary>Connected, with <c>onConnected</c> completed since that connect.</summary>
    public bool IsReady => _ready && Client.IsConnected;

    /// <summary>
    /// The token cancelled when the connection is disposed. Work started for the client, such as handing a
    /// received message on, takes it, so a dispose does not wait for it. Read it before the connection is
    /// disposed: it reads the token source, which the dispose disposes.
    /// </summary>
    public CancellationToken Lifetime => _lifetime.Token;

    /// <summary>
    /// Throws when <paramref name="config"/> can't be connected with: no host or namespace, an undefined
    /// quality of service, or a kept session with no client id.
    /// </summary>
    public static void Validate(ChannelConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Host);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Namespace);
        if (!Enum.IsDefined(config.QualityOfService))
            throw new ArgumentException($"Unknown quality of service {config.QualityOfService}.", nameof(config));
        if (!config.CleanSession && string.IsNullOrWhiteSpace(config.ClientId))
            throw new ArgumentException(
                $"{nameof(ChannelConfig.CleanSession)} false needs a {nameof(ChannelConfig.ClientId)}: the broker keeps the session by client id.",
                nameof(config));
    }

    /// <summary>
    /// The options a channel connects with: the broker, credentials, a 30 s keep-alive, TLS when asked for,
    /// the session as configured, and <see cref="ClientIdFor"/> as the client id when a client id is set.
    /// </summary>
    /// <param name="config">The channel's configuration.</param>
    /// <param name="client">What the client is: its role and the types and name that tell it from the others.</param>
    /// <param name="certificateValidation">Replaces the validation of the broker's certificate against the trusted roots.</param>
    public static MqttClientOptions OptionsFor(
        ChannelConfig config,
        IEnumerable<string> client,
        Func<MqttClientCertificateValidationEventArgs, bool>? certificateValidation = null)
    {
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(config.Host, config.Port)
            .WithCredentials(config.Username, config.Password)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithCleanSession(config.CleanSession);

        // MQTT 5 ends a session at disconnect unless it has an expiry interval; the maximum never expires.
        if (!config.CleanSession)
            builder.WithSessionExpiryInterval(uint.MaxValue);

        if (!string.IsNullOrWhiteSpace(config.ClientId))
            builder.WithClientId(ClientIdFor(config.ClientId, [.. client, config.Namespace]));

        if (config.UseTls)
        {
            builder.WithTlsOptions(tls =>
            {
                tls.UseTls();
                if (certificateValidation is not null)
                    tls.WithCertificateValidationHandler(certificateValidation);
            });
        }

        return builder.Build();
    }

    /// <summary>
    /// <c>{clientId cut to 14 characters}-{8 hex digits}</c>, the hex digits the start of the SHA-256 of the whole
    /// <paramref name="clientId"/> and <paramref name="client"/>: at most 23 characters, the length the MQTT spec
    /// has every broker accept, and the same in every process.
    /// </summary>
    private static string ClientIdFor(string clientId, IEnumerable<string> client)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', [clientId, .. client])));
        var prefix = clientId.Length > ClientIdPrefixLength ? clientId[..ClientIdPrefixLength] : clientId;
        return $"{prefix}-{Convert.ToHexStringLower(hash.AsSpan(0, 4))}";
    }

    private static string? Claim(string? clientId)
    {
        if (string.IsNullOrEmpty(clientId)) return null;
        if (!ClientIdsInUse.TryAdd(clientId, 0))
            throw new InvalidOperationException(
                $"The MQTT client id '{clientId}' is already in use in this process. Give the second channel another " +
                $"{nameof(ChannelConfig.ClientId)}, or a handoff channel another {nameof(MqttHandoffChannel.Name)}.");
        return clientId;
    }

    /// <summary>The configured quality of service, as MQTTnet's level.</summary>
    public static MqttQualityOfServiceLevel QualityOfServiceFor(ChannelConfig config) =>
        (MqttQualityOfServiceLevel)config.QualityOfService;

    /// <summary>Throws when the broker refused any topic filter of a subscribe, so the caller can treat it as failed.</summary>
    public static void EnsureGranted(MqttClientSubscribeResult result)
    {
        foreach (var item in result.Items)
        {
            if (item.ResultCode > MqttClientSubscribeResultCode.GrantedQoS2)
                throw new InvalidOperationException(
                    $"The broker refused the subscription to '{item.TopicFilter.Topic}': {item.ResultCode}");
        }
    }

    /// <summary>
    /// Connects, then starts the reconnect loop whether or not the connect succeeded.
    /// </summary>
    /// <returns>Whether the first connect succeeded.</returns>
    public async Task<bool> StartAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _disposed) == 1) return false;

        // Read once: a dispose that runs while this connects disposes the source, not the token.
        var lifetime = _lifetime.Token;
        Client.DisconnectedAsync += OnDisconnectedAsync;

        var connected = false;
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime))
        {
            try
            {
                connected = await TryConnectAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                logger.LogError("{Name} unable to connect: cancelled", name);
            }
        }

        if (!connected)
            _disconnected.Writer.TryWrite(true);

        _loop = Task.Run(() => RunAsync(lifetime), CancellationToken.None);
        return connected;
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        _ready = false;
        if (_lifetime.IsCancellationRequested)
            return Task.CompletedTask;

        if (e.ClientWasConnected)
            logger.LogWarning("{Name} disconnected: {Reason}", name, e.ReasonString ?? e.Reason.ToString());

        _disconnected.Writer.TryWrite(true);
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            while (await _disconnected.Reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                _disconnected.Reader.TryRead(out _);

                // A failed attempt raises DisconnectedAsync too, which leaves a signal behind once the
                // client is connected again; it is read here and finds the client ready.
                for (var attempt = 0; !IsReady; attempt++)
                {
                    await Task.Delay(delay.For(attempt), token).ConfigureAwait(false);
                    if (await TryConnectAsync(token, attempt + 1).ConfigureAwait(false))
                        logger.LogInformation("{Name} reconnected after {Attempt} attempt(s)", name, attempt + 1);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Disposed.
        }
    }

    private async Task<bool> TryConnectAsync(CancellationToken token, int attempt = 0)
    {
        // Not ready until onConnected completes on this connect, whatever an earlier one left behind.
        _ready = false;

        // Left connected by a set-up that was cancelled or a disconnect that failed: start over.
        if (Client.IsConnected)
            await DisconnectQuietlyAsync().ConfigureAwait(false);

        try
        {
            var result = await Client.ConnectAsync(options, token).ConfigureAwait(false);
            if (result.ResultCode != MqttClientConnectResultCode.Success)
            {
                LogFailed(attempt, result.ResultCode.ToString(), null);
                return false;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogFailed(attempt, ex.Message, ex);
            return false;
        }

        try
        {
            await onConnected(Client, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Connected but not set up: the loop retries only a client that is not ready.
            await DisconnectQuietlyAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Name} connected, but setting up the connection failed; reconnecting", name);
            await DisconnectQuietlyAsync().ConfigureAwait(false);
            return false;
        }

        _ready = true;
        Interlocked.Increment(ref _connectCount);
        return true;
    }

    private void LogFailed(int attempt, string reason, Exception? ex)
    {
        if (attempt == 0)
            logger.LogError(ex, "{Name} unable to connect: {Reason}", name, reason);
        else
            logger.LogWarning("{Name} reconnect attempt {Attempt} failed: {Reason}", name, attempt, reason);
    }

    private async Task DisconnectQuietlyAsync()
    {
        try
        {
            if (Client.IsConnected)
                await Client.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "{Name} disconnect failed", name);
        }
    }

    /// <summary>Stops the reconnect loop, disconnects and disposes the client.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        try
        {
            await _lifetime.CancelAsync().ConfigureAwait(false);
            Client.DisconnectedAsync -= OnDisconnectedAsync;
            await _loop.ConfigureAwait(false);

            await DisconnectQuietlyAsync().ConfigureAwait(false);
            Client.Dispose();
            _lifetime.Dispose();
        }
        finally
        {
            if (_clientId is not null)
                ClientIdsInUse.TryRemove(_clientId, out _);
        }
    }
}
