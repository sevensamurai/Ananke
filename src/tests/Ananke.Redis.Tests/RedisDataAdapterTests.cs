using Ananke.Abstractions.Config;
using Microsoft.Extensions.Options;
using Shouldly;
using StackExchange.Redis;
using System.Net;
using System.Net.Sockets;

namespace Ananke.Redis.Tests;

/// <summary>
/// <see cref="RedisDataAdapter"/> without a Redis: against a port nothing listens on every operation throws
/// rather than reading as a missing key, a caller's token ends its wait on the first connect, and the client options
/// it builds from <see cref="CacheConfig"/>.
/// </summary>
[TestFixture]
public class RedisDataAdapterTests
{
    private static CacheConfig UnreachableConfig() => new()
    {
        Host = "127.0.0.1",
        Port = 1,
        ConnectTimeoutMs = 200,
        SyncTimeoutMs = 300,
        AsyncTimeoutMs = 400,
    };

    private static RedisDataAdapter Unreachable() => new(Options.Create(UnreachableConfig()));

    [Test]
    public async Task Operations_RedisUnreachable_Throw()
    {
        await using var adapter = Unreachable();

        await Task.WhenAll(
            ShouldFail(() => adapter.GetValueAsync("k")),
            ShouldFail(() => adapter.SetValueAsync("k", "v")));
    }

    // Without the token reaching the connect, the cancelled call waits out the 30 s connect and fails with a
    // connection error; the test's own CancelAfter token then ends the wait, with a token the assert rejects.
    [Test, CancelAfter(10_000)]
    public async Task GetValueAsync_CancelledDuringFirstConnect_ThrowsAndConnectGoesOn()
    {
        var server = SilentServer.Start();
        await using var adapter = new RedisDataAdapter(Options.Create(server.Config()));
        try
        {
            await ShouldBeCancelled(ct => adapter.GetValueAsync("k", ct));

            var second = adapter.GetValueAsync("k");
            server.Dispose();

            await Should.ThrowAsync<RedisConnectionException>(() => second.WaitAsync(TestContext.CurrentContext.CancellationToken));
        }
        finally
        {
            server.Dispose();
        }
    }

    [Test, CancelAfter(10_000)]
    public async Task RunCoordinatedActionAsync_CancelledDuringFirstConnect_ThrowsAndConnectGoesOn()
    {
        var server = SilentServer.Start();
        await using var redisLock = new RedisDistributedLock(Options.Create(server.Config()));
        var runs = 0;
        try
        {
            await ShouldBeCancelled(ct => redisLock.RunCoordinatedActionAsync("r", () => Task.FromResult(++runs), ct));

            var second = redisLock.RunCoordinatedActionAsync("r", () => Task.FromResult(++runs));
            server.Dispose();

            var result = await second.WaitAsync(TestContext.CurrentContext.CancellationToken);
            result.LockAcquired.ShouldBeFalse();
            result.ErrorMessage.ShouldBe("Not connected to Redis");
            runs.ShouldBe(0);
        }
        finally
        {
            server.Dispose();
        }
    }

    [Test]
    public async Task GetConnectionAsync_TimeoutsSet_AppliedToLockConnection()
    {
        await using var redisLock = new RedisDistributedLock(Options.Create(UnreachableConfig()));

        await ShouldFail(() => redisLock.GetValueAsync("k"));

        var applied = ConfigurationOptions.Parse(redisLock.Connection!.Configuration);
        applied.ConnectTimeout.ShouldBe(200);
        applied.SyncTimeout.ShouldBe(300);
        applied.AsyncTimeout.ShouldBe(400);
    }

    [Test]
    public void BuildOptions_Parts_ConnectOnce()
    {
        var options = RedisDataAdapter.BuildOptions(new CacheConfig { Host = "localhost" });

        options.ConnectRetry.ShouldBe(1);
    }

    [Test]
    public void BuildOptions_ConnectionStringWithoutConnectRetry_ConnectOnce()
    {
        var options = RedisDataAdapter.BuildOptions(new CacheConfig { ConnectionString = "localhost:6379" });

        options.ConnectRetry.ShouldBe(1);
    }

    [TestCase("localhost:6379,connectRetry=4")]
    [TestCase("localhost:6379, connectRetry = 4")]
    public void BuildOptions_ConnectionStringSetsConnectRetry_IsKept(string connectionString)
    {
        var options = RedisDataAdapter.BuildOptions(new CacheConfig { ConnectionString = connectionString });

        options.ConnectRetry.ShouldBe(4);
    }

