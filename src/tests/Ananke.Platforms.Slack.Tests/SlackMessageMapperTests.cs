using Ananke.Platforms;
using Ananke.Platforms.Slack;
using Shouldly;
using SlackNet;
using SlackNet.Blocks;
using SlackNet.Interaction;

namespace Ananke.Platforms.Slack.Tests;

[TestFixture]
public sealed class SlackMessageMapperTests
{
    // ── Slash commands ──────────────────────────────────────────────────────

    [Test]
    public void FromSlashCommand_MapsCommandAndText()
    {
        var cmd = new SlashCommand
        {
            Command = "/ask",
            Text = "  what time is it?  ",
            UserId = "U001",
            TriggerId = "trigger-123"
        };

        var result = SlackMessageMapper.FromSlashCommand(cmd);

        result.Command.ShouldBe("/ask");
        result.Text.ShouldBe("what time is it?");
        result.UserId.ShouldBe("U001");
        result.TriggerId.ShouldBe("trigger-123");
        result.PlatformContext.ShouldBeSameAs(cmd);
    }

    [Test]
    public void FromSlashCommand_NullText_YieldsEmptyString()
    {
        var cmd = new SlashCommand { Command = "/ping", UserId = "U002", Text = null };

        var result = SlackMessageMapper.FromSlashCommand(cmd);

        result.Text.ShouldBeEmpty();
    }

    [Test]
    public void FromSlashCommand_NullUserId_YieldsUnknown()
    {
        var cmd = new SlashCommand { Command = "/x", UserId = null! };

        var result = SlackMessageMapper.FromSlashCommand(cmd);

        result.UserId.ShouldBe("unknown");
    }

    // ── Block actions ───────────────────────────────────────────────────────

    [Test]
    public void FromBlockActionRequest_MapsKindAndActionId()
    {
        var action = new PlainTextInputAction { ActionId = "input-submit" };
        var request = new BlockActionRequest
        {
            Actions = [action],
            User = new User { Id = "U003" },
            TriggerId = "trig-456",
#pragma warning disable CS0618 // Channel obsolete; required by BlockActionRequest property type
            Channel = new Channel { Id = "C001" }
#pragma warning restore CS0618
        };

        var result = SlackMessageMapper.FromBlockActionRequest(request);

        result.Kind.ShouldBe(PlatformInteractionKind.BlockAction);
        result.ActionId.ShouldBe("input-submit");
        result.UserId.ShouldBe("U003");
        result.TriggerId.ShouldBe("trig-456");
        result.ChannelId.ShouldBe("C001");
        result.PlatformContext.ShouldBeSameAs(request);
    }

    [Test]
    public void FromBlockActionRequest_EmptyActions_YieldsNullActionId()
    {
        var request = new BlockActionRequest
        {
            Actions = [],
            User = new User { Id = "U004" }
        };

        var result = SlackMessageMapper.FromBlockActionRequest(request);

        result.ActionId.ShouldBeNull();
    }

    // ── View submissions ────────────────────────────────────────────────────

    [Test]
    public void FromViewSubmission_MapsKindAndCallbackId()
    {
        var view = new HomeViewInfo { CallbackId = "my-modal" };
        var submission = new ViewSubmission
        {
            View = view,
            User = new User { Id = "U005" }
        };

        var result = SlackMessageMapper.FromViewSubmission(submission, submission);

        result.Kind.ShouldBe(PlatformInteractionKind.ViewSubmission);
        result.ActionId.ShouldBe("my-modal");
        result.UserId.ShouldBe("U005");
        result.PlatformContext.ShouldBeSameAs(submission);
    }

    // ── View closed ─────────────────────────────────────────────────────────

    [Test]
    public void FromViewClosed_MapsKindAndCallbackId()
    {
        var view = new HomeViewInfo { CallbackId = "dismiss-modal" };
        var closed = new ViewClosed
        {
            View = view,
            User = new User { Id = "U006" }
        };

        var result = SlackMessageMapper.FromViewClosed(closed, closed);

        result.Kind.ShouldBe(PlatformInteractionKind.ViewClosed);
        result.ActionId.ShouldBe("dismiss-modal");
        result.UserId.ShouldBe("U006");
    }
}
