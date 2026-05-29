using Ananke.Organics.Division.Review;
using Ananke.Platforms;
using Ananke.Platforms.Slack.Blocks;

namespace Ananke.Roles.Slack;

/// <summary>
/// Bridges Slack block-action and view-submission <see cref="PlatformInteractionEvent"/>s
/// into <see cref="WorkReviewDecision"/>s for a <see cref="CallbackWorkReviewGate"/>.
/// </summary>
/// <remarks>
/// <para>
/// Wire this callback into a <see cref="CallbackWorkReviewGate"/> via DI or constructor
/// injection. When a reviewer clicks an Approve / Revise / Reject button in Slack
/// (rendered by <see cref="SlackApprovalBlocks"/>), call
/// <see cref="HandleInteractionAsync"/> and the gate's pending review will be resolved.
/// </para>
/// <para>
/// The <c>ReviewerId</c> of the resulting <see cref="WorkReviewDecision"/> is taken
/// from <see cref="PlatformInteractionEvent.UserId"/>; the comment defaults to an empty
/// string unless the interaction carries a <see cref="PlatformInteractionEvent.Value"/>.
/// </para>
/// </remarks>
public sealed class SlackApprovalCallback
{
    private readonly Func<WorkReviewDecision, CancellationToken, Task> _onDecision;

    /// <summary>
    /// Initialises the callback with a delegate that is invoked once a decision is
    /// resolved from a Slack interaction.
    /// </summary>
    /// <param name="onDecision">
    /// Async delegate that receives the resolved <see cref="WorkReviewDecision"/> and a
    /// cancellation token. Typically this is the inner callback of a
    /// <see cref="CallbackWorkReviewGate"/>.
    /// </param>
    public SlackApprovalCallback(Func<WorkReviewDecision, CancellationToken, Task> onDecision)
    {
        ArgumentNullException.ThrowIfNull(onDecision);
        _onDecision = onDecision;
    }

    /// <summary>
    /// Attempts to map the given <paramref name="interaction"/> to an approval decision
    /// and invoke the registered <c>onDecision</c> delegate.
    /// </summary>
    /// <param name="interaction">The normalised platform interaction event.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if the interaction matched a known approval action id
    /// and the delegate was invoked; <see langword="false"/> if the action id is
    /// unrecognised (the caller should handle it elsewhere).
    /// </returns>
    public async Task<bool> HandleInteractionAsync(
        PlatformInteractionEvent interaction,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        var actionId = interaction.ActionId;
        var reviewerId = interaction.UserId;
        var comment = interaction.Value ?? string.Empty;

        WorkReviewDecision decision;

        if (actionId == SlackApprovalBlocks.ApproveActionId)
            decision = WorkReviewDecision.Approve(comment, reviewerId);
        else if (actionId == SlackApprovalBlocks.ReviseActionId)
            decision = WorkReviewDecision.Revise(comment, reviewerId);
        else if (actionId == SlackApprovalBlocks.RejectActionId)
            decision = WorkReviewDecision.Reject(comment, reviewerId);
        else
            return false;

        await _onDecision(decision, ct).ConfigureAwait(false);
        return true;
    }
}