    [Test]
    public void BuildOptions_TimeoutsNull_KeepClientDefaults()
    {
        var defaults = new ConfigurationOptions();

        var options = RedisDataAdapter.BuildOptions(new CacheConfig());

        options.ConnectTimeout.ShouldBe(defaults.ConnectTimeout);
        options.SyncTimeout.ShouldBe(defaults.SyncTimeout);
        options.AsyncTimeout.ShouldBe(defaults.AsyncTimeout);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void BuildOptions_TimeoutNotPositive_Throws(int timeout)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RedisDataAdapter.BuildOptions(new CacheConfig { ConnectTimeoutMs = timeout }));
        Should.Throw<ArgumentOutOfRangeException>(() => RedisDataAdapter.BuildOptions(new CacheConfig { SyncTimeoutMs = timeout }));
        Should.Throw<ArgumentOutOfRangeException>(() => RedisDataAdapter.BuildOptions(new CacheConfig { AsyncTimeoutMs = timeout }));
    }

    [Test]
    public void BuildOptions_ConnectionStringAndTimeouts_TimeoutsWin()
    {
        var options = RedisDataAdapter.BuildOptions(new CacheConfig
        {
            ConnectionString = "localhost:6379,connectTimeout=9000,syncTimeout=9000",
            ConnectTimeoutMs = 200,
            AsyncTimeoutMs = 400,
        });

        options.ConnectTimeout.ShouldBe(200);
        options.SyncTimeout.ShouldBe(9000);
        options.AsyncTimeout.ShouldBe(400);
    }

    [Test]
    public async Task Constructor_LockOnUnreachableServer_DoesNotConnect()
    {
        await using var redisLock = new RedisDistributedLock(Options.Create(new CacheConfig { Host = "127.0.0.1", Port = 1 }));

        redisLock.HasConnection.ShouldBeFalse();
    }

    [Test]
    public async Task SetValueAsync_ExpiryNotPositive_Throws()
    {
        await using var adapter = Unreachable();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => adapter.SetValueAsync("k", "v", TimeSpan.Zero));
        adapter.HasConnection.ShouldBeFalse();
    }

    [Test]
    public void BuildOptions_Parts_CarryCredentialsTlsAndDatabase()
    {
        var options = RedisDataAdapter.BuildOptions(new CacheConfig
        {
            Host = "cache.example.com",
            Port = 6380,
            Username = "app",
            Password = "secret",
            Ssl = true,
            Database = 3,
        });

        var endPoint = options.EndPoints.ShouldHaveSingleItem().ShouldBeOfType<DnsEndPoint>();
        endPoint.Host.ShouldBe("cache.example.com");
        endPoint.Port.ShouldBe(6380);
        options.User.ShouldBe("app");
        options.Password.ShouldBe("secret");
        options.Ssl.ShouldBeTrue();
        options.DefaultDatabase.ShouldBe(3);
        options.AbortOnConnectFail.ShouldBeFalse();
    }

    [Test]
    public void BuildOptions_ConnectionString_WinsOverParts()
    {
        var options = RedisDataAdapter.BuildOptions(new CacheConfig
        {
            Host = "ignored.example.com",
            Password = "ignored",
            ConnectionString = "cache.example.com:6380,user=app,password=secret,ssl=true,defaultDatabase=2",
        });

        var endPoint = options.EndPoints.ShouldHaveSingleItem().ShouldBeOfType<DnsEndPoint>();
        endPoint.Host.ShouldBe("cache.example.com");
        endPoint.Port.ShouldBe(6380);
        options.User.ShouldBe("app");
        options.Password.ShouldBe("secret");
        options.Ssl.ShouldBeTrue();
        options.DefaultDatabase.ShouldBe(2);
        options.AbortOnConnectFail.ShouldBeFalse();
    }

    [Test]
    public void BuildOptions_ConnectionStringSetsAbortConnect_IsKept()
    {
        var options = RedisDataAdapter.BuildOptions(new CacheConfig { ConnectionString = "localhost:6379,abortConnect=true" });

        options.AbortOnConnectFail.ShouldBeTrue();
    }

    [Test]
    public void BuildOptions_PasswordContainsAbortConnect_DoesNotAbort()
    {
        var options = RedisDataAdapter.BuildOptions(new CacheConfig { ConnectionString = "localhost:6379,password=xabortConnectx" });

        options.AbortOnConnectFail.ShouldBeFalse();
    }

    [TestCase(null, "redlock:{0}")]
    [TestCase("seren:", "seren:redlock:{0}")]
    [TestCase("{seren}:", "{{seren}}:redlock:{0}")]
    public void LockKeyFormat_KeyPrefix_PrefixesLockNames(string? prefix, string expected) =>
        RedisDistributedLock.LockKeyFormat(new CacheConfig { KeyPrefix = prefix }).ShouldBe(expected);

    /// <summary>
    /// Starts <paramref name="operation"/>, cancels its token while it waits, and expects it to end with that token's
    /// <see cref="OperationCanceledException"/>. The test's CancelAfter token ends a wait that hangs.
    /// </summary>
    private static async Task ShouldBeCancelled(Func<CancellationToken, Task> operation)
    {
        using var cts = new CancellationTokenSource();
        var call = operation(cts.Token);
        call.IsCompleted.ShouldBeFalse();

        await cts.CancelAsync().ConfigureAwait(false);

        var ex = await Should.ThrowAsync<OperationCanceledException>(() => call.WaitAsync(TestContext.CurrentContext.CancellationToken)).ConfigureAwait(false);
        ex.CancellationToken.ShouldBe(cts.Token);
    }

    /// <summary>
    /// A local port that takes connections and never answers, so a connect to it waits for its whole connect timeout
    /// (30 s) unless the server is disposed first, which ends it.
    /// </summary>
    private sealed class SilentServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        public static SilentServer Start()
        {
            var server = new SilentServer();
            server._listener.Start();
            return server;
        }

        public CacheConfig Config() => new()
        {
            Host = "127.0.0.1",
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port,
            ConnectTimeoutMs = 30_000,
            SyncTimeoutMs = 500,
            AsyncTimeoutMs = 500,
        };

        public void Dispose() => _listener.Dispose();
    }

    // WaitAsync's own TimeoutException means the call hung, which is not the failure asserted here.
    private static async Task ShouldFail(Func<Task> operation)
    {
        var ex = await Should.ThrowAsync<Exception>(() => operation().WaitAsync(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
        ex.GetType().ShouldNotBe(typeof(TimeoutException));
    }
}
