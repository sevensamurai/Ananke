using System.Diagnostics;
using System.Threading.Channels;
using Ananke.Abstractions.Channels;
using Ananke.Abstractions.Config;
using MQTTnet;
using MQTTnet.Protocol;
using Shouldly;

namespace Ananke.MQTT.Tests;

public enum PingAction { Ping }

public sealed class PingMessage
{
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// <see cref="MqttChannelReader{M, A}"/> and <see cref="MqttChannelWriter{A}"/> against a broker in the
/// test process that is stopped and started again: a broker outage at a short reconnect delay.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MqttChannelBrokerTests
{
    internal static readonly MqttReconnectDelay ShortDelay = new(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200));
    private static readonly TimeSpan Outage = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private InProcessBroker _broker = null!;

    [SetUp]
    public async Task SetUp() => _broker = await InProcessBroker.StartNewAsync();

    [TearDown]
    public async Task TearDown() => await _broker.DisposeAsync();

    [Test]
    public async Task ConfigureAsync_BrokerOutage_ReaderAndWriterReconnectWithinTheCappedDelay()
    {
        var readerLog = new ListLogger<MqttChannelReader<PingMessage, PingAction>>();
        var writerLog = new ListLogger<MqttChannelWriter<PingAction>>();
        var worker = new RecordingWorker();
        await using var reader = new MqttChannelReader<PingMessage, PingAction>(logger: readerLog) { ReconnectDelay = ShortDelay };
        await using var writer = new MqttChannelWriter<PingAction>(writerLog) { ReconnectDelay = ShortDelay };

        (await reader.ConfigureAsync(Config(), worker)).ShouldBeTrue();
        (await writer.ConfigureAsync(Config())).ShouldBeTrue();
        await RoundTripAsync(writer, worker, "before");

        await _broker.StopAsync();
        await WaitUntilAsync(() => !writer.IsConnected && !reader.IsConnected);
        var readerLinesBefore = readerLog.Messages.Count;
        var writerLinesBefore = writerLog.Messages.Count;
        await Task.Delay(Outage);
        var readerAttempts = readerLog.Count("reconnect attempt");
        var writerAttempts = writerLog.Count("reconnect attempt");
        var readerLines = readerLog.Messages.Count - readerLinesBefore;
        var writerLines = writerLog.Messages.Count - writerLinesBefore;

        var back = Stopwatch.StartNew();
        await _broker.StartAsync();
        await WaitUntilAsync(() => reader.ConnectCount == 2 && writer.ConnectCount == 2);
        back.Stop();

        writer.IsConnected.ShouldBeTrue();
        reader.IsConnected.ShouldBeTrue();
        back.Elapsed.ShouldBeLessThan(ShortDelay.Max + TimeSpan.FromSeconds(1),
            "both clients connect within the capped delay of the broker coming back");
        await RoundTripAsync(writer, worker, "after");

        // One attempt per delay (150-200 ms) over the outage, per client; the floor is halved for a loaded machine.
        var fewest = (int)(Outage / ShortDelay.Max) / 2;
        var most = (int)(Outage / (ShortDelay.Max * 0.75)) + 2;
        readerAttempts.ShouldBeInRange(fewest, most);
        writerAttempts.ShouldBeInRange(fewest, most);
        readerLines.ShouldBeLessThanOrEqualTo(most + 2, "one log line per attempt, plus the disconnect");
        writerLines.ShouldBeLessThanOrEqualTo(most + 2, "one log line per attempt, plus the disconnect");
    }

    [Test]
    public async Task ConfigureAsync_EveryConnect_SubscribesAtExactlyOnce()
    {
        var worker = new RecordingWorker();
        await using var reader = new MqttChannelReader<PingMessage, PingAction> { ReconnectDelay = ShortDelay };

        (await reader.ConfigureAsync(Config(), worker)).ShouldBeTrue();
        await WaitUntilAsync(() => !_broker.Subscriptions.IsEmpty);
        await _broker.StopAsync();
        await _broker.StartAsync();
        await WaitUntilAsync(() => _broker.Subscriptions.Count == 2);

        _broker.Subscriptions.Select(s => s.Topic).ShouldAllBe(t => t == "test/pingaction/+");
        _broker.Subscriptions.Select(s => s.QoS).ShouldAllBe(q => q == MqttQualityOfServiceLevel.ExactlyOnce);
    }

