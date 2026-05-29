namespace Ananke.Organics.Division.Review;

/// <summary>
/// Generic <see cref="IWorkReviewGate"/> backed by an async callback.
/// </summary>
/// <param name="callback">
/// Async function that receives the work item and cancellation token, and returns a review decision.
/// </param>
public sealed class CallbackWorkReviewGate(
    Func<WorkItem, CancellationToken, Task<WorkReviewDecision>> callback) : IWorkReviewGate
{
    /// <inheritdoc />
    public Task<WorkReviewDecision> ReviewAsync(WorkItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        return callback(item, ct);
    }
}
