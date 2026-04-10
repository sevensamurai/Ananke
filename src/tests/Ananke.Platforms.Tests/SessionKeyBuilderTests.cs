using Ananke.Abstractions.Agents;
using Shouldly;

namespace Ananke.Platforms.Tests;

[TestFixture]
public sealed class SessionKeyBuilderTests
{
    [Test]
    public void Build_WithThreadId_IncludesThread()
    {
        var message = CreateMessage(channelId: "C123", threadId: "T456");

        var key = Sessions.SessionKeyBuilder.Build(message);

        key.ShouldBe("C123:T456");
    }

    [Test]
    public void Build_WithoutThreadId_UsesChannelAsConversation()
    {
        var message = CreateMessage(channelId: "C123", threadId: null);

        var key = Sessions.SessionKeyBuilder.Build(message);

        key.ShouldBe("C123:C123");
    }

    [Test]
    public void Build_WithPlatformName_PrefixesPlatform()
    {
        var message = CreateMessage(channelId: "C123", threadId: "T456");

        var key = Sessions.SessionKeyBuilder.Build(message, "slack");

        key.ShouldBe("slack:C123:T456");
    }

    [Test]
    public void Build_WithPlatformName_WithoutThread_UsesChannel()
    {
        var message = CreateMessage(channelId: "C123", threadId: null);

        var key = Sessions.SessionKeyBuilder.Build(message, "discord");

        key.ShouldBe("discord:C123:C123");
    }

    [Test]
    public void Build_NullMessage_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            Sessions.SessionKeyBuilder.Build(null!));
    }

    [Test]
    public void BuildPerUser_IncludesUserId()
    {
        var message = CreateMessage(channelId: "C123", userId: "U789");

        var key = Sessions.SessionKeyBuilder.BuildPerUser(message);

        key.ShouldBe("C123:U789");
    }

    [Test]
    public void BuildPerUser_WithPlatformName_PrefixesPlatform()
    {
        var message = CreateMessage(channelId: "C123", userId: "U789");

        var key = Sessions.SessionKeyBuilder.BuildPerUser(message, "slack");

        key.ShouldBe("slack:C123:U789");
    }

    [Test]
    public void BuildPerUser_NullMessage_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            Sessions.SessionKeyBuilder.BuildPerUser(null!));
    }

    [Test]
    public void Build_DifferentThreads_DifferentKeys()
    {
        var msg1 = CreateMessage(channelId: "C1", threadId: "T1");
        var msg2 = CreateMessage(channelId: "C1", threadId: "T2");

        Sessions.SessionKeyBuilder.Build(msg1)
            .ShouldNotBe(Sessions.SessionKeyBuilder.Build(msg2));
    }

    [Test]
    public void Build_DifferentPlatforms_DifferentKeys()
    {
        var message = CreateMessage(channelId: "C1", threadId: "T1");

        Sessions.SessionKeyBuilder.Build(message, "slack")
            .ShouldNotBe(Sessions.SessionKeyBuilder.Build(message, "discord"));
    }

    private static PlatformMessage CreateMessage(
        string channelId = "ch1",
        string? threadId = null,
        string userId = "user1") =>
        new()
        {
            ChannelId = channelId,
            ThreadId = threadId,
            UserId = userId,
            Message = AgentMessage.User("test")
        };
}
