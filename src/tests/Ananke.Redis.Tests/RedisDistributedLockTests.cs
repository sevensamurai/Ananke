using Ananke.Abstractions.Config;
using Ananke.Abstractions.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using StackExchange.Redis;

namespace Ananke.Redis.Tests;

/// <summary>
/// <see cref="RedisDistributedLock"/> (values and locks) and <c>AddRedis</c> on two Valkey containers: one that
/// requires a password and one that requires none. The tests are ignored when Docker isn't available.
/// </summary>
[TestFixture]
[Category("Containers")]
[Explicit("Starts a container. Run with: bash scripts/ci.sh --containers")]
public sealed class RedisDistributedLockTests
{
    private const string Password = "s3cret";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private ValkeyServer? _secured;
    private ValkeyServer? _open;
    private ConnectionMultiplexer? _admin;

    private string Host => _secured!.Host;
    private int Port => _secured!.Port;
    private string Endpoint => _secured!.Endpoint;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        var secured = ValkeyServer.StartAsync(Password);
        var open = ValkeyServer.StartAsync();
        _secured = await secured;
        _open = await open;
        if (_secured.Unavailable is null)
            _admin = await ConnectionMultiplexer.ConnectAsync(_secured.ConnectionString);
    }

    [SetUp]
    public void RequireContainer() => _secured!.Require();

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (_admin is not null)
            await _admin.DisposeAsync();
        if (_secured is not null)
            await _secured.DisposeAsync();
        if (_open is not null)
            await _open.DisposeAsync();
    }

    /// <summary>A lock on the server that requires <see cref="Password"/>, with that password.</summary>
    private RedisDistributedLock NewLock(Action<CacheConfig> configure)
    {
        var config = new CacheConfig { Host = Host, Port = Port, Password = Password };
        configure(config);
        return new RedisDistributedLock(Options.Create(config));
    }

    private static string NewKey() => $"k-{Guid.NewGuid():N}";

    private static async Task ValueRoundTripsAndLockIsAcquired(RedisDistributedLock redisLock)
    {
        var key = NewKey();
        await redisLock.SetValueAsync(key, "hello").WaitAsync(Timeout).ConfigureAwait(false);
        (await redisLock.GetValueAsync(key).WaitAsync(Timeout).ConfigureAwait(false)).ShouldBe("hello");

        var result = await redisLock.RunCoordinatedActionAsync(key, () => Task.FromResult(42))
            .WaitAsync(Timeout).ConfigureAwait(false);
        result.LockAcquired.ShouldBeTrue(result.ErrorMessage);
        result.Success.ShouldBeTrue();
        result.Result.ShouldBe(42);
    }

    [Test]
    public async Task ValuesAndLock_Password_RoundTripAndAcquire()
    {
        await using var redisLock = NewLock(_ => { });

        await ValueRoundTripsAndLockIsAcquired(redisLock);
    }

    [Test]
    public async Task ValuesAndLock_NoPasswordRequired_RoundTripAndAcquire()
    {
        _open!.Require();
        await using var redisLock = new RedisDistributedLock(
            Options.Create(new CacheConfig { Host = _open!.Host, Port = _open.Port }));

        await ValueRoundTripsAndLockIsAcquired(redisLock);
    }

    // A client that can't sign in keeps reconnecting: each read fails at a client timeout, and the lock answers
    // LockFailed without running the action.
    [TestCase(null, TestName = "ValuesAndLock_RequiredPasswordMissing_ThrowAndLockFails")]
    [TestCase("wrong", TestName = "ValuesAndLock_WrongPassword_ThrowAndLockFails")]
    public async Task ValuesAndLock_NotSignedIn_ThrowAndLockFails(string? password)
    {
        var connectionString = Endpoint + (password is null ? "" : $",password={password}");
        await using var redisLock = NewLock(c => c.ConnectionString = connectionString);
        var runs = 0;

        var read = await Should.ThrowAsync<Exception>(() => redisLock.GetValueAsync(NewKey()).WaitAsync(Timeout));
        var exists = await Should.ThrowAsync<Exception>(() => redisLock.ExistsAsync(NewKey()).WaitAsync(Timeout));
        var result = await redisLock.RunCoordinatedActionAsync(NewKey(), () => Task.FromResult(++runs))
            .WaitAsync(Timeout);

        read.ShouldNotBeOfType<TimeoutException>();
        exists.ShouldNotBeOfType<TimeoutException>();
        result.LockAcquired.ShouldBeFalse();
        result.ErrorMessage.ShouldBe("Not connected to Redis");
        runs.ShouldBe(0);
    }

    [Test]
    public async Task ValuesAndLock_UsernameAndPassword_RoundTripAndAcquire()
    {
        await using var redisLock = NewLock(c => c.Username = "default");

        await ValueRoundTripsAndLockIsAcquired(redisLock);
    }

    [Test]
    public async Task GetValueAsync_UnknownUsername_Throws()
    {
        await using var redisLock = NewLock(c => c.Username = "nobody");

        var ex = await Should.ThrowAsync<Exception>(() => redisLock.GetValueAsync(NewKey()).WaitAsync(Timeout));
        ex.ShouldNotBeOfType<TimeoutException>();
    }

    [Test]
    public async Task ValuesAndLock_ConnectionString_WinsOverParts()
    {
        await using var redisLock = NewLock(c =>
        {
            c.Host = "127.0.0.1";
            c.Port = 1;
            c.Password = null;
            c.ConnectionString = $"{Endpoint},password={Password}";
        });

        await ValueRoundTripsAndLockIsAcquired(redisLock);
    }

    [Test]
    public async Task KeysAndLockNames_KeyPrefixAndDatabase_StartWithPrefixInThatDatabase()
    {
        await using var redisLock = NewLock(c =>
        {
            c.KeyPrefix = "app1:";
            c.Database = 3;
        });
        var key = NewKey();
        var db3 = _admin!.GetDatabase(3);

        await redisLock.SetValueAsync(key, "v").WaitAsync(Timeout);
        var lockKeyHeld = await redisLock.RunCoordinatedActionAsync(key, () => db3.KeyExistsAsync($"app1:redlock:{key}"))
            .WaitAsync(Timeout);

        (await db3.StringGetAsync($"app1:{key}")).ToString().ShouldBe("v");
        (await db3.KeyExistsAsync(key)).ShouldBeFalse();
        (await _admin.GetDatabase(0).KeyExistsAsync($"app1:{key}")).ShouldBeFalse();
        lockKeyHeld.Result.ShouldBeTrue();
        (await db3.KeyExistsAsync($"app1:redlock:{key}")).ShouldBeFalse();
    }

    [Test]
    public async Task SetValueAsync_Expiry_SetsAndClearsTimeToLive()
    {
        await using var redisLock = NewLock(_ => { });
        var key = NewKey();
        var db = _admin!.GetDatabase();

        await redisLock.SetValueAsync(key, "v", TimeSpan.FromMinutes(5)).WaitAsync(Timeout);
        var ttl = await db.KeyTimeToLiveAsync(key);
        await redisLock.SetValueAsync(key, "v").WaitAsync(Timeout);

        ttl.ShouldNotBeNull();
        ttl.Value.ShouldBeInRange(TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));
        (await db.KeyTimeToLiveAsync(key)).ShouldBeNull();
    }

    [Test]
    public async Task AddRedis_HostRegisteredConnection_IsUsedAndLeftOpen()
    {
        await using var host = await ConnectionMultiplexer.ConnectAsync($"{Endpoint},password={Password}");
        var services = new ServiceCollection();
        // Nothing listens on port 1: a connection made from the config could not answer.
        services.AddRedis(c =>
        {
            c.Host = "127.0.0.1";
            c.Port = 1;
        });
        services.AddSingleton<IConnectionMultiplexer>(host);

        await using (var provider = services.BuildServiceProvider())
        {
            var store = provider.GetRequiredService<IKeyValueDataAdapter>();
            var locker = provider.GetRequiredService<IDistributedLock>();
            var key = NewKey();

            await store.SetValueAsync(key, "hello").WaitAsync(Timeout);
            (await store.GetValueAsync(key).WaitAsync(Timeout)).ShouldBe("hello");
            var result = await locker.RunCoordinatedActionAsync(key, () => Task.FromResult(1)).WaitAsync(Timeout);
            result.LockAcquired.ShouldBeTrue(result.ErrorMessage);
        }

        host.IsConnected.ShouldBeTrue();
        await host.GetDatabase().PingAsync().WaitAsync(Timeout);
    }
}
