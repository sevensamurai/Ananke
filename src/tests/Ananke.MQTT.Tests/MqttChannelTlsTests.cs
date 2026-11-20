using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using Ananke.Abstractions.Channels;
using Ananke.Abstractions.Config;
using Shouldly;

namespace Ananke.MQTT.Tests;

/// <summary>
/// <see cref="MqttChannelReader{M, A}"/> and <see cref="MqttChannelWriter{A}"/> against a broker in the test
/// process that listens over TLS only, with a self-signed certificate.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MqttChannelTlsTests
{
    private static readonly MqttReconnectDelay ShortDelay = new(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200));

    private X509Certificate2 _certificate = null!;
    private InProcessBroker _broker = null!;

    [SetUp]
    public async Task SetUp()
    {
        _certificate = InProcessBroker.SelfSignedCertificate();
        _broker = await InProcessBroker.StartNewAsync(_certificate);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _broker.DisposeAsync();
        _certificate.Dispose();
    }

    [Test]
    public async Task ConfigureAsync_UseTls_ReaderAndWriterRoundTripOverTls()
    {
        var worker = new TextWorker();
        await using var reader = new MqttChannelReader<PingMessage, PingAction>
        {
            ReconnectDelay = ShortDelay,
            CertificateValidation = e => e.Certificate.GetCertHashString() == _certificate.Thumbprint,
        };
        await using var writer = new MqttChannelWriter<PingAction>
        {
            ReconnectDelay = ShortDelay,
            CertificateValidation = e => e.Certificate.GetCertHashString() == _certificate.Thumbprint,
        };

        (await reader.ConfigureAsync(Config(useTls: true), worker)).ShouldBeTrue();
        (await writer.ConfigureAsync(Config(useTls: true))).ShouldBeTrue();

        (await writer.SendAsync(new PingMessage { Text = "tls" }, PingAction.Ping)).Success.ShouldBeTrue();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        (await worker.Received.Reader.ReadAsync(cts.Token)).ShouldBe("tls");
    }

    [Test]
    public async Task ConfigureAsync_UseTls_HandoffRequestAndReplyOverTls()
    {
        await using var requester = new MqttHandoffChannel
        {
            ReconnectDelay = ShortDelay,
            CertificateValidation = e => e.Certificate.GetCertHashString() == _certificate.Thumbprint,
        };
        await using var responder = new MqttHandoffChannel
        {
            ReconnectDelay = ShortDelay,
            CertificateValidation = e => e.Certificate.GetCertHashString() == _certificate.Thumbprint,
        };
        (await requester.ConfigureAsync(Config(useTls: true))).ShouldBeTrue();
        (await responder.ConfigureAsync(Config(useTls: true))).ShouldBeTrue();
        await responder.SubscribeAsync<PingMessage, PingMessage>("tls",
            (request, _) => Task.FromResult(new PingMessage { Text = $"re:{request.Text}" }));

        var reply = await requester.SendAsync<PingMessage, PingMessage>(
            "tls", Guid.NewGuid().ToString("N"), new PingMessage { Text = "hello" }, TimeSpan.FromSeconds(10));

        reply.Text.ShouldBe("re:hello");
    }

    [Test]
    public async Task ConfigureAsync_UseTlsWithAnUntrustedCertificate_DoesNotConnect()
    {
        await using var writer = new MqttChannelWriter<PingAction> { ReconnectDelay = ShortDelay };

        (await writer.ConfigureAsync(Config(useTls: true))).ShouldBeFalse();
        writer.IsConnected.ShouldBeFalse();
    }

    private ChannelConfig Config(bool useTls) =>
        new() { Host = "127.0.0.1", Port = _broker.Port, Namespace = "test", UseTls = useTls };

    private sealed class TextWorker : IBackgroundWorker<PingMessage, PingAction>
    {
        public Channel<string> Received { get; } = Channel.CreateUnbounded<string>();

        public async Task HandleAsync(PingMessage? item, PingAction action, CancellationToken token) =>
            await Received.Writer.WriteAsync(item?.Text ?? string.Empty, token).ConfigureAwait(false);
    }
}
