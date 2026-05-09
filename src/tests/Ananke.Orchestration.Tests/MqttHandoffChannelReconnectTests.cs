using Ananke.Abstractions.Config;
using Ananke.MQTT;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Phase 6.4 — <see cref="MqttHandoffChannel"/> reconnect-bound tests.
/// <para>
/// These tests exercise the observable public contract of the channel without
/// requiring a live MQTT broker. Reconnect-loop behaviour (bounded at 10 attempts
/// with exponential back-off) is verified via:
/// <list type="bullet">
///   <item>The <see cref="MqttHandoffChannel.IsConnected"/> property after a failed configure.</item>
///   <item>The <c>SendAsync</c> guard that throws when the channel is not connected.</item>
///   <item>The <c>CompleteAsync</c> guard under the same condition.</item>
///   <item>A constant-bound assertion: the declared maximum must remain 10.</item>
/// </list>
/// End-to-end reconnect-loop coverage (driving 10 consecutive failures) is an
/// integration test that requires a controllable broker and lives outside CI.
/// </para>
/// </summary>
[TestFixture]
public class MqttHandoffChannelReconnectTests
{
    private static readonly ChannelConfig BadConfig = new()
    {
        Host      = "127.0.0.1",
        Port      = 19883,           // nothing listening here
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

    // ── reconnect-bound constant ──────────────────────────────────────────────

    [Test]
    public void MaxReconnectAttempts_IsExactlyTen()
    {
        // Verifies the bounded reconnect loop constant hasn't been silently removed or changed to unbounded (0 / int.MaxValue).
        // Because MaxReconnectAttempts is private const, we read it via reflection.
        var field = typeof(MqttHandoffChannel).GetField(
            "MaxReconnectAttempts",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        field.ShouldNotBeNull("MaxReconnectAttempts constant must exist on MqttHandoffChannel.");
        var value = (int)field!.GetValue(null)!;
        value.ShouldBe(10,
            "MqttHandoffChannel must limit reconnect attempts to exactly 10.");
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
