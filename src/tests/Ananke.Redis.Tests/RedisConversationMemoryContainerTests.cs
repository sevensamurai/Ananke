using Ananke.Abstractions.Agents;
using Shouldly;
using StackExchange.Redis;

namespace Ananke.Redis.Tests;

/// <summary>
/// <see cref="RedisConversationMemory"/> on a Valkey container. The tests are ignored when Docker isn't available.
/// </summary>
[TestFixture]
[Category("Containers")]
[Explicit("Starts a container. Run with: bash scripts/ci.sh --containers")]
public sealed class RedisConversationMemoryContainerTests
{
    private ValkeyServer? _server;
    private ConnectionMultiplexer? _redis;

    private IConnectionMultiplexer Redis =>
        _redis ?? throw new InvalidOperationException("No connection: the fixture did not start");

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _server = await ValkeyServer.StartAsync();
        if (_server.Unavailable is null)
            _redis = await ConnectionMultiplexer.ConnectAsync(_server.Endpoint);
    }

    [SetUp]
    public void RequireContainer() => _server!.Require();

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (_redis is not null)
            await _redis.DisposeAsync();
        if (_server is not null)
            await _server.DisposeAsync();
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    [Test]
    public async Task AddThenGetHistory_ReturnsMessagesInOrder()
    {
        var memory = new RedisConversationMemory(Redis);
        var session = NewId();

        await memory.AddAsync(session, AgentMessage.User("one"));
        await memory.AddAsync(session, [AgentMessage.Assistant("two"), AgentMessage.User("three")]);

        var history = await memory.GetHistoryAsync(session);
        history.Select(m => m.Content).ShouldBe(["one", "two", "three"]);
    }

    [Test]
    public async Task AddAsync_WithTtl_SetsTheSessionExpiry()
    {
        var memory = new RedisConversationMemory(Redis, prefix: "test:memory", ttl: TimeSpan.FromMinutes(5));
        var session = NewId();

        await memory.AddAsync(session, AgentMessage.User("hello"));

        var ttl = await Redis.GetDatabase().KeyTimeToLiveAsync($"test:memory:{session}");
        ttl.ShouldNotBeNull();
        ttl.Value.ShouldBeInRange(TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));
    }

    [Test]
    public async Task ClearAsync_LeavesNoHistory()
    {
        var memory = new RedisConversationMemory(Redis);
        var session = NewId();
        await memory.AddAsync(session, AgentMessage.User("hello"));

        await memory.ClearAsync(session);

        (await memory.GetHistoryAsync(session)).ShouldBeEmpty();
    }
}
