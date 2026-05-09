namespace Ananke.Organics.Division;

/// <summary>
/// Orchestrates the three-phase drain → switchover → complete handover that
/// prevents in-flight requests from being silently dropped during cell division.
/// </summary>
/// <remarks>
/// <para>
/// The three phases correspond to the division lifecycle in
/// <see cref="WorkflowDivider"/>:
/// </para>
/// <list type="number">
///   <item><see cref="BeginDrainAsync"/> — stop accepting new requests on the
///   parent; wait for in-flight work to complete or the timeout to elapse.</item>
///   <item><see cref="SwitchoverAsync"/> — atomically activate children and
///   update the router's capability map so new requests go to children.</item>
///   <item><see cref="CompleteAsync"/> — release parent resources once children
///   are confirmed alive.</item>
/// </list>
/// <para>
/// Default implementation: <see cref="StopTheWorldDivisionTransition"/>.
/// </para>
/// </remarks>
public interface IDivisionTransition
{
    /// <summary>
    /// Stop the parent cell from accepting new requests and drain in-flight
    /// work. Returns when all in-flight requests have completed or
    /// <paramref name="timeout"/> elapses, whichever comes first.
    /// </summary>
    /// <param name="parentCellId">Name of the cell being divided.</param>
    /// <param name="timeout">Maximum time to wait for in-flight drain.</param>
    /// <param name="ct">Cancellation token.</param>
    Task BeginDrainAsync(string parentCellId, TimeSpan timeout, CancellationToken ct = default);

    /// <summary>
    /// Activate child cells and update the router's capability map atomically.
    /// After this call, new requests must route to children, not the parent.
    /// </summary>
    /// <param name="plan">The approved division plan.</param>
    /// <param name="childCellIds">Names of the newly-spawned child cells.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SwitchoverAsync(DivisionPlan plan, IReadOnlyList<string> childCellIds, CancellationToken ct = default);

    /// <summary>
    /// Confirm that handover is complete and release any resources held during
    /// the transition (e.g. request queues, paused listeners).
    /// </summary>
    /// <param name="parentCellId">Name of the cell being divided.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CompleteAsync(string parentCellId, CancellationToken ct = default);
}
