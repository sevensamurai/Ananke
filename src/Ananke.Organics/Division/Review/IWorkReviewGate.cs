namespace Ananke.Organics.Division.Review;

/// <summary>
/// Reviews a work item and returns a decision.
/// </summary>
public interface IWorkReviewGate
{
    /// <summary>
    /// Reviews the supplied work item.
    /// </summary>
    /// <param name="item">The work item under review.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<WorkReviewDecision> ReviewAsync(WorkItem item, CancellationToken ct = default);
}
