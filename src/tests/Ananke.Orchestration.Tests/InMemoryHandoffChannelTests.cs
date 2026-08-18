using Ananke.Orchestration.Jobs;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class InMemoryHandoffChannelTests
{
    [Test]
    public async Task SendAsync_RegisteredHandlerExceedsTimeout_ThrowsOperationCanceledException()
    {
        await using var channel = new InMemoryHandoffChannel();
        channel.RegisterHandler<string, string>("slow-topic", async (msg, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return msg;
        });

        await Should.ThrowAsync<OperationCanceledException>(
            () => channel.SendAsync<string, string>(
                "slow-topic", "corr-1", "hello", TimeSpan.FromMilliseconds(50)));
    }

    [Test]
    public async Task SendAsync_RegisteredHandlerCompletesBeforeTimeout_ReturnsResult()
    {
        await using var channel = new InMemoryHandoffChannel();
        channel.RegisterHandler<string, string>("fast-topic", msg => msg.ToUpperInvariant());

        var result = await channel.SendAsync<string, string>(
            "fast-topic", "corr-1", "hello", TimeSpan.FromSeconds(5));

        result.ShouldBe("HELLO");
    }

    [Test]
    public async Task SendAsync_RegisteredHandlerHonoursCallerCancellation_ThrowsOperationCanceledException()
    {
        await using var channel = new InMemoryHandoffChannel();
        channel.RegisterHandler<string, string>("slow-topic", async (msg, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return msg;
        });

        using var cts = new CancellationTokenSource();
        var sendTask = channel.SendAsync<string, string>(
            "slow-topic", "corr-1", "hello", TimeSpan.FromSeconds(30), cts.Token);
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => sendTask);
    }
}