    [Test]
    public async Task IsConnected_ReconnectedButNotSubscribedAgain_IsFalseUntilSubscribed()
    {
        var worker = new RecordingWorker();
        await using var reader = new MqttChannelReader<PingMessage, PingAction> { ReconnectDelay = ShortDelay };
        (await reader.ConfigureAsync(Config(), worker)).ShouldBeTrue();
        reader.IsConnected.ShouldBeTrue();

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _broker.SubscriptionsHeldUntil = release.Task;
            await _broker.StopAsync();
            await WaitUntilAsync(() => !reader.IsConnected);
            await _broker.StartAsync();
            await WaitUntilAsync(() => _broker.SubscriptionsReceived >= 2);

            _broker.Connected.ShouldNotBeEmpty();
            reader.IsConnected.ShouldBeFalse("connected, but the subscription is not set up again yet");
        }
        finally
        {
            release.SetResult();
        }

        await WaitUntilAsync(() => reader.ConnectCount == 2);
        reader.IsConnected.ShouldBeTrue();
    }

    [Test]
    public async Task ConfigureAsync_BrokerDownAtStart_ConnectsWhenTheBrokerComes()
    {
        await _broker.StopAsync();
        var worker = new RecordingWorker();
        await using var reader = new MqttChannelReader<PingMessage, PingAction> { ReconnectDelay = ShortDelay };
        await using var writer = new MqttChannelWriter<PingAction> { ReconnectDelay = ShortDelay };

        (await reader.ConfigureAsync(Config(), worker)).ShouldBeFalse();
        (await writer.ConfigureAsync(Config())).ShouldBeFalse();
        writer.IsConnected.ShouldBeFalse();

        await _broker.StartAsync();
        await WaitUntilAsync(() => reader.ConnectCount == 1 && writer.ConnectCount == 1);

        await RoundTripAsync(writer, worker, "late");
    }

    [Test]
    public async Task ConfigureAsync_BrokerRefusesTheSubscription_ReconnectsAndSubscribesAgain()
    {
        _broker.SubscriptionsToRefuse = 1;
        var worker = new RecordingWorker();
        await using var reader = new MqttChannelReader<PingMessage, PingAction> { ReconnectDelay = ShortDelay };
        await using var writer = new MqttChannelWriter<PingAction> { ReconnectDelay = ShortDelay };

        (await reader.ConfigureAsync(Config(), worker)).ShouldBeFalse();
        (await writer.ConfigureAsync(Config())).ShouldBeTrue();
        await WaitUntilAsync(() => reader.ConnectCount == 1);

        _broker.Subscriptions.Count.ShouldBe(1);
        await RoundTripAsync(writer, worker, "granted");
    }

    [Test]
    public async Task ConfigureAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var reader = new MqttChannelReader<PingMessage, PingAction>();
        var writer = new MqttChannelWriter<PingAction>();
        await reader.DisposeAsync();
        await writer.DisposeAsync();

        await Should.ThrowAsync<ObjectDisposedException>(() => reader.ConfigureAsync(Config(), new RecordingWorker()));
        await Should.ThrowAsync<ObjectDisposedException>(() => writer.ConfigureAsync(Config()));
    }

    [Test]
    public async Task DisposeAsync_DuringAnOutage_StopsReconnecting()
    {
        var writerLog = new ListLogger<MqttChannelWriter<PingAction>>();
        var writer = new MqttChannelWriter<PingAction>(writerLog) { ReconnectDelay = ShortDelay };
        (await writer.ConfigureAsync(Config())).ShouldBeTrue();

        await _broker.StopAsync();
        await WaitUntilAsync(() => writerLog.Count("reconnect attempt") >= 2);
        await writer.DisposeAsync().AsTask().WaitAsync(Timeout);
        var attempts = writerLog.Count("reconnect attempt");
        await Task.Delay(ShortDelay.Max * 3);

        writerLog.Count("reconnect attempt").ShouldBe(attempts);
    }

    [Test]
    public async Task ClearAsync_DuringAnOutage_StopsReconnecting()
    {
        var readerLog = new ListLogger<MqttChannelReader<PingMessage, PingAction>>();
        await using var reader = new MqttChannelReader<PingMessage, PingAction>(logger: readerLog) { ReconnectDelay = ShortDelay };
        (await reader.ConfigureAsync(Config(), new RecordingWorker())).ShouldBeTrue();

        await _broker.StopAsync();
        await WaitUntilAsync(() => readerLog.Count("reconnect attempt") >= 2);
        await reader.ClearAsync().WaitAsync(Timeout);
        var attempts = readerLog.Count("reconnect attempt");
        await Task.Delay(ShortDelay.Max * 3);

        readerLog.Count("reconnect attempt").ShouldBe(attempts);
    }

    [Test]
    public async Task Receive_PayloadThatCannotBeDeserialized_IsLoggedAndTheNextMessageIsDelivered()
    {
        var readerLog = new ListLogger<MqttChannelReader<PingMessage, PingAction>>();
        var worker = new RecordingWorker();
        await using var reader = new MqttChannelReader<PingMessage, PingAction>(logger: readerLog) { ReconnectDelay = ShortDelay };
        await using var writer = new MqttChannelWriter<PingAction> { ReconnectDelay = ShortDelay };
        (await reader.ConfigureAsync(Config(), worker)).ShouldBeTrue();
        (await writer.ConfigureAsync(Config())).ShouldBeTrue();

        using var raw = new MqttClientFactory().CreateMqttClient();
        await raw.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1", _broker.Port).Build());
        // 0xC1 is the one byte MessagePack never uses.
        await raw.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic("test/pingaction/ping")
            .WithPayload(new byte[] { 0xC1, 0xC1 })
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
            .Build());
        await raw.DisconnectAsync();

        await RoundTripAsync(writer, worker, "next");
        readerLog.Count("could not be deserialized").ShouldBe(1);
        reader.IsConnected.ShouldBeTrue();
    }

    [Test]
    public async Task ClearAsync_WorkerBlockedAndQueueFull_Returns()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new BlockingWorker(release.Task);
        await using var reader = new MqttChannelReader<PingMessage, PingAction>(queueCapacity: 1) { ReconnectDelay = ShortDelay };
        await using var writer = new MqttChannelWriter<PingAction> { ReconnectDelay = ShortDelay };
        (await reader.ConfigureAsync(Config(), worker)).ShouldBeTrue();
        (await writer.ConfigureAsync(Config())).ShouldBeTrue();

        // One in the worker, one in the queue, the third waits in the enqueue.
        for (var i = 0; i < 3; i++)
            (await writer.SendAsync(new PingMessage { Text = $"m{i}" }, PingAction.Ping)).Success.ShouldBeTrue();
        await WaitUntilAsync(() => worker.Started == 1 && _broker.Publishes.Count == 3);
        await Task.Delay(300);

        await reader.ClearAsync().WaitAsync(TimeSpan.FromSeconds(3));
        release.SetResult();
    }

    [Test]
    public async Task ConfigureAsync_ClientIdSessionAndQualityOfService_ConnectSubscribeAndPublishWithThem()
    {
        var config = Config();
        config.ClientId = "seren-api";
        config.CleanSession = false;
        config.QualityOfService = ChannelQualityOfService.AtLeastOnce;
        var worker = new RecordingWorker();
        await using var reader = new MqttChannelReader<PingMessage, PingAction> { ReconnectDelay = ShortDelay };
        await using var writer = new MqttChannelWriter<PingAction> { ReconnectDelay = ShortDelay };

        (await reader.ConfigureAsync(config, worker)).ShouldBeTrue();
        (await writer.ConfigureAsync(config)).ShouldBeTrue();
        await RoundTripAsync(writer, worker, "persistent");
        await Task.Delay(ShortDelay.Max * 3);

        _broker.Connections.Count.ShouldBe(2);
        _broker.Connections.ShouldAllBe(c => !c.CleanSession && c.ClientId.StartsWith("seren-api-") && c.ClientId.Length == 18);
        _broker.Connected.Keys.Order().ShouldBe(_broker.Connections.Select(c => c.ClientId).Distinct().Order());
        _broker.Subscriptions.ShouldBe([("test/pingaction/+", MqttQualityOfServiceLevel.AtLeastOnce)]);
        _broker.Publishes.ShouldBe([("test/pingaction/ping", MqttQualityOfServiceLevel.AtLeastOnce)]);
    }

    [Test]
    public async Task ConfigureAsync_ClientIdInUseInTheProcess_ThrowsNamingIt()
    {
        var config = Config();
        config.ClientId = "in-use";
        await using var first = new MqttChannelReader<PingMessage, PingAction> { ReconnectDelay = ShortDelay };
        await using var second = new MqttChannelReader<PingMessage, PingAction> { ReconnectDelay = ShortDelay };
        await using var writer = new MqttChannelWriter<PingAction> { ReconnectDelay = ShortDelay };
        await using var secondWriter = new MqttChannelWriter<PingAction> { ReconnectDelay = ShortDelay };
        (await first.ConfigureAsync(config, new RecordingWorker())).ShouldBeTrue();
        (await writer.ConfigureAsync(config)).ShouldBeTrue();
        var readerId = MqttConnection.OptionsFor(config, ["reader", typeof(PingAction).ToString(), typeof(PingMessage).ToString()]).ClientId;
        var writerId = MqttConnection.OptionsFor(config, ["writer", typeof(PingAction).ToString()]).ClientId;

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => second.ConfigureAsync(config, new RecordingWorker()));
        var writerEx = await Should.ThrowAsync<InvalidOperationException>(() => secondWriter.ConfigureAsync(config));

        ex.Message.ShouldContain($"'{readerId}'");
        writerEx.Message.ShouldContain($"'{writerId}'");
        _broker.Connections.Select(c => c.ClientId).ShouldBe([readerId, writerId]);
        second.IsConnected.ShouldBeFalse();

        await first.DisposeAsync();
        (await second.ConfigureAsync(config, new RecordingWorker())).ShouldBeTrue("a disposed channel's id is free again");
    }

    [Test]
    public async Task ConfigureAsync_ReadersOfOneActionTypeWithDifferentMessageTypes_BothConnectWithTheirOwnIds()
    {
        var config = Config();
        config.ClientId = "two-readers";
        await using var ping = new MqttChannelReader<PingMessage, PingAction> { ReconnectDelay = ShortDelay };
        await using var other = new MqttChannelReader<OtherMessage, PingAction> { ReconnectDelay = ShortDelay };

        (await ping.ConfigureAsync(config, new RecordingWorker())).ShouldBeTrue();
        (await other.ConfigureAsync(config, new IgnoringWorker<OtherMessage>())).ShouldBeTrue();
        await Task.Delay(ShortDelay.Max * 3);

        ping.IsConnected.ShouldBeTrue();
        other.IsConnected.ShouldBeTrue();
        _broker.Connected.Keys.Count.ShouldBe(2);
    }

    [Test]
    public async Task DisposeAsync_KeptSession_MessageSentWhileGoneIsDeliveredOnTheNextConnect()
    {
        var config = Config();
        config.ClientId = "kept";
        config.CleanSession = false;
        await using var writer = new MqttChannelWriter<PingAction> { ReconnectDelay = ShortDelay };
        (await writer.ConfigureAsync(Config())).ShouldBeTrue();

        var first = new MqttChannelReader<PingMessage, PingAction> { ReconnectDelay = ShortDelay };
        (await first.ConfigureAsync(config, new RecordingWorker())).ShouldBeTrue();
        await first.DisposeAsync();
        var sent = await writer.SendAsync(new PingMessage { Text = "while-gone" }, PingAction.Ping);
        sent.Success.ShouldBeTrue(sent.ErrorMessage);

        var worker = new RecordingWorker();
        await using var second = new MqttChannelReader<PingMessage, PingAction> { ReconnectDelay = ShortDelay };
        (await second.ConfigureAsync(config, worker)).ShouldBeTrue();
        using var cts = new CancellationTokenSource(Timeout);
        (await worker.Received.Reader.ReadAsync(cts.Token)).ShouldBe("while-gone");
    }

    [Test]
    public async Task SendAsync_CancelledToken_ThrowsOperationCanceled()
    {
        await using var writer = new MqttChannelWriter<PingAction> { ReconnectDelay = ShortDelay };
        (await writer.ConfigureAsync(Config())).ShouldBeTrue();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => writer.SendAsync(new PingMessage { Text = "x" }, PingAction.Ping, cts.Token));
    }

    [Test]
    public async Task SendAsync_NotConnected_ReturnsFailed()
    {
        await using var writer = new MqttChannelWriter<PingAction> { ReconnectDelay = ShortDelay };

        var sent = await writer.SendAsync(new PingMessage { Text = "x" }, PingAction.Ping);

        sent.Success.ShouldBeFalse();
    }

    [Test]
    public async Task ConfigureAsync_DefaultConfig_ConnectsWithACleanSessionAtExactlyOnce()
    {
        var worker = new RecordingWorker();
        await using var reader = new MqttChannelReader<PingMessage, PingAction> { ReconnectDelay = ShortDelay };
        await using var writer = new MqttChannelWriter<PingAction> { ReconnectDelay = ShortDelay };

        (await reader.ConfigureAsync(Config(), worker)).ShouldBeTrue();
        (await writer.ConfigureAsync(Config())).ShouldBeTrue();
        await RoundTripAsync(writer, worker, "default");

        _broker.Connections.Select(c => c.CleanSession).ShouldBe([true, true]);
        _broker.Publishes.Select(p => p.QoS).ShouldBe([MqttQualityOfServiceLevel.ExactlyOnce]);
    }

    [Test]
    public async Task ConfigureAsync_PersistentSessionWithoutClientId_Throws()
    {
        var config = Config();
        config.CleanSession = false;
        await using var reader = new MqttChannelReader<PingMessage, PingAction>();
        await using var writer = new MqttChannelWriter<PingAction>();

        await Should.ThrowAsync<ArgumentException>(() => reader.ConfigureAsync(config, new RecordingWorker()));
        await Should.ThrowAsync<ArgumentException>(() => writer.ConfigureAsync(config));
    }

    /// <summary>
    /// A 45 s broker outage at the default reconnect delay, then a round trip.
    /// Takes over a minute, so it runs only when selected.
    /// </summary>
    [Test]
    [Explicit("Runs for over a minute: a 45 s outage at the default reconnect delay.")]
    public async Task ConfigureAsync_45SecondOutageAtTheDefaultDelay_ReaderAndWriterReconnectWithinTheCap()
    {
        var readerLog = new ListLogger<MqttChannelReader<PingMessage, PingAction>>();
        var writerLog = new ListLogger<MqttChannelWriter<PingAction>>();
        var worker = new RecordingWorker();
        await using var reader = new MqttChannelReader<PingMessage, PingAction>(logger: readerLog);
        await using var writer = new MqttChannelWriter<PingAction>(writerLog);

        (await reader.ConfigureAsync(Config(), worker)).ShouldBeTrue();
        (await writer.ConfigureAsync(Config())).ShouldBeTrue();
        await RoundTripAsync(writer, worker, "before");

        await _broker.StopAsync();
        await Task.Delay(TimeSpan.FromSeconds(45));
        var back = Stopwatch.StartNew();
        await _broker.StartAsync();
        await WaitUntilAsync(() => reader.ConnectCount == 2 && writer.ConnectCount == 2,
            MqttReconnectDelay.Default.Max + TimeSpan.FromSeconds(5));
        back.Stop();

        TestContext.Out.WriteLine($"Back after {back.Elapsed.TotalSeconds:F1} s; " +
            $"reader {readerLog.Messages.Count} lines, {readerLog.Count("reconnect attempt")} attempts; " +
            $"writer {writerLog.Messages.Count} lines, {writerLog.Count("reconnect attempt")} attempts");
        back.Elapsed.ShouldBeLessThan(MqttReconnectDelay.Default.Max + TimeSpan.FromSeconds(1));
        await RoundTripAsync(writer, worker, "after");
        readerLog.Count("reconnect attempt").ShouldBeLessThanOrEqualTo(10);
        writerLog.Count("reconnect attempt").ShouldBeLessThanOrEqualTo(10);
    }

    private ChannelConfig Config() => new() { Host = "127.0.0.1", Port = _broker.Port, Namespace = "test" };

    internal static async Task RoundTripAsync(MqttChannelWriter<PingAction> writer, RecordingWorker worker, string text)
    {
        var sent = await writer.SendAsync(new PingMessage { Text = text }, PingAction.Ping).ConfigureAwait(false);
        sent.Success.ShouldBeTrue(sent.ErrorMessage);

        using var cts = new CancellationTokenSource(Timeout);
        var received = await worker.Received.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
        received.ShouldBe(text);
    }

    internal static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var limit = Stopwatch.StartNew();
        while (!condition())
        {
            if (limit.Elapsed > (timeout ?? Timeout))
                Assert.Fail($"Condition not met within {(timeout ?? Timeout).TotalSeconds} s");
            await Task.Delay(20).ConfigureAwait(false);
        }
    }

    internal sealed class RecordingWorker : IBackgroundWorker<PingMessage, PingAction>
    {
        public Channel<string> Received { get; } = Channel.CreateUnbounded<string>();

        public async Task HandleAsync(PingMessage? item, PingAction action, CancellationToken token) =>
            await Received.Writer.WriteAsync(item?.Text ?? string.Empty, token).ConfigureAwait(false);
    }

    private sealed class IgnoringWorker<M> : IBackgroundWorker<M, PingAction> where M : class
    {
        public Task HandleAsync(M? item, PingAction action, CancellationToken token) => Task.CompletedTask;
    }

    private sealed class BlockingWorker(Task release) : IBackgroundWorker<PingMessage, PingAction>
    {
        private int _started;

        public int Started => Volatile.Read(ref _started);

        public async Task HandleAsync(PingMessage? item, PingAction action, CancellationToken token)
        {
            Interlocked.Increment(ref _started);
            await release.WaitAsync(token).ConfigureAwait(false);
        }
    }
}
