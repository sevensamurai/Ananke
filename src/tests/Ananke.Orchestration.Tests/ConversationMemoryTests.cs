using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Memory;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class ConversationMemoryTests
{
    [Test]
    public async Task AddAndGet_SingleMessage_ReturnsMessage()
    {
        var memory = new InMemoryConversationMemory();

        await memory.AddAsync("session-1", AgentMessage.User("hello"));

        var history = await memory.GetHistoryAsync("session-1");
        history.Count.ShouldBe(1);
        history[0].Role.ShouldBe(AgentRole.User);
        history[0].Content.ShouldBe("hello");
    }

    [Test]
    public async Task AddAndGet_MultipleMessages_ReturnsAll()
    {
        var memory = new InMemoryConversationMemory();

        await memory.AddAsync("s1", [
            AgentMessage.User("q1"),
            AgentMessage.Assistant("a1")
        ]);
        await memory.AddAsync("s1", AgentMessage.User("q2"));

        var history = await memory.GetHistoryAsync("s1");
        history.Count.ShouldBe(3);
        history[0].Content.ShouldBe("q1");
        history[1].Content.ShouldBe("a1");
        history[2].Content.ShouldBe("q2");
    }

    [Test]
    public async Task GetHistory_NonexistentSession_ReturnsEmpty()
    {
        var memory = new InMemoryConversationMemory();

        var history = await memory.GetHistoryAsync("nonexistent");

        history.ShouldBeEmpty();
    }

    [Test]
    public async Task Clear_RemovesSession()
    {
        var memory = new InMemoryConversationMemory();
        await memory.AddAsync("s1", AgentMessage.User("test"));

        await memory.ClearAsync("s1");

        var history = await memory.GetHistoryAsync("s1");
        history.ShouldBeEmpty();
        memory.SessionCount.ShouldBe(0);
    }

    [Test]
    public async Task Sessions_AreIsolated()
    {
        var memory = new InMemoryConversationMemory();
        await memory.AddAsync("alice", AgentMessage.User("hi from alice"));
        await memory.AddAsync("bob", AgentMessage.User("hi from bob"));

        var alice = await memory.GetHistoryAsync("alice");
        var bob = await memory.GetHistoryAsync("bob");

        alice.Count.ShouldBe(1);
        alice[0].Content.ShouldBe("hi from alice");
        bob.Count.ShouldBe(1);
        bob[0].Content.ShouldBe("hi from bob");
        memory.SessionCount.ShouldBe(2);
    }

    [Test]
    public async Task SessionCount_ReflectsActiveSessions()
    {
        var memory = new InMemoryConversationMemory();
        memory.SessionCount.ShouldBe(0);

        await memory.AddAsync("a", AgentMessage.User("1"));
        await memory.AddAsync("b", AgentMessage.User("2"));
        memory.SessionCount.ShouldBe(2);

        await memory.ClearAsync("a");
        memory.SessionCount.ShouldBe(1);
    }

    [Test]
    public async Task GetHistory_ReturnsSnapshot_NotLiveReference()
    {
        var memory = new InMemoryConversationMemory();
        await memory.AddAsync("s1", AgentMessage.User("first"));

        var snapshot = await memory.GetHistoryAsync("s1");

        await memory.AddAsync("s1", AgentMessage.User("second"));

        // Snapshot should not include the second message
        snapshot.Count.ShouldBe(1);
    }

    [Test]
    public async Task CleanupExpired_WithNoTtl_IsNoOp()
    {
        var memory = new InMemoryConversationMemory(); // no TTL
        await memory.AddAsync("s1", AgentMessage.User("test"));

        await memory.CleanupExpiredAsync();

        memory.SessionCount.ShouldBe(1);
    }
}
