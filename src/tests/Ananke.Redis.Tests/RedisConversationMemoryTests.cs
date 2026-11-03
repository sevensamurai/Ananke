using Ananke.Abstractions.Agents;
using Shouldly;
using StackExchange.Redis;

namespace Ananke.Redis.Tests;

/// <summary>
/// What <see cref="RedisConversationMemory"/> does when Redis cannot be reached. No server is
/// needed: the multiplexer is pointed at a closed port and closed before use.
/// </summary>
[TestFixture]
public class RedisConversationMemoryTests
{
    private ConnectionMultiplexer _redis = null!;
    private RedisConversationMemory _memory = null!;

    [SetUp]
    public async Task SetUp()
    {
        _redis = await ConnectionMultiplexer.ConnectAsync("127.0.0.1:1,abortConnect=false,connectTimeout=100");
        await _redis.CloseAsync();
        _memory = new RedisConversationMemory(_redis);
    }

    [TearDown]
    public void TearDown() => _redis.Dispose();

    [Test]
    public async Task AddAsync_AClosedConnection_Throws() =>
        await Should.ThrowAsync<Exception>(
            _memory.AddAsync("session", AgentMessage.User("hello")).WaitAsync(TimeSpan.FromSeconds(10)));

    [Test]
    public async Task AddAsync_SeveralMessagesOnAClosedConnection_Throws() =>
        await Should.ThrowAsync<Exception>(
            _memory.AddAsync("session", [AgentMessage.User("hello"), AgentMessage.Assistant("hi")])
                .WaitAsync(TimeSpan.FromSeconds(10)));

    [Test]
    public async Task GetHistoryAsync_AClosedConnection_ThrowsRatherThanAnsweringEmpty() =>
        await Should.ThrowAsync<Exception>(
            _memory.GetHistoryAsync("session").WaitAsync(TimeSpan.FromSeconds(10)));

    [Test]
    public async Task ClearAsync_AClosedConnection_Throws() =>
        await Should.ThrowAsync<Exception>(
            _memory.ClearAsync("session").WaitAsync(TimeSpan.FromSeconds(10)));
}
