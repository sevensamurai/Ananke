using System.Collections.Concurrent;
using Ananke.Abstractions.Distributed;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Ananke.Abstractions.Tests.Distributed;

[TestFixture]
public class DistributedLockExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private DelayClock _clock = new();

    [SetUp]
    public void SetUp() => _clock = new DelayClock();

    [Test]
    public async Task RunCoordinatedActionWithRetryAsync_LockFreedAfterTwoTries_RunsActionOnceOnThirdTry()
    {
        var locker = new HeldLock(heldFor: 2);
        var runs = 0;

        var retrying = locker.RunCoordinatedActionWithRetryAsync(
            "r", () => Task.FromResult(++runs), maxRetries: 3, retryDelayMs: 100, clock: _clock);
        _clock.Advance(await _clock.NextDelayAsync());
        _clock.Advance(await _clock.NextDelayAsync());
        var result = await retrying.WaitAsync(Timeout);

        result.Success.ShouldBeTrue();
        result.Result.ShouldBe(1);
        runs.ShouldBe(1);
        locker.Attempts.ShouldBe(3);
        _clock.Delays.ShouldBe([TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200)]);
    }

    [Test]
    public async Task RunCoordinatedActionWithRetryAsync_LockHeldThroughout_DoublesDelayUpToClampThenReturnsLockFailed()
    {
        var locker = new HeldLock(heldFor: int.MaxValue);
        int[] expected = [10, 20, 40, 80, 160, 320, 640, 1280, 2560, 5120, 10240, 10240];

        var retrying = locker.RunCoordinatedActionWithRetryAsync(
            "r", () => Task.FromResult(0), maxRetries: expected.Length, retryDelayMs: 10, clock: _clock);
        foreach (var _ in expected)
            _clock.Advance(await _clock.NextDelayAsync());
        var result = await retrying.WaitAsync(Timeout);

        result.LockAcquired.ShouldBeFalse();
        locker.Attempts.ShouldBe(expected.Length + 1);
        _clock.Delays.ShouldBe(expected.Select(ms => TimeSpan.FromMilliseconds(ms)));
    }

    [Test]
    public async Task RunCoordinatedActionWithRetryAsync_CancelledBetweenTries_ThrowsWithoutAnotherTry()
    {
        var locker = new HeldLock(heldFor: int.MaxValue);
        using var cts = new CancellationTokenSource();

        var retrying = locker.RunCoordinatedActionWithRetryAsync(
            "r", () => Task.FromResult(0), maxRetries: 3, retryDelayMs: 100, cts.Token, _clock);
        await _clock.NextDelayAsync();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => retrying.WaitAsync(Timeout));
        _clock.Advance(TimeSpan.FromSeconds(1));
        locker.Attempts.ShouldBe(1);
    }

    /// <summary>A lock held elsewhere for its first <c>heldFor</c> attempts.</summary>
    private sealed class HeldLock(int heldFor) : IDistributedLock
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        public async Task<CoordinatedActionResult<R>> RunCoordinatedActionAsync<R>(
            string resourceId, Func<Task<R>> action, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _attempts) <= heldFor)
                return CoordinatedActionResult<R>.LockFailed();
            return CoordinatedActionResult<R>.Succeeded(await action());
        }
    }

    /// <summary>A fake clock that records the due time of every timer, each delay between tries.</summary>
    private sealed class DelayClock : FakeTimeProvider
    {
        private readonly SemaphoreSlim _created = new(0);

        public ConcurrentQueue<TimeSpan> Delays { get; } = new();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            Delays.Enqueue(dueTime);
            _created.Release();
            return timer;
        }

        /// <summary>Waits for the next timer and returns its due time.</summary>
        public async Task<TimeSpan> NextDelayAsync()
        {
            (await _created.WaitAsync(Timeout)).ShouldBeTrue("no delay started");
            return Delays.Last();
        }
    }
}
