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

    [Test]
    public async Task Operations_TokenAlreadyCancelled_ThrowOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            _memory.AddAsync("session", AgentMessage.User("hello"), cts.Token).WaitAsync(TimeSpan.FromSeconds(10)));
        await Should.ThrowAsync<OperationCanceledException>(
            _memory.AddAsync("session", [AgentMessage.User("hello")], cts.Token).WaitAsync(TimeSpan.FromSeconds(10)));
        await Should.ThrowAsync<OperationCanceledException>(
            _memory.GetHistoryAsync("session", cts.Token).WaitAsync(TimeSpan.FromSeconds(10)));
        await Should.ThrowAsync<OperationCanceledException>(
            _memory.ClearAsync("session", cts.Token).WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Test]
    public async Task GetHistoryAsync_CancelledWhileWaitingForTheServer_ThrowsOperationCanceled()
    {
        // Not closed: the client holds the command until it connects or its timeout (5 s) passes.
        using var waiting = await ConnectionMultiplexer.ConnectAsync("127.0.0.1:1,abortConnect=false,connectTimeout=100");
        var memory = new RedisConversationMemory(waiting);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Should.ThrowAsync<OperationCanceledException>(
            memory.GetHistoryAsync("session", cts.Token).WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Test]
    public async Task AddAsync_WithTtl_PushesAndSetsExpiryInOneTransaction()
    {
        var recording = new RecordingMultiplexer();
        var memory = new RedisConversationMemory(recording.Connection, ttl: TimeSpan.FromMinutes(5));

        await memory.AddAsync("session", AgentMessage.User("hello"));
        await memory.AddAsync("session", [AgentMessage.User("hello"), AgentMessage.Assistant("hi")]);

        string[] oneAdd =
        [
            "multiplexer.GetDatabase",
            "database.CreateTransaction",
            "transaction.ListRightPushAsync",
            "transaction.KeyExpireAsync",
            "transaction.ExecuteAsync",
        ];
        recording.Calls.ShouldBe([.. oneAdd, .. oneAdd]);
    }

    [Test]
    public async Task AddAsync_WithoutTtl_PushesWithoutATransaction()
    {
        var recording = new RecordingMultiplexer();
        var memory = new RedisConversationMemory(recording.Connection);

        await memory.AddAsync("session", AgentMessage.User("hello"));

        recording.Calls.ShouldBe(["multiplexer.GetDatabase", "database.ListRightPushAsync"]);
    }
}
