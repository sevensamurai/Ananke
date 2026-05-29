namespace Ananke.Organics.Division.Review;

/// <summary>
/// Stores serialised pending review state so that a review can outlive the process
/// that initiated it and be resumed later — for example, after a Slack reviewer
/// clicks Approve many hours later.
/// </summary>
/// <remarks>
/// Implementations must be safe for concurrent access. The in-process default is
/// <see cref="InMemoryWorkReviewParkingStore"/>; a Redis-backed counterpart is planned
/// for a later release.
/// </remarks>
public interface IWorkReviewParkingStore
{
    /// <summary>
    /// Persists a pending review and returns an opaque parking id that the caller
    /// uses to resume the review later.
    /// </summary>
    /// <param name="item">The work item submitted for review.</param>
    /// <param name="gateId">
    /// Logical identifier of the gate that parked the review (e.g. a channel id or
    /// workflow name). Used to correlate resumes with the correct gate instance.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An opaque, globally unique parking id.</returns>
    Task<string> ParkAsync(WorkItem item, string gateId, CancellationToken ct = default);

    /// <summary>
    /// Attempts to retrieve a parked review by its parking id.
    /// </summary>
    /// <param name="parkingId">The id returned by <see cref="ParkAsync"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A tuple of the parked <see cref="WorkItem"/> and the gate id it was parked under,
    /// or <see langword="null"/> if no entry exists for <paramref name="parkingId"/>.
    /// </returns>
    Task<(WorkItem Item, string GateId)?> TryGetAsync(string parkingId,
        CancellationToken ct = default);

    /// <summary>
    /// Removes a parked review entry once the decision has been applied.
    /// Calling this method for an unknown id is a no-op.
    /// </summary>
    /// <param name="parkingId">The id of the entry to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CompleteAsync(string parkingId, CancellationToken ct = default);
}
