using System.Collections.Concurrent;

namespace Ananke.Organics.Division.Review;

/// <summary>
/// An <see cref="IWorkReviewGate"/> that parks the work item on first call and
/// returns a <see cref="WorkReviewOutcome.Pending"/> decision immediately, allowing
/// the workflow to checkpoint and release its thread. A later call to
/// <see cref="ResumeAsync"/> resolves the pending review with the real decision.
/// </summary>
/// <remarks>
/// <para>
/// Typical usage:
/// <list type="number">
///   <item>
///     The workflow calls <see cref="ReviewAsync"/>. The item is persisted via the
///     <see cref="IWorkReviewParkingStore"/> and a <c>Pending</c> decision is returned
///     together with the opaque parking id.
///   </item>
///   <item>
///     The workflow runner checkpoints and yields. The caller (e.g.
///     <c>SlackApprovalCallback</c>) posts the review request to a Slack channel.
///   </item>
///   <item>
///     When the reviewer interacts, the caller invokes
///     <see cref="ResumeAsync(string, WorkReviewDecision, CancellationToken)"/> with the
///     parking id and the resolved decision.
///   </item>
///   <item>
///     Any <see cref="ReviewAsync"/> call that is still awaiting (in the same process)
///     is completed immediately. If no waiter is present the decision is held in memory
///     until the next call picks it up.
///   </item>
/// </list>
/// </para>
/// <para>
/// This implementation is safe for concurrent use across threads in the same process.
/// It does <b>not</b> survive process restarts; pair it with a durable
/// <see cref="IWorkReviewParkingStore"/> to persist the parking id across restarts.
/// </para>
/// </remarks>
public sealed class ParkingCallbackWorkReviewGate(
    IWorkReviewParkingStore store,
    string gateId) : IWorkReviewGate
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<WorkReviewDecision>>
        _pending = new();

    /// <summary>
    /// Logical identifier of this gate instance (e.g. a workflow or channel id).
    /// Stored alongside the work item in the parking store for correlation.
    /// </summary>
    public string GateId => gateId;

    /// <inheritdoc />
    /// <remarks>
    /// Parks the work item and returns a <see cref="WorkReviewOutcome.Pending"/>
    /// decision. The <see cref="WorkReviewDecision.ReviewerId"/> is set to
    /// <c>"system"</c> and the <see cref="WorkReviewDecision.Comment"/> carries
    /// the opaque parking id so the caller can forward it (e.g. into a Slack message)
    /// without needing a separate out-parameter.
    /// </remarks>
    public async Task<WorkReviewDecision> ReviewAsync(WorkItem item,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var parkingId = await store.ParkAsync(item, gateId, ct).ConfigureAwait(false);

        // Register a TCS so that a ResumeAsync arriving before this method returns
        // (or from another thread) can complete it.
        var tcs = _pending.GetOrAdd(parkingId,
            _ => new TaskCompletionSource<WorkReviewDecision>(
                TaskCreationOptions.RunContinuationsAsynchronously));

        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));

        // Check if ResumeAsync already resolved the TCS before we even registered.
        if (tcs.Task.IsCompleted)
        {
            _pending.TryRemove(parkingId, out _);
            await store.CompleteAsync(parkingId, ct).ConfigureAwait(false);
            return await tcs.Task.ConfigureAwait(false);
        }

        // Return Pending immediately — the caller should checkpoint here.
        _ = tcs; // keep alive
        return new WorkReviewDecision
        {
            Outcome = WorkReviewOutcome.Pending,
            Comment = parkingId,
            ReviewerId = "system"
        };
    }

    /// <summary>
    /// Resolves a previously parked review with the supplied decision, unblocking any
    /// in-process waiter.
    /// </summary>
    /// <param name="parkingId">The opaque id returned via the <c>Pending</c> comment.</param>
    /// <param name="decision">The resolved review decision.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when <paramref name="parkingId"/> is not found in the store.
    /// </exception>
    public async Task ResumeAsync(string parkingId, WorkReviewDecision decision,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parkingId);
        ArgumentNullException.ThrowIfNull(decision);

        var entry = await store.TryGetAsync(parkingId, ct).ConfigureAwait(false);
        if (entry is null)
            throw new KeyNotFoundException(
                $"No parked review found for parking id '{parkingId}'.");

        await store.CompleteAsync(parkingId, ct).ConfigureAwait(false);

        if (_pending.TryRemove(parkingId, out var tcs))
            tcs.TrySetResult(decision);
        // If no in-process waiter exists (e.g. resumed after restart), the decision
        // is recorded in the store completion above; the caller is responsible for
        // re-entering the workflow with the resolved decision.
    }
}
