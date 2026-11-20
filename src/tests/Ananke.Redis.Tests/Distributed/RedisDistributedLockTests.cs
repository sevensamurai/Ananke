using Ananke.Abstractions.Config;
using Ananke.Abstractions.Distributed;
using Microsoft.Extensions.Options;
using RedLockNet.SERedis.Configuration;
using Shouldly;

namespace Ananke.Redis.Tests.Distributed;

/// <summary>
/// The lock contract against <see cref="RedisDistributedLock"/> on a Valkey container. The tests are ignored when
/// Docker isn't available.
/// </summary>
[TestFixture]
public sealed class RedisDistributedLockTests : DistributedLockContractTests
{
    private ValkeyServer? _server;
    private RedisDistributedLock? _lock;

    protected override IDistributedLock Lock =>
        _lock ?? throw new InvalidOperationException("No lock: the fixture did not start");

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _server = await ValkeyServer.StartAsync();
        if (_server.Unavailable is null)
            _lock = new RedisDistributedLock(Options.Create(Config()));
    }

    private CacheConfig Config() => new() { Host = _server!.Host, Port = _server.Port };

    [SetUp]
    public void RequireContainer() => _server!.Require();

    [Test]
    public async Task RunCoordinatedActionAsync_CancelledWhileTryingTheLock_Throws()
    {
        var resource = NewResource();
        var holder = await HoldAsync(resource);
        var runs = 0;
        // Quorum retries that far outlast the token, whatever RedLock's random delays (0-10 ms each) add up to.
        await using var retrying = new RedisDistributedLock(Options.Create(Config()))
        {
            RetryConfiguration = new RedLockRetryConfiguration(retryCount: 10_000, retryDelayMs: 10),
        };
        // Connect before the token starts, so it ends the retries and not the connect.
        (await retrying.RunCoordinatedActionAsync(NewResource(), () => Task.FromResult(0)).WaitAsync(Timeout))
            .Success.ShouldBeTrue();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            retrying.RunCoordinatedActionAsync(resource, () => Task.FromResult(++runs), cts.Token).WaitAsync(Timeout));
        await holder.ReleaseAsync();

        runs.ShouldBe(0);
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (_lock is not null)
            await _lock.DisposeAsync();
        if (_server is not null)
            await _server.DisposeAsync();
    }
}
