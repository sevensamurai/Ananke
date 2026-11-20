using System.Diagnostics;
using System.Net;
using System.Text;
using Ananke.Abstractions.Config;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Shouldly;
using static Ananke.MQTT.Tests.MqttChannelBrokerTests;
using static Ananke.MQTT.Tests.MqttHandoffChannelBrokerTests;

namespace Ananke.MQTT.Tests;

/// <summary>
/// <see cref="MqttChannelReader{M, A}"/>, <see cref="MqttChannelWriter{A}"/> and <see cref="MqttHandoffChannel"/>
/// against Eclipse Mosquitto in a container that is restarted and stopped. The tests are ignored when Docker isn't
/// available, and fail when it is but the container doesn't start.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class MqttMosquittoContainerTests
{
    private const int MqttPort = 1883;
    private const string Image = "eclipse-mosquitto:2";

    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(10);

    // A fixed host port, so the broker comes back where the clients look for it after a restart. Docker binds it on
    // every address, so it is checked there.
    private readonly int _hostPort = InProcessBroker.FreePort(IPAddress.Any);
    private IContainer? _container;
    private string? _unavailable;
    private string? _startFailure;

    private IContainer Container =>
        _container ?? throw new InvalidOperationException("No container: the fixture did not start");

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        try
        {
            _container = new ContainerBuilder(Image)
                .WithPortBinding(_hostPort, MqttPort)
                .WithResourceMapping(Encoding.ASCII.GetBytes("listener 1883\nallow_anonymous true\n"),
                    "/mosquitto/config/mosquitto.conf")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged(@"mosquitto version \S+ running"))
                .Build();
            await _container.StartAsync().WaitAsync(TimeSpan.FromMinutes(3));
        }
        catch (DockerUnavailableException e)
        {
            _container = null;
            _unavailable = $"No Mosquitto container: Docker isn't available ({e.Message})";
        }
        catch (Exception e)
        {
            await DisposeQuietlyAsync(_container);
            _container = null;
            _startFailure = $"The Mosquitto container didn't start ({e.GetType().Name}: {e.Message})";
        }
    }

    /// <summary>Ignores the test when Docker isn't available; fails it when Docker is there but the broker didn't start.</summary>
    [SetUp]
    public void RequireContainer()
    {
        if (_startFailure is not null)
            Assert.Fail(_startFailure);
        if (_container is null)
            Assert.Ignore(_unavailable ?? "No Mosquitto container: Docker isn't available");
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    [Test]
    public async Task SendAsync_WriterToReader_RoundTrips()
    {
        var worker = new RecordingWorker();
        await using var reader = new MqttChannelReader<PingMessage, PingAction> { ReconnectDelay = ShortDelay };
        await using var writer = new MqttChannelWriter<PingAction> { ReconnectDelay = ShortDelay };
        var config = Config();

        (await reader.ConfigureAsync(config, worker)).ShouldBeTrue();
        (await writer.ConfigureAsync(config)).ShouldBeTrue();

        await RoundTripAsync(writer, worker, "hello");
    }

    [Test]
    public async Task ConfigureAsync_BrokerRestart_ReaderAndWriterReconnectAndAMessageRoundTrips()
    {
        var worker = new RecordingWorker();
        await using var reader = new MqttChannelReader<PingMessage, PingAction> { ReconnectDelay = ShortDelay };
        await using var writer = new MqttChannelWriter<PingAction> { ReconnectDelay = ShortDelay };
        var config = Config();
        (await reader.ConfigureAsync(config, worker)).ShouldBeTrue();
        (await writer.ConfigureAsync(config)).ShouldBeTrue();
        await RoundTripAsync(writer, worker, "before");

        await RestartBrokerAsync();
        await WaitUntilAsync(() => reader.ConnectCount == 2 && writer.ConnectCount == 2, ReplyTimeout);

        reader.IsConnected.ShouldBeTrue();
        writer.IsConnected.ShouldBeTrue();
        await RoundTripAsync(writer, worker, "after");
    }

    /// <summary>A 45 s outage at the default reconnect delay, then a round trip. Takes about a minute.</summary>
    [Test]
    public async Task ConfigureAsync_45SecondOutageAtTheDefaultDelay_ReaderAndWriterReconnectAndAMessageRoundTrips()
    {
        var worker = new RecordingWorker();
        await using var reader = new MqttChannelReader<PingMessage, PingAction>();
        await using var writer = new MqttChannelWriter<PingAction>();
        var config = Config();
        (await reader.ConfigureAsync(config, worker)).ShouldBeTrue();
        (await writer.ConfigureAsync(config)).ShouldBeTrue();
        await RoundTripAsync(writer, worker, "before");

        await Container.StopAsync().WaitAsync(TimeSpan.FromMinutes(1));
        await WaitUntilAsync(() => !reader.IsConnected && !writer.IsConnected);
        await Task.Delay(TimeSpan.FromSeconds(45));
        await Container.StartAsync().WaitAsync(TimeSpan.FromMinutes(1));
        var back = Stopwatch.StartNew();
        await WaitUntilAsync(() => reader.ConnectCount == 2 && writer.ConnectCount == 2,
            MqttReconnectDelay.Default.Max + TimeSpan.FromSeconds(5));
        back.Stop();

        TestContext.Out.WriteLine($"Both clients back {back.Elapsed.TotalSeconds:F1} s after the broker");
        back.Elapsed.ShouldBeLessThan(MqttReconnectDelay.Default.Max + TimeSpan.FromSeconds(1),
            "both clients connect within the capped delay of the broker coming back");
        await RoundTripAsync(writer, worker, "after");
    }

    [Test]
    public async Task SendAsync_HandoffAfterABrokerRestart_GetsItsReply()
    {
        await using var requester = new MqttHandoffChannel { ReconnectDelay = ShortDelay };
        await using var responder = new MqttHandoffChannel { ReconnectDelay = ShortDelay };
        var config = Config();
        (await requester.ConfigureAsync(config)).ShouldBeTrue();
        (await responder.ConfigureAsync(config)).ShouldBeTrue();
        await responder.SubscribeAsync<HandoffRequest, HandoffReply>("specialist",
            (request, _) => Task.FromResult(new HandoffReply { Text = $"re:{request.Text}" }));
        (await SendAsync(requester, "one", ReplyTimeout)).Text.ShouldBe("re:one");

        await RestartBrokerAsync();
        await WaitUntilAsync(() => requester.ConnectCount == 2 && responder.ConnectCount == 2, ReplyTimeout);

        (await SendAsync(requester, "two", ReplyTimeout)).Text.ShouldBe("re:two");
    }

    /// <summary>Each test on topics of its own, so no message from an earlier test reaches it.</summary>
    private ChannelConfig Config() =>
        new() { Host = Container.Hostname, Port = _hostPort, Namespace = $"test{Guid.NewGuid():N}" };

    private async Task RestartBrokerAsync()
    {
        await Container.StopAsync().WaitAsync(TimeSpan.FromMinutes(1)).ConfigureAwait(false);
        await Container.StartAsync().WaitAsync(TimeSpan.FromMinutes(1)).ConfigureAwait(false);
    }

    // A container that failed to start may fail to dispose too; the fixture is ignored either way.
    private static async Task DisposeQuietlyAsync(IContainer? container)
    {
        if (container is null)
            return;
        try
        {
            await container.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            TestContext.Progress.WriteLine($"Mosquitto container not disposed ({e.GetType().Name}: {e.Message})");
        }
    }
}
