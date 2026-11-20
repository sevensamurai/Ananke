using System.Threading.Channels;
using Ananke.Abstractions.Channels;
using Ananke.Abstractions.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Ananke.MQTT.Tests;

public enum PongAction { Pong }

[TestFixture]
[NonParallelizable]
public class ServiceCollectionExtensionsTests
{
    private InProcessBroker _broker = null!;

    [SetUp]
    public async Task SetUp() => _broker = await InProcessBroker.StartNewAsync();

    [TearDown]
    public async Task TearDown() => await _broker.DisposeAsync();

    [Test]
    public void AddMqtt_TwoMessageTypes_EachKeepsItsOwnOptions()
    {
        using var provider = TwoRegistrations().BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<ChannelConfig>>();

        var ping = options.Get(MqttChannelOptions.NameFor<PingMessage, PingAction>());
        var pong = options.Get(MqttChannelOptions.NameFor<PingMessage, PongAction>());

        ping.Namespace.ShouldBe("ping");
        ping.QualityOfService.ShouldBe(ChannelQualityOfService.AtLeastOnce);
        pong.Namespace.ShouldBe("pong");
        pong.QualityOfService.ShouldBe(ChannelQualityOfService.ExactlyOnce);
        options.CurrentValue.Namespace.ShouldBe(new ChannelConfig().Namespace, "the unnamed options are left as they are");
    }

    [Test]
    public async Task AddMqtt_TwoMessageTypes_EachReaderGetsOnlyItsOwnTopic()
    {
        await using var provider = TwoRegistrations().BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<ChannelConfig>>();
        var pingConfig = options.Get(MqttChannelOptions.NameFor<PingMessage, PingAction>());
        var pongConfig = options.Get(MqttChannelOptions.NameFor<PingMessage, PongAction>());
        var pingWorker = new Worker<PingAction>();
        var pongWorker = new Worker<PongAction>();

        var pingReader = provider.GetRequiredService<IChannelReader<PingMessage, PingAction>>();
        var pongReader = provider.GetRequiredService<IChannelReader<PingMessage, PongAction>>();
        var pingWriter = provider.GetRequiredService<IChannelWriter<PingAction>>();
        var pongWriter = provider.GetRequiredService<IChannelWriter<PongAction>>();
        (await pingReader.ConfigureAsync(pingConfig, pingWorker)).ShouldBeTrue();
        (await pongReader.ConfigureAsync(pongConfig, pongWorker)).ShouldBeTrue();
        (await pingWriter.ConfigureAsync(pingConfig)).ShouldBeTrue();
        (await pongWriter.ConfigureAsync(pongConfig)).ShouldBeTrue();

        (await pingWriter.SendAsync(new PingMessage { Text = "ping" }, PingAction.Ping)).Success.ShouldBeTrue();
        (await pongWriter.SendAsync(new PingMessage { Text = "pong" }, PongAction.Pong)).Success.ShouldBeTrue();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        (await pingWorker.Received.Reader.ReadAsync(cts.Token)).ShouldBe("ping");
        (await pongWorker.Received.Reader.ReadAsync(cts.Token)).ShouldBe("pong");
        await Task.Delay(200);
        pingWorker.Received.Reader.Count.ShouldBe(0);
        pongWorker.Received.Reader.Count.ShouldBe(0);
        _broker.Subscriptions.ShouldBe(
            [("ping/pingaction/+", MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce),
             ("pong/pongaction/+", MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce)]);
    }

    private ServiceCollection TwoRegistrations()
    {
        var services = new ServiceCollection();
        services.AddMqtt<PingMessage, PingAction>(c =>
        {
            c.Host = "127.0.0.1";
            c.Port = _broker.Port;
            c.Namespace = "ping";
            c.QualityOfService = ChannelQualityOfService.AtLeastOnce;
        });
        services.AddMqtt<PingMessage, PongAction>("127.0.0.1", _broker.Port, "pong");
        return services;
    }

    private sealed class Worker<A> : IBackgroundWorker<PingMessage, A> where A : Enum
    {
        public Channel<string> Received { get; } = Channel.CreateUnbounded<string>();

        public async Task HandleAsync(PingMessage? item, A action, CancellationToken token) =>
            await Received.Writer.WriteAsync(item?.Text ?? string.Empty, token).ConfigureAwait(false);
    }
}
