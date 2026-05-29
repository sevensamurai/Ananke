namespace Ananke.Organics.Division.Review;

/// <summary>
/// Default in-memory <see cref="IWorkReviewGate"/> that approves every work item.
/// </summary>
public sealed class AutoWorkReviewGate(string reviewerId = "auto-review") : IWorkReviewGate
{
    /// <inheritdoc />
    public Task<WorkReviewDecision> ReviewAsync(WorkItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(WorkReviewDecision.Approve(
            comment: "Auto-approved (no work review gate configured)",
            reviewerId: reviewerId));
    }
}
