using Ananke.Abstractions.Config;
using Shouldly;

namespace Ananke.MQTT.Tests;

/// <summary>
/// <see cref="MqttHandoffChannel"/> without a broker: configure failures, the send and complete guards,
/// disposal and config validation. The broker tests are in <see cref="MqttHandoffChannelBrokerTests"/>.
/// </summary>
[TestFixture]
public class MqttHandoffChannelTests
{
    private static readonly ChannelConfig BadConfig = new()
    {
        Host = "127.0.0.1",
        Port = 19883,           // nothing listening here
        Namespace = "test"
    };

    // ── configure failures ────────────────────────────────────────────────────

    [Test]
    public async Task ConfigureAsync_WhenBrokerUnreachable_ReturnsFalse()
    {
        await using var channel = new MqttHandoffChannel();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var connected = await channel.ConfigureAsync(BadConfig, cts.Token);

        connected.ShouldBeFalse("ConfigureAsync must return false when the broker cannot be reached.");
    }

    [Test]
    public async Task IsConnected_WhenConfigureFailed_IsFalse()
    {
        await using var channel = new MqttHandoffChannel();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await channel.ConfigureAsync(BadConfig, cts.Token);

        channel.IsConnected.ShouldBeFalse(
            "IsConnected must be false after a failed ConfigureAsync.");
    }

    [Test]
    public void IsConnected_WhenNeverConfigured_IsFalse()
    {
        var channel = new MqttHandoffChannel();

        channel.IsConnected.ShouldBeFalse(
            "IsConnected must be false before ConfigureAsync is called.");
    }

    // ── send / complete guards ────────────────────────────────────────────────

    [Test]
    public async Task SendAsync_WhenNotConnected_ThrowsInvalidOperationException()
    {
        await using var channel = new MqttHandoffChannel();

        await Should.ThrowAsync<InvalidOperationException>(
            () => channel.SendAsync<object, object>(
                "topic", "corr-1", new { }, TimeSpan.FromSeconds(1)));
    }

    [Test]
    public async Task CompleteAsync_WhenNotConnected_ThrowsInvalidOperationException()
    {
        await using var channel = new MqttHandoffChannel();

        await Should.ThrowAsync<InvalidOperationException>(
            () => channel.CompleteAsync<object>("topic", "corr-1", new { }));
    }

    // ── disposal guards ───────────────────────────────────────────────────────

    [Test]
    public async Task SendAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var channel = new MqttHandoffChannel();
        await channel.DisposeAsync();

        await Should.ThrowAsync<ObjectDisposedException>(
            () => channel.SendAsync<object, object>(
                "topic", "corr-1", new { }, TimeSpan.FromSeconds(1)));
    }

    [Test]
    public async Task CompleteAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var channel = new MqttHandoffChannel();
        await channel.DisposeAsync();

        await Should.ThrowAsync<ObjectDisposedException>(
            () => channel.CompleteAsync<object>("topic", "corr-1", new { }));
    }

    // ── config validation ─────────────────────────────────────────────────────

    [Test]
    public async Task ConfigureAsync_WhenConfigIsNull_ThrowsArgumentNullException()
    {
        await using var channel = new MqttHandoffChannel();

        await Should.ThrowAsync<ArgumentNullException>(
            () => channel.ConfigureAsync(null!));
    }

    [Test]
    public async Task ConfigureAsync_WhenHostIsEmpty_ThrowsArgumentException()
    {
        await using var channel = new MqttHandoffChannel();

        await Should.ThrowAsync<ArgumentException>(
            () => channel.ConfigureAsync(new ChannelConfig { Host = "", Namespace = "test" }));
    }

    [Test]
    public async Task ConfigureAsync_WhenNamespaceIsEmpty_ThrowsArgumentException()
    {
        await using var channel = new MqttHandoffChannel();

        await Should.ThrowAsync<ArgumentException>(
            () => channel.ConfigureAsync(new ChannelConfig { Host = "localhost", Namespace = "" }));
    }
}
