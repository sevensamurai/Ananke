using Ananke.Platforms;
using Ananke.Platforms.Slack;
using Shouldly;
using SlackNet;
using SlackNet.Events;

namespace Ananke.Platforms.Slack.Tests;

[TestFixture]
public sealed class SlackAssistantMapperTests
{
    // ── AssistantThreadStarted ───────────────────────────────────────────────

    [Test]
    public void FromAssistantThreadStarted_MapsKindAndIdentifiers()
    {
        var slackEvent = new AssistantThreadStarted
        {
            AssistantThread = new AssistantThread
            {
                UserId = "U010",
                ChannelId = "C020",
                ThreadTs = "1234567890.000100"
            }
        };

        var result = SlackMessageMapper.FromAssistantThreadStarted(slackEvent);

        result.Kind.ShouldBe(AssistantThreadEventKind.Started);
        result.UserId.ShouldBe("U010");
        result.ChannelId.ShouldBe("C020");
        result.ThreadId.ShouldBe("1234567890.000100");
        result.SourceContext.ShouldBeNull();
        result.PlatformContext.ShouldBeSameAs(slackEvent);
    }

    [Test]
    public void FromAssistantThreadStarted_NullUserId_YieldsUnknown()
    {
        var slackEvent = new AssistantThreadStarted
        {
            AssistantThread = new AssistantThread { UserId = null!, ChannelId = "C021", ThreadTs = "1.0" }
        };

        var result = SlackMessageMapper.FromAssistantThreadStarted(slackEvent);

        result.UserId.ShouldBe("unknown");
    }

    // ── AssistantThreadContextChanged ────────────────────────────────────────

    [Test]
    public void FromAssistantThreadContextChanged_MapsKindAndSourceContext()
    {
        var slackEvent = new AssistantThreadContextChanged
        {
            AssistantThread = new AssistantThread
            {
                UserId = "U011",
                ChannelId = "C030",
                ThreadTs = "1234567890.000200",
                Context = new AssistantThreadContext
                {
                    ChannelId = "C099",
                    TeamId = "T001",
                    EnterpriseId = string.Empty   // empty → omitted
                }
            }
        };

        var result = SlackMessageMapper.FromAssistantThreadContextChanged(slackEvent);

        result.Kind.ShouldBe(AssistantThreadEventKind.ContextChanged);
        result.UserId.ShouldBe("U011");
        result.ChannelId.ShouldBe("C030");
        result.ThreadId.ShouldBe("1234567890.000200");
        result.SourceContext.ShouldNotBeNull();
        result.SourceContext!["channel_id"].ShouldBe("C099");
        result.SourceContext["team_id"].ShouldBe("T001");
        result.SourceContext.ContainsKey("enterprise_id").ShouldBeFalse();
    }

    [Test]
    public void FromAssistantThreadContextChanged_NullContext_YieldsNullSourceContext()
    {
        var slackEvent = new AssistantThreadContextChanged
        {
            AssistantThread = new AssistantThread
            {
                UserId = "U012",
                ChannelId = "C031",
                ThreadTs = "1.0",
                Context = null!
            }
        };

        var result = SlackMessageMapper.FromAssistantThreadContextChanged(slackEvent);

        result.SourceContext.ShouldBeNull();
    }
}
