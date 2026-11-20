using Ananke.Abstractions.Config;
using MQTTnet.Protocol;
using Shouldly;
using static Ananke.MQTT.Tests.MqttChannelBrokerTests;

namespace Ananke.MQTT.Tests;

public sealed class HandoffRequest
{
    public string Text { get; set; } = string.Empty;
}

public sealed class HandoffReply
{
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// <see cref="MqttHandoffChannel"/> requester and responder against a broker in the test process that
/// is restarted.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MqttHandoffChannelBrokerTests
{
    private static readonly MqttReconnectDelay ShortDelay = new(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200));
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(5);

    private InProcessBroker _broker = null!;

    [SetUp]
    public async Task SetUp() => _broker = await InProcessBroker.StartNewAsync();

    [TearDown]
    public async Task TearDown() => await _broker.DisposeAsync();

    [Test]
    public async Task SendAsync_AfterABrokerRestart_GetsItsReply()
    {
        await using var requester = new MqttHandoffChannel { ReconnectDelay = ShortDelay };
        await using var responder = new MqttHandoffChannel { ReconnectDelay = ShortDelay };
        (await requester.ConfigureAsync(Config())).ShouldBeTrue();
        (await responder.ConfigureAsync(Config())).ShouldBeTrue();
        await responder.SubscribeAsync<HandoffRequest, HandoffReply>("specialist",
            (request, _) => Task.FromResult(new HandoffReply { Text = $"re:{request.Text}" }));

        (await SendAsync(requester, "one")).Text.ShouldBe("re:one");

        await _broker.StopAsync();
        await _broker.StartAsync();
        await WaitUntilAsync(() => requester.ConnectCount == 2 && responder.ConnectCount == 2);

        (await SendAsync(requester, "two")).Text.ShouldBe("re:two");
    }

    [Test]
    public async Task SendAsync_BrokerRestartsWhileTheRequestIsHandled_GetsItsReply()
    {
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var requester = new MqttHandoffChannel { ReconnectDelay = ShortDelay };
        await using var responder = new MqttHandoffChannel { ReconnectDelay = ShortDelay };
        (await requester.ConfigureAsync(Config())).ShouldBeTrue();
        (await responder.ConfigureAsync(Config())).ShouldBeTrue();
        await responder.SubscribeAsync<HandoffRequest, HandoffReply>("specialist", async (request, ct) =>
        {
            received.TrySetResult();
            await release.Task.WaitAsync(ct);
            return new HandoffReply { Text = $"re:{request.Text}" };
        });

        var reply = requester.SendAsync<HandoffRequest, HandoffReply>(
            "specialist", "corr-pending", new HandoffRequest { Text = "slow" }, TimeSpan.FromSeconds(15));
        await received.Task.WaitAsync(ReplyTimeout);
        // The broker forwards a QoS 2 request before the requester's publish handshake completes; a
        // broker stopped inside that handshake fails the publish, not the reply.
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        await _broker.StopAsync();
        await _broker.StartAsync();
        await WaitUntilAsync(() => requester.ConnectCount == 2 && responder.ConnectCount == 2);
        release.SetResult();

        (await reply.WaitAsync(ReplyTimeout)).Text.ShouldBe("re:slow");
    }

    [Test]
    public async Task ConfigureAsync_ClientIdAndQualityOfService_SubscribesAndPublishesWithThem()
    {
        var requesterConfig = Config();
        requesterConfig.ClientId = "orchestrator";
        requesterConfig.QualityOfService = ChannelQualityOfService.AtLeastOnce;
        var responderConfig = Config();
        responderConfig.ClientId = "specialist";
        responderConfig.QualityOfService = ChannelQualityOfService.AtLeastOnce;
        await using var requester = new MqttHandoffChannel { ReconnectDelay = ShortDelay };
        await using var responder = new MqttHandoffChannel { ReconnectDelay = ShortDelay };
        (await requester.ConfigureAsync(requesterConfig)).ShouldBeTrue();
        (await responder.ConfigureAsync(responderConfig)).ShouldBeTrue();
        await responder.SubscribeAsync<HandoffRequest, HandoffReply>("specialist",
            (request, _) => Task.FromResult(new HandoffReply { Text = $"re:{request.Text}" }));

        (await SendAsync(requester, "one")).Text.ShouldBe("re:one");
        await _broker.StopAsync();
        await _broker.StartAsync();
        await WaitUntilAsync(() => responder.ConnectCount == 2);

        string[] ids = [MqttConnection.OptionsFor(requesterConfig, ["handoff"]).ClientId, MqttConnection.OptionsFor(responderConfig, ["handoff"]).ClientId];
        _broker.Connections.Select(c => c.ClientId).Distinct().Order().ShouldBe(ids.Order());
        _broker.Subscriptions.Count.ShouldBe(3, "request filter, reply topic, request filter again after the restart");
        _broker.Subscriptions.Select(s => s.QoS).ShouldAllBe(q => q == MqttQualityOfServiceLevel.AtLeastOnce);
        _broker.Publishes.Select(p => p.QoS).ShouldBe([MqttQualityOfServiceLevel.AtLeastOnce, MqttQualityOfServiceLevel.AtLeastOnce]);
    }

    [Test]
    public async Task ConfigureAsync_ChannelsWithDifferentNames_BothConnect_WithoutAName_TheSecondThrows()
    {
        var config = Config();
        config.ClientId = "named-handoff";
        await using var a = new MqttHandoffChannel { Name = "a", ReconnectDelay = ShortDelay };
        await using var b = new MqttHandoffChannel { Name = "b", ReconnectDelay = ShortDelay };
        await using var unnamed = new MqttHandoffChannel { ReconnectDelay = ShortDelay };
        await using var secondUnnamed = new MqttHandoffChannel { ReconnectDelay = ShortDelay };

        (await a.ConfigureAsync(config)).ShouldBeTrue();
        (await b.ConfigureAsync(config)).ShouldBeTrue();
        (await unnamed.ConfigureAsync(config)).ShouldBeTrue();
        var ex = await Should.ThrowAsync<InvalidOperationException>(() => secondUnnamed.ConfigureAsync(config));

        ex.Message.ShouldContain($"'{MqttConnection.OptionsFor(config, ["handoff"]).ClientId}'");
        _broker.Connections.Select(c => c.ClientId).Distinct().Count().ShouldBe(3);
        a.IsConnected.ShouldBeTrue();
        b.IsConnected.ShouldBeTrue();
    }

    private ChannelConfig Config() => new() { Host = "127.0.0.1", Port = _broker.Port, Namespace = "test" };

    internal static Task<HandoffReply> SendAsync(MqttHandoffChannel requester, string text, TimeSpan? timeout = null)
    {
        var replyTimeout = timeout ?? ReplyTimeout;
        return requester.SendAsync<HandoffRequest, HandoffReply>(
            "specialist", Guid.NewGuid().ToString("N"), new HandoffRequest { Text = text }, replyTimeout)
            .WaitAsync(replyTimeout + TimeSpan.FromSeconds(1));
    }
}
