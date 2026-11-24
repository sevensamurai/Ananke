using Ananke.Abstractions.Config;
using Ananke.Abstractions.Distributed;
using Ananke.Orchestration.Checkpointing;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Ananke.Redis.Tests;

/// <summary>
/// <see cref="RedisDataAdapter"/> and <see cref="RedisDistributedLock"/> against a Valkey container that is paused: the
/// connection stays open and the server answers nothing. A read throws rather than returning a missing key, the lock is
/// not taken, and a caller that cancels stops waiting on the command. Each test pauses the server and unpauses it before
/// it ends; the tests are ignored when Docker isn't available.
/// </summary>
[TestFixture]
[Category("Containers")]
[Explicit("Starts a container. Run with: bash scripts/ci.sh --containers")]
public sealed class RedisPausedServerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private ValkeyServer? _server;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await ValkeyServer.StartAsync();

    [SetUp]
    public void RequireContainer() => _server!.Require();

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (_server is not null)
            await _server.DisposeAsync();
    }

    private RedisDistributedLock NewLock() =>
        new(Options.Create(new CacheConfig { ConnectionString = _server!.EndpointWithShortTimeouts }));

    private static string NewKey() => $"k-{Guid.NewGuid():N}";

    /// <summary>Pauses the server for <paramref name="whilePaused"/>, and unpauses it however that ends.</summary>
    private async Task WhilePausedAsync(Func<Task> whilePaused)
    {
        await _server!.PauseAsync().WaitAsync(Timeout).ConfigureAwait(false);
        try
        {
            await whilePaused().ConfigureAwait(false);
        }
        finally
        {
            await _server.UnpauseAsync().WaitAsync(Timeout).ConfigureAwait(false);
        }
    }

    // WaitAsync's own TimeoutException means the call hung, which is not the failure asserted here.
    private static async Task ShouldFail<T>(Func<Task<T>> operation)
    {
        var ex = await Should.ThrowAsync<Exception>(() => operation().WaitAsync(Timeout)).ConfigureAwait(false);
        ex.ShouldNotBeOfType<TimeoutException>();
    }

    [Test]
    public async Task GetValueAndExistsAsync_ServerPaused_ThrowRatherThanReadAsMissing()
    {
        await using var redisLock = NewLock();
        var stored = NewKey();
        var missing = NewKey();
        await redisLock.SetValueAsync(stored, "v").WaitAsync(Timeout);
        await redisLock.SetValueAsync(stored + ":n", 7).WaitAsync(Timeout);

        await WhilePausedAsync(async () =>
        {
            await ShouldFail(() => redisLock.GetValueAsync(stored));
            await ShouldFail(() => redisLock.GetValueAsync(missing));
            await ShouldFail(() => redisLock.GetValueAsync<int>(stored + ":n"));
            await ShouldFail(() => redisLock.ExistsAsync(stored));
        });

        (await ReadOnceAnsweringAsync(redisLock, stored)).ShouldBe("v");
        (await redisLock.GetValueAsync<int>(stored + ":n").WaitAsync(Timeout)).ShouldBe(7);
        (await redisLock.GetValueAsync(missing).WaitAsync(Timeout)).ShouldBeNull();
    }

    [Test]
    public async Task GetValueAsync_ServerPausedBeforeFirstConnect_Throws()
    {
        await using var redisLock = NewLock();

        await WhilePausedAsync(() => ShouldFail(() => redisLock.GetValueAsync(NewKey())));
    }

    [Test]
    public async Task RunCoordinatedActionAsync_ServerPaused_ReturnsLockFailedWithoutRunningAction()
    {
        await using var redisLock = NewLock();
        var resource = NewKey();
        (await redisLock.RunCoordinatedActionAsync(resource, () => Task.FromResult(0)).WaitAsync(Timeout))
            .Success.ShouldBeTrue();
        var runs = 0;

        CoordinatedActionResult<int>? result = null;
        await WhilePausedAsync(async () =>
            result = await redisLock.RunCoordinatedActionAsync(resource, () => Task.FromResult(++runs))
                .WaitAsync(Timeout));

        result.ShouldNotBeNull();
        result!.LockAcquired.ShouldBeFalse();
        result.Success.ShouldBeFalse();
        runs.ShouldBe(0);
    }

    [Test]
    public async Task LoadAsync_CancelledWhileThePausedServerHoldsTheCommand_ThrowsOperationCanceled()
    {
        // The default AsyncTimeoutMs (5 s): waiting for it would end in a RedisTimeoutException instead.
        await using var adapter = new RedisDataAdapter(
            Options.Create(new CacheConfig { ConnectionString = _server!.Endpoint }));
        var store = new KeyValueCheckpointStore(adapter);
        await store.ExistsAsync("run").WaitAsync(Timeout);

        await WhilePausedAsync(() => CancelWhileHeldAsync(ct => store.LoadAsync<string>("run", ct)));
    }

    [Test]
    public async Task GetSetRemoveExistsAsync_CancelledWhileThePausedServerHoldsThem_ThrowOperationCanceled()
    {
        await using var adapter = new RedisDataAdapter(
            Options.Create(new CacheConfig { ConnectionString = _server!.Endpoint }));
        var key = NewKey();
        await adapter.ExistsAsync(key).WaitAsync(Timeout);

        await WhilePausedAsync(async () =>
        {
            await CancelWhileHeldAsync(ct => adapter.GetValueAsync(key, ct));
            await CancelWhileHeldAsync(ct => adapter.SetValueAsync(key, "v", ct));
            await CancelWhileHeldAsync(ct => adapter.SetValueAsync(key, "v", TimeSpan.FromMinutes(1), ct));
            await CancelWhileHeldAsync(ct => adapter.RemoveAsync(key, ct));
            await CancelWhileHeldAsync(ct => adapter.ExistsAsync(key, ct));
        });
    }

    /// <summary>
    /// Starts <paramref name="operation"/>, whose command the paused server holds, then cancels its token: it ends
    /// with <see cref="OperationCanceledException"/>, not with the client's timeout.
    /// </summary>
    private static async Task CancelWhileHeldAsync(Func<CancellationToken, Task> operation)
    {
        using var cts = new CancellationTokenSource();
        var running = operation(cts.Token);
        running.IsCompleted.ShouldBeFalse();

        await cts.CancelAsync().ConfigureAwait(false);

        var ex = await Should.ThrowAsync<OperationCanceledException>(() => running.WaitAsync(Timeout)).ConfigureAwait(false);
        ex.CancellationToken.ShouldBe(cts.Token);
    }

    /// <summary>
    /// Reads <paramref name="key"/> once the server answers again: right after an unpause, a command can still time
    /// out on the reply queue the paused server left behind.
    /// </summary>
    private static async Task<string?> ReadOnceAnsweringAsync(RedisDataAdapter adapter, string key)
    {
        using var deadline = new CancellationTokenSource(Timeout);
        while (true)
        {
            try
            {
                return await adapter.GetValueAsync(key).WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (Exception) when (!deadline.IsCancellationRequested)
            {
                await Task.Delay(200, deadline.Token).ConfigureAwait(false);
            }
        }
    }
}
