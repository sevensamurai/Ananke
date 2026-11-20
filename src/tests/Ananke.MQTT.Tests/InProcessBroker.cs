using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace Ananke.MQTT.Tests;

/// <summary>
/// An MQTTnet broker in the test process, on a free port. <see cref="StopAsync"/> and
/// <see cref="StartAsync"/> take it down and bring it back on the same port, with no retained state,
/// as a restarted broker with clean sessions would come back. Given a certificate, it listens over TLS only.
/// </summary>
internal sealed class InProcessBroker(X509Certificate2? certificate = null) : IAsyncDisposable
{
    private MqttServer? _server;

    public int Port { get; } = FreePort(IPAddress.Loopback);

    /// <summary>Every subscription the broker accepted, in order: topic filter and QoS.</summary>
    public ConcurrentQueue<(string Topic, MqttQualityOfServiceLevel QoS)> Subscriptions { get; } = new();

    /// <summary>Every connect the broker accepted, in order: client id and clean session.</summary>
    public ConcurrentQueue<(string ClientId, bool CleanSession)> Connections { get; } = new();

    /// <summary>Every message a client published, in order: topic and QoS.</summary>
    public ConcurrentQueue<(string Topic, MqttQualityOfServiceLevel QoS)> Publishes { get; } = new();

    /// <summary>The ids of the clients connected now.</summary>
    public ConcurrentDictionary<string, byte> Connected { get; } = new();

    /// <summary>How many of the next subscriptions the broker refuses as not authorized.</summary>
    public int SubscriptionsToRefuse { get; set; }

    /// <summary>Every subscription waits for this task before the broker answers it.</summary>
    public Task SubscriptionsHeldUntil { get; set; } = Task.CompletedTask;

    /// <summary>How many subscriptions have reached the broker, held or not.</summary>
    public int SubscriptionsReceived => Volatile.Read(ref _subscriptionsReceived);

    private int _subscriptionsReceived;

    public static async Task<InProcessBroker> StartNewAsync(X509Certificate2? certificate = null)
    {
        var broker = new InProcessBroker(certificate);
        await broker.StartAsync();
        return broker;
    }

    public async Task StartAsync()
    {
        // Keeps the session of a client that connects without a clean session, as brokers do.
        var builder = new MqttServerOptionsBuilder().WithPersistentSessions();
        if (certificate is null)
        {
            builder.WithDefaultEndpoint()
                .WithDefaultEndpointBoundIPAddress(IPAddress.Loopback)
                .WithDefaultEndpointPort(Port)
                .WithDefaultEndpointReuseAddress();
        }
        else
        {
            builder.WithoutDefaultEndpoint()
                .WithEncryptedEndpoint()
                .WithEncryptedEndpointBoundIPAddress(IPAddress.Loopback)
                .WithEncryptedEndpointPort(Port)
                .WithEncryptionCertificate(certificate);
        }

        _server = new MqttServerFactory().CreateMqttServer(builder.Build());
        _server.InterceptingSubscriptionAsync += async e =>
        {
            Interlocked.Increment(ref _subscriptionsReceived);
            await SubscriptionsHeldUntil;

            if (SubscriptionsToRefuse > 0)
            {
                SubscriptionsToRefuse--;
                e.Response.ReasonCode = MqttSubscribeReasonCode.NotAuthorized;
                e.ProcessSubscription = false;
                return;
            }

            Subscriptions.Enqueue((e.TopicFilter.Topic, e.TopicFilter.QualityOfServiceLevel));
        };
        _server.ValidatingConnectionAsync += e =>
        {
            Connections.Enqueue((e.ClientId, e.CleanSession == true));
            return Task.CompletedTask;
        };
        _server.ClientConnectedAsync += e =>
        {
            Connected[e.ClientId] = 0;
            return Task.CompletedTask;
        };
        _server.ClientDisconnectedAsync += e =>
        {
            Connected.TryRemove(e.ClientId, out _);
            return Task.CompletedTask;
        };
        _server.InterceptingPublishAsync += e =>
        {
            Publishes.Enqueue((e.ApplicationMessage.Topic, e.ApplicationMessage.QualityOfServiceLevel));
            return Task.CompletedTask;
        };
        await _server.StartAsync();
    }

    public async Task StopAsync()
    {
        if (_server is null) return;
        await _server.StopAsync();
        _server.Dispose();
        _server = null;
        Connected.Clear();
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    /// <summary>A self-signed certificate for 127.0.0.1 and localhost, with its private key.</summary>
    public static X509Certificate2 SelfSignedCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1")], critical: false));

        var now = TimeProvider.System.GetUtcNow();
        using var created = request.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), password: null);
    }

    /// <summary>A port nothing listens on at <paramref name="address"/> now.</summary>
    internal static int FreePort(IPAddress address)
    {
        using var listener = new TcpListener(address, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
