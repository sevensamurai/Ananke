using Ananke.Abstractions.Config;
using Ananke.Abstractions.Distributed;
using Ananke.Orchestration.Checkpointing;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Ananke.Redis.Tests.Checkpointing;

/// <summary>
/// The <see cref="KeyValueCheckpointStore"/> contract over <see cref="RedisDataAdapter"/> on a Valkey container. The
/// tests are ignored when Docker isn't available.
/// </summary>
[TestFixture]
[Category("Containers")]
[Explicit("Starts a container. Run with: bash scripts/ci.sh --containers")]
public sealed class KeyValueCheckpointStoreRedisTests : KeyValueCheckpointStoreContractTests
{
    private ValkeyServer? _server;
    private ConnectionMultiplexer? _redis;
    private RedisDataAdapter? _adapter;

    protected override IKeyValueDataAdapter Adapter =>
        _adapter ?? throw new InvalidOperationException("No adapter: the fixture did not start");

    // Require ignores or fails the whole fixture when there is no server.
    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _server = await ValkeyServer.StartAsync();
        _server.Require();
        _redis = await ConnectionMultiplexer.ConnectAsync(_server.Endpoint);
        _adapter = new RedisDataAdapter(Options.Create(new CacheConfig()), connection: _redis);
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (_adapter is not null)
            await _adapter.DisposeAsync();
        if (_redis is not null)
            await _redis.DisposeAsync();
        if (_server is not null)
            await _server.DisposeAsync();
    }

    protected override async Task<bool> HasExpiryAsync(string key) =>
        await _redis!.GetDatabase().KeyTimeToLiveAsync(key) is not null;
}
