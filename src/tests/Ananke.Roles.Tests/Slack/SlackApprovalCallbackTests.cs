using Ananke.Organics.Division.Review;
using Ananke.Platforms;
using Ananke.Roles.Slack;
using Ananke.Platforms.Slack.Blocks;
using Shouldly;

namespace Ananke.Roles.Tests.Slack;

[TestFixture]
public sealed class SlackApprovalCallbackTests
{
    private static PlatformInteractionEvent MakeBlockAction(string actionId, string userId,
        string? value = null) => new()
        {
            Kind = PlatformInteractionKind.BlockAction,
            ActionId = actionId,
            UserId = userId,
            Value = value
        };

    [Test]
    public async Task HandleInteractionAsync_ApproveAction_InvokesOnDecisionWithApprovedOutcome()
    {
        WorkReviewDecision? captured = null;
        var callback = new SlackApprovalCallback((d, _) => { captured = d; return Task.CompletedTask; });

        var result = await callback.HandleInteractionAsync(
            MakeBlockAction(SlackApprovalBlocks.ApproveActionId, "U1", "looks good"));

        result.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured!.Outcome.ShouldBe(WorkReviewOutcome.Approved);
        captured.ReviewerId.ShouldBe("U1");
        captured.Comment.ShouldBe("looks good");
    }

    [Test]
    public async Task HandleInteractionAsync_ReviseAction_InvokesOnDecisionWithRevisedOutcome()
    {
        WorkReviewDecision? captured = null;
        var callback = new SlackApprovalCallback((d, _) => { captured = d; return Task.CompletedTask; });

        var result = await callback.HandleInteractionAsync(
            MakeBlockAction(SlackApprovalBlocks.ReviseActionId, "U2", "please fix section 3"));

        result.ShouldBeTrue();
        captured!.Outcome.ShouldBe(WorkReviewOutcome.Revised);
        captured.ReviewerId.ShouldBe("U2");
    }

    [Test]
    public async Task HandleInteractionAsync_RejectAction_InvokesOnDecisionWithRejectedOutcome()
    {
        WorkReviewDecision? captured = null;
        var callback = new SlackApprovalCallback((d, _) => { captured = d; return Task.CompletedTask; });

        var result = await callback.HandleInteractionAsync(
            MakeBlockAction(SlackApprovalBlocks.RejectActionId, "U3"));

        result.ShouldBeTrue();
        captured!.Outcome.ShouldBe(WorkReviewOutcome.Rejected);
        captured.ReviewerId.ShouldBe("U3");
        captured.Comment.ShouldBe(string.Empty);
    }

    [Test]
    public async Task HandleInteractionAsync_UnknownActionId_ReturnsFalseAndDoesNotInvokeDelegate()
    {
        var invoked = false;
        var callback = new SlackApprovalCallback((_, _) => { invoked = true; return Task.CompletedTask; });

        var result = await callback.HandleInteractionAsync(
            MakeBlockAction("some_other_action", "U4"));

        result.ShouldBeFalse();
        invoked.ShouldBeFalse();
    }

    [Test]
    public async Task HandleInteractionAsync_NullActionId_ReturnsFalse()
    {
        var callback = new SlackApprovalCallback((_, _) => Task.CompletedTask);

        var interaction = new PlatformInteractionEvent
        {
            Kind = PlatformInteractionKind.BlockAction,
            ActionId = null,
            UserId = "U5"
        };

        var result = await callback.HandleInteractionAsync(interaction);

        result.ShouldBeFalse();
    }

    [Test]
    public async Task HandleInteractionAsync_PassesCancellationTokenToDelegate()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken received = default;
        var callback = new SlackApprovalCallback((_, ct) => { received = ct; return Task.CompletedTask; });

        await callback.HandleInteractionAsync(
            MakeBlockAction(SlackApprovalBlocks.ApproveActionId, "U6"), cts.Token);

        received.ShouldBe(cts.Token);
    }
}
