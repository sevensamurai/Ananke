using Ananke.Abstractions.Channels;
using Ananke.StateMachine.Worker;
using Shouldly;

namespace Ananke.StateMachine.Tests;

[TestFixture]
public class ProducerConsumerTests
{
    [Test]
    public async Task Queue_ItemsProcessedByWorker()
    {
        var processed = new List<int>();
        var worker = new TestWorker<int>(processed);
        var pc = new ProducerConsumer<int>(worker);
        using var cts = new CancellationTokenSource();

        pc.Start(cts.Token);
        pc.Queue(1);
        pc.Queue(2);
        pc.Queue(3);

        // Give the background task time to process
        await Task.Delay(200);
        cts.Cancel();

        processed.ShouldBe([1, 2, 3]);
    }

    [Test]
    public async Task QueueAsync_ItemsProcessedByWorker()
    {
        var processed = new List<string>();
        var worker = new TestWorker<string>(processed);
        var pc = new ProducerConsumer<string>(worker);
        using var cts = new CancellationTokenSource();

        pc.Start(cts.Token);
        await pc.QueueAsync("a");
        await pc.QueueAsync("b");

        await Task.Delay(200);
        cts.Cancel();

        processed.ShouldBe(["a", "b"]);
    }

    [Test]
    public void Start_SetsIsRunning()
    {
        var pc = new ProducerConsumer<int>(null);
        using var cts = new CancellationTokenSource();

        pc.IsRunning.ShouldBeFalse();
        pc.Start(cts.Token);
        pc.IsRunning.ShouldBeTrue();

        cts.Cancel();
    }

    [Test]
    public void Start_CalledTwice_DoesNotThrow()
    {
        var pc = new ProducerConsumer<int>(null);
        using var cts = new CancellationTokenSource();

        pc.Start(cts.Token);
        pc.Start(cts.Token); // second call should be no-op

        pc.IsRunning.ShouldBeTrue();
        cts.Cancel();
    }

    [Test]
    public void Stop_SetsIsRunningFalse()
    {
        var pc = new ProducerConsumer<int>(null);
        using var cts = new CancellationTokenSource();

        pc.Start(cts.Token);
        pc.Stop();

        pc.IsRunning.ShouldBeFalse();
    }

    [Test]
    public void Stop_WhenNotRunning_DoesNotThrow()
    {
        var pc = new ProducerConsumer<int>(null);

        Should.NotThrow(() => pc.Stop());
    }

    [Test]
    public async Task MarkAsCompleted_ChannelCompletes()
    {
        var pc = new ProducerConsumer<int>(null);
        using var cts = new CancellationTokenSource();

        pc.Start(cts.Token);
        pc.MarkAsCompleted();

        // IsDone should complete when the channel is done
        var task = pc.IsDone();
        await Task.WhenAny(task, Task.Delay(1000));
        task.IsCompleted.ShouldBeTrue();
    }

    [Test]
    public async Task NullWorker_ItemsSkippedGracefully()
    {
        var pc = new ProducerConsumer<int>(null);
        using var cts = new CancellationTokenSource();

        pc.Start(cts.Token);
        pc.Queue(1);
        pc.Queue(2);

        await Task.Delay(100);
        cts.Cancel();

        // Should not throw — null worker is handled
    }

    // ── Test helpers ─────────────────────────────────────────────────

    private sealed class TestWorker<T>(List<T> processed) : IBackgroundWorker<T>
    {
        public Task HandleAsync(T? item, CancellationToken ct)
        {
            if (item is not null)
                processed.Add(item);
            return Task.CompletedTask;
        }
    }
}
