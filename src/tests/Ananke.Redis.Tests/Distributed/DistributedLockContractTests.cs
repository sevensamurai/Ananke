using Ananke.Abstractions.Distributed;
using Shouldly;

namespace Ananke.Redis.Tests.Distributed;

/// <summary>
/// The behaviour every <see cref="IDistributedLock"/> shares. Each implementation runs these tests through a fixture
/// that derives from this class.
/// </summary>
public abstract class DistributedLockContractTests
{
    protected static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>The lock under test; one instance serves a whole fixture.</summary>
    protected abstract IDistributedLock Lock { get; }

    protected static string NewResource() => $"lock-test-{Guid.NewGuid():N}";

    /// <summary>
    /// Takes the lock on <paramref name="resourceId"/> and keeps it until the returned holder is released.
    /// </summary>
    protected async Task<Holder> HoldAsync(string resourceId)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = Lock.RunCoordinatedActionAsync(resourceId, async () =>
        {
            entered.SetResult();
            await release.Task.ConfigureAwait(false);
            return 0;
        });
        await Task.WhenAny(entered.Task, run).WaitAsync(Timeout).ConfigureAwait(false);
        if (!entered.Task.IsCompleted)
            Assert.Fail($"The lock was not taken: {(await run.ConfigureAwait(false)).ErrorMessage}");
        return new Holder(release, run);
    }

    protected sealed record Holder(TaskCompletionSource Release, Task<CoordinatedActionResult<int>> Run)
    {
        public async Task ReleaseAsync()
        {
            Release.TrySetResult();
            (await Run.WaitAsync(Timeout).ConfigureAwait(false)).Success.ShouldBeTrue();
        }
    }

    [Test]
    public async Task RunCoordinatedActionAsync_FreeLock_RunsActionAndReturnsItsResult()
    {
        var result = await Lock.RunCoordinatedActionAsync(NewResource(), () => Task.FromResult(42)).WaitAsync(Timeout);

        result.LockAcquired.ShouldBeTrue();
        result.Success.ShouldBeTrue();
        result.Result.ShouldBe(42);
    }

    [Test]
    public async Task RunCoordinatedActionAsync_ActionThrows_RunsOnceAndReturnsItsException()
    {
        var runs = 0;
        var boom = new InvalidOperationException("boom");

        var result = await Lock.RunCoordinatedActionAsync<int>(NewResource(), () =>
        {
            runs++;
            throw boom;
        }).WaitAsync(Timeout);

        runs.ShouldBe(1);
        result.LockAcquired.ShouldBeTrue();
        result.Success.ShouldBeFalse();
        result.Exception.ShouldBeSameAs(boom);
        result.ErrorMessage.ShouldBe("boom");
    }

    [Test]
    public async Task RunCoordinatedActionAsync_ActionThrowsCanceledWithLiveToken_ReturnsItsException()
    {
        var timeout = new TaskCanceledException("timed out");

        var result = await Lock.RunCoordinatedActionAsync<int>(NewResource(), () => throw timeout).WaitAsync(Timeout);

        result.LockAcquired.ShouldBeTrue();
        result.Success.ShouldBeFalse();
        result.Exception.ShouldBeSameAs(timeout);
    }

    [Test]
    public async Task RunCoordinatedActionWithRetryAsync_ActionThrows_RunsOnceAndReturnsItsException()
    {
        var runs = 0;
        var boom = new InvalidOperationException("boom");

        var result = await Lock.RunCoordinatedActionWithRetryAsync<int>(NewResource(), () =>
        {
            runs++;
            throw boom;
        }, maxRetries: 3, retryDelayMs: 10).WaitAsync(Timeout);

        runs.ShouldBe(1);
        result.LockAcquired.ShouldBeTrue();
        result.Success.ShouldBeFalse();
        result.Exception.ShouldBeSameAs(boom);
    }

    [Test]
    public async Task RunCoordinatedActionAsync_AfterActionThrew_LockIsFreeAgain()
    {
        var resource = NewResource();
        await Lock.RunCoordinatedActionAsync<int>(resource, () => throw new InvalidOperationException("boom"))
            .WaitAsync(Timeout);

        var result = await Lock.RunCoordinatedActionAsync(resource, () => Task.FromResult(1)).WaitAsync(Timeout);

        result.Success.ShouldBeTrue();
    }

    [Test]
    public async Task RunCoordinatedActionAsync_LockHeld_ReturnsLockFailedWithoutRunningAction()
    {
        var resource = NewResource();
        var holder = await HoldAsync(resource);
        var runs = 0;

        var result = await Lock.RunCoordinatedActionAsync(resource, () => Task.FromResult(++runs)).WaitAsync(Timeout);
        await holder.ReleaseAsync();

        result.LockAcquired.ShouldBeFalse();
        result.Success.ShouldBeFalse();
        runs.ShouldBe(0);
    }

    [Test]
    public async Task RunCoordinatedActionWithRetryAsync_LockHeldThroughout_RetriesThenReturnsLockFailed()
    {
        var resource = NewResource();
        var holder = await HoldAsync(resource);
        var runs = 0;
        var clock = new DelayRecordingClock();

        var result = await Lock.RunCoordinatedActionWithRetryAsync(
            resource, () => Task.FromResult(++runs), maxRetries: 2, retryDelayMs: 40, clock: clock).WaitAsync(Timeout);
        await holder.ReleaseAsync();

        result.LockAcquired.ShouldBeFalse();
        result.Success.ShouldBeFalse();
        runs.ShouldBe(0);
        clock.Delays.ShouldBe([TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(80)]);
    }

    /// <summary>Records each delay asked of it and lets it end at once.</summary>
    private sealed class DelayRecordingClock : TimeProvider
    {
        private readonly List<TimeSpan> _delays = [];

        public IReadOnlyList<TimeSpan> Delays
        {
            get
            {
                lock (_delays)
                    return [.. _delays];
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (_delays)
                _delays.Add(dueTime);
            return TimeProvider.System.CreateTimer(callback, state, TimeSpan.Zero, period);
        }
    }

    [Test]
    public async Task RunCoordinatedActionWithRetryAsync_LockReleasedDuringRetries_RunsActionOnce()
    {
        var resource = NewResource();
        var holder = await HoldAsync(resource);
        var runs = 0;

        var retrying = Lock.RunCoordinatedActionWithRetryAsync(
            resource, () => Task.FromResult(++runs), maxRetries: 8, retryDelayMs: 20);
        await Task.Delay(50);
        await holder.ReleaseAsync();
        var result = await retrying.WaitAsync(Timeout);

        result.Success.ShouldBeTrue();
        result.Result.ShouldBe(1);
        runs.ShouldBe(1);
    }

    [Test]
    public async Task RunCoordinatedActionAsync_OtherResourceHeld_RunsAction()
    {
        var holder = await HoldAsync(NewResource());

        var result = await Lock.RunCoordinatedActionAsync(NewResource(), () => Task.FromResult(1)).WaitAsync(Timeout);
        await holder.ReleaseAsync();

        result.Success.ShouldBeTrue();
    }

    [Test]
    public async Task RunCoordinatedActionAsync_CancelledToken_ThrowsWithoutRunningAction()
    {
        var runs = 0;

        await Should.ThrowAsync<OperationCanceledException>(() =>
            Lock.RunCoordinatedActionAsync(NewResource(), () => Task.FromResult(++runs), new CancellationToken(true))
                .WaitAsync(Timeout));

        runs.ShouldBe(0);
    }

    [Test]
    public async Task RunCoordinatedActionWithRetryAsync_CancelledWhileWaitingForLock_Throws()
    {
        var resource = NewResource();
        var holder = await HoldAsync(resource);
        var runs = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            Lock.RunCoordinatedActionWithRetryAsync(
                resource, () => Task.FromResult(++runs), maxRetries: 100, retryDelayMs: 20, cts.Token)
                .WaitAsync(Timeout));
        await holder.ReleaseAsync();

        runs.ShouldBe(0);
    }
}

[TestFixture]
public sealed class InMemoryDistributedLockTests : DistributedLockContractTests
{
    private readonly InMemoryDistributedLock _lock = new();

    protected override IDistributedLock Lock => _lock;
}
