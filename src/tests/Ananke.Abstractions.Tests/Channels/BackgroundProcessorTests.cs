using System.Collections.Concurrent;
using System.Threading.Channels;
using Ananke.Abstractions.Channels;
using Shouldly;

namespace Ananke.Abstractions.Tests.Channels;

[TestFixture]
public class BackgroundProcessorTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public sealed record Item(int Id);

    public enum Op { Go }

    /// <summary>Records each item; holds every call until <see cref="Release"/> when gated.</summary>
    private sealed class RecordingWorker(bool gated = false, Func<Item?, Exception?>? fail = null)
        : IBackgroundWorker<Item>, IBackgroundWorker<Item, Op>
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;

        public ConcurrentQueue<int> Handled { get; } = new();
        public int MaxConcurrent;

        public void Release() => _gate.TrySetResult();

        public async Task HandleAsync(Item? item, CancellationToken token)
        {
            var active = Interlocked.Increment(ref _active);
            InterlockedMax(ref MaxConcurrent, active);
            try
            {
                if (gated) await _gate.Task.WaitAsync(token);
                else await Task.Yield();
                if (fail?.Invoke(item) is { } ex) throw ex;
                Handled.Enqueue(item!.Id);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public Task HandleAsync(Item? item, Op action, CancellationToken token) => HandleAsync(item, token);

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while ((current = Volatile.Read(ref target)) < value
                   && Interlocked.CompareExchange(ref target, value, current) != current) { }
        }
    }

    [Test]
    public async Task CompleteAsync_ItemsQueued_ReturnsAfterEveryItemHandled()
    {
        var worker = new RecordingWorker(gated: true);
        await using var processor = new BackgroundProcessor<Item>(worker);
        processor.Start();
        for (var i = 0; i < 5; i++)
            await processor.EnqueueAsync(new Item(i));

        var completing = processor.CompleteAsync();
        await Task.Delay(50);
        completing.IsCompleted.ShouldBeFalse();

        worker.Release();
        await completing.WaitAsync(Timeout);

        worker.Handled.ShouldBe([0, 1, 2, 3, 4]);
        processor.IsRunning.ShouldBeFalse();
    }

    [Test]
    public async Task CompleteAsync_ItemQueuedAfter_IsRefused()
    {
        var worker = new RecordingWorker();
        await using var processor = new BackgroundProcessor<Item>(worker);
        processor.Start();
        await processor.EnqueueAsync(new Item(1));

        await processor.CompleteAsync().WaitAsync(Timeout);

        processor.TryEnqueue(new Item(2)).ShouldBeFalse();
        await Should.ThrowAsync<ChannelClosedException>(() => processor.EnqueueAsync(new Item(3)).AsTask());
        worker.Handled.ShouldBe([1]);
    }

    [Test]
    public async Task CompleteAsync_NotStarted_HandlesItemsQueuedBefore()
    {
        var worker = new RecordingWorker();
        await using var processor = new BackgroundProcessor<Item>(worker);
        await processor.EnqueueAsync(new Item(1));
        await processor.EnqueueAsync(new Item(2));

        await processor.CompleteAsync().WaitAsync(Timeout);

        worker.Handled.ShouldBe([1, 2]);
    }

    [Test]
    public async Task CompleteAsync_CalledTwice_BothReturnAfterTheQueueDrains()
    {
        var worker = new RecordingWorker(gated: true);
        await using var processor = new BackgroundProcessor<Item>(worker);
        processor.Start();
        await processor.EnqueueAsync(new Item(1));

        var first = processor.CompleteAsync();
        var second = processor.CompleteAsync();
        worker.Release();

        await Task.WhenAll(first, second).WaitAsync(Timeout);
        worker.Handled.ShouldBe([1]);
    }

    [Test]
    public async Task CompleteAsync_TokenCancelled_StopsWaitingAndLoopGoesOn()
    {
        var worker = new RecordingWorker(gated: true);
        await using var processor = new BackgroundProcessor<Item>(worker);
        processor.Start();
        await processor.EnqueueAsync(new Item(1));
        using var cts = new CancellationTokenSource();

        var completing = processor.CompleteAsync(cts.Token);
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => completing.WaitAsync(Timeout));
        processor.IsRunning.ShouldBeTrue();

        worker.Release();
        await processor.CompleteAsync().WaitAsync(Timeout);
        worker.Handled.ShouldBe([1]);
    }

    [Test]
    public async Task CompleteAsync_TokenAlreadyCancelled_QueueStaysOpen()
    {
        var worker = new RecordingWorker();
        await using var processor = new BackgroundProcessor<Item>(worker);

        await Should.ThrowAsync<OperationCanceledException>(
            () => processor.CompleteAsync(new CancellationToken(canceled: true)));

        processor.TryEnqueue(new Item(1)).ShouldBeTrue();
        processor.IsRunning.ShouldBeFalse();
    }

    [Test]
    public async Task CompleteAsync_StartTokenCancelledBeforeQueueDrains_ThrowsOperationCanceled()
    {
        var worker = new RecordingWorker(gated: true);
        await using var processor = new BackgroundProcessor<Item>(worker);
        using var stop = new CancellationTokenSource();
        processor.Start(stop.Token);
        await processor.EnqueueAsync(new Item(1));
        await processor.EnqueueAsync(new Item(2));

        var completing = processor.CompleteAsync();
        await stop.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => completing.WaitAsync(Timeout));
        worker.Handled.ShouldBeEmpty();
    }

    [Test]
    public async Task CompleteAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var processor = new BackgroundProcessor<Item>(new RecordingWorker());
        processor.Start();
        await processor.DisposeAsync();

        await Should.ThrowAsync<ObjectDisposedException>(() => processor.CompleteAsync());
    }

    [Test]
    public async Task Start_CalledFromManyThreads_RunsOneLoop()
    {
        for (var round = 0; round < 50; round++)
        {
            var worker = new RecordingWorker(gated: true);
            await using var processor = new BackgroundProcessor<Item>(worker);
            for (var i = 0; i < 8; i++)
                await processor.EnqueueAsync(new Item(i));

            using var barrier = new Barrier(8);
            var starts = Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() => { barrier.SignalAndWait(); processor.Start(); }))
                .ToArray();
            await Task.WhenAll(starts).WaitAsync(Timeout);
            await Task.Delay(20);
            worker.Release();
            await processor.CompleteAsync().WaitAsync(Timeout);

            worker.MaxConcurrent.ShouldBe(1, $"round {round}");
            worker.Handled.Count.ShouldBe(8);
        }
    }

    [Test]
    public async Task Start_AfterDispose_ThrowsObjectDisposed()
    {
        var processor = new BackgroundProcessor<Item>(new RecordingWorker());
        await processor.DisposeAsync();

        Should.Throw<ObjectDisposedException>(() => processor.Start());
    }

    /// <summary>Holds the item it is handling until released, whatever the token says.</summary>
    private sealed class StubbornWorker : IBackgroundWorker<Item>
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task HandleAsync(Item? item, CancellationToken token)
        {
            Started.TrySetResult();
            await Gate.Task;
        }
    }

    [Test]
    public async Task DisposeAsync_CalledTwice_SecondReturnsOnceTheLoopStopped()
    {
        var worker = new StubbornWorker();
        var processor = new BackgroundProcessor<Item>(worker);
        processor.Start();
        await processor.EnqueueAsync(new Item(1));
        await worker.Started.Task.WaitAsync(Timeout);

        var first = processor.DisposeAsync().AsTask();
        var second = processor.DisposeAsync().AsTask();
        await Task.Delay(50);
        second.IsCompleted.ShouldBeFalse();

        worker.Gate.SetResult();
        await Task.WhenAll(first, second).WaitAsync(Timeout);
        processor.IsRunning.ShouldBeFalse();
    }

    [Test]
    public async Task ProcessAsync_OnErrorThrows_LaterItemsHandledAndFailureReported()
    {
        var worker = new RecordingWorker(fail: item => item?.Id == 1 ? new InvalidOperationException("worker") : null);
        var info = new ConcurrentQueue<string>();
        await using var processor = new BackgroundProcessor<Item>(
            worker,
            capacity: 1,
            onError: (_, _) => throw new InvalidOperationException("onError"),
            onInfo: info.Enqueue);
        processor.Start();

        for (var i = 0; i < 4; i++)
            await processor.EnqueueAsync(new Item(i)).AsTask().WaitAsync(Timeout);
        await processor.CompleteAsync().WaitAsync(Timeout);

        worker.Handled.ShouldBe([0, 2, 3]);
        info.ShouldContain(m => m.Contains("onError") && m.Contains("InvalidOperationException"));
    }

    [Test]
    public async Task CompleteAsync_TypedAction_ReturnsAfterEveryItemHandled()
    {
        var worker = new RecordingWorker(gated: true);
        await using var processor = new BackgroundProcessor<Item, Op>(worker);
        processor.Start();
        for (var i = 0; i < 3; i++)
            await processor.EnqueueAsync(new Item(i), Op.Go);

        var completing = processor.CompleteAsync();
        worker.Release();
        await completing.WaitAsync(Timeout);

        worker.Handled.ShouldBe([0, 1, 2]);
        processor.TryEnqueue(new Item(9), Op.Go).ShouldBeFalse();
    }

    [Test]
    public async Task ProcessAsync_TypedActionOnErrorThrows_LaterItemsHandled()
    {
        var worker = new RecordingWorker(fail: item => item?.Id == 0 ? new InvalidOperationException("worker") : null);
        await using var processor = new BackgroundProcessor<Item, Op>(
            worker,
            capacity: 1,
            onError: (_, _) => throw new InvalidOperationException("onError"));
        processor.Start();

        for (var i = 0; i < 3; i++)
            await processor.EnqueueAsync(new Item(i), Op.Go).AsTask().WaitAsync(Timeout);
        await processor.CompleteAsync().WaitAsync(Timeout);

        worker.Handled.ShouldBe([1, 2]);
    }

    /// <summary>Runs <see cref="Call"/> on each item, inline or in a task it starts, and keeps what it threw.</summary>
    private sealed class SelfStoppingWorker(bool inStartedTask) : IBackgroundWorker<Item>
    {
        public Func<Task> Call { get; set; } = () => Task.CompletedTask;
        public TaskCompletionSource<Exception?> Thrown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task HandleAsync(Item? item, CancellationToken token)
        {
            try
            {
                if (inStartedTask) await Task.Run(Call, CancellationToken.None).WaitAsync(Timeout, token);
                else await Call().WaitAsync(Timeout, token);
                Thrown.TrySetResult(null);
            }
            catch (Exception ex)
            {
                Thrown.TrySetResult(ex);
            }
        }
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task CompleteAsyncOrDisposeAsync_CalledFromTheWorker_ThrowsInvalidOperationAtOnce(
        bool dispose, bool inStartedTask)
    {
        var worker = new SelfStoppingWorker(inStartedTask);
        var processor = new BackgroundProcessor<Item>(worker);
        worker.Call = dispose ? () => processor.DisposeAsync().AsTask() : () => processor.CompleteAsync();
        processor.Start();

        await processor.EnqueueAsync(new Item(1));

        var thrown = await worker.Thrown.Task.WaitAsync(Timeout);
        thrown.ShouldBeOfType<InvalidOperationException>();
        await processor.CompleteAsync().WaitAsync(Timeout);
        await processor.DisposeAsync().AsTask().WaitAsync(Timeout);
    }

    [Test]
    public async Task DisposeAsync_FromATaskTheWorkerStarted_AfterTheLoopEnded_Disposes()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new SelfStoppingWorker(inStartedTask: false);
        var processor = new BackgroundProcessor<Item>(worker);
        Task? disposing = null;
        worker.Call = () =>
        {
            disposing = Task.Run(async () =>
            {
                await release.Task;
                await processor.DisposeAsync();
            });
            return Task.CompletedTask;
        };
        processor.Start();

        await processor.EnqueueAsync(new Item(1));
        (await worker.Thrown.Task.WaitAsync(Timeout)).ShouldBeNull();
        await processor.CompleteAsync().WaitAsync(Timeout);
        release.SetResult();

        await disposing!.WaitAsync(Timeout);
        processor.IsRunning.ShouldBeFalse();
    }
}
