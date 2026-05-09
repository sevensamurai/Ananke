using Ananke.Organics.Kernel;

namespace Ananke.Organics.Division;

/// <summary>
/// Default <see cref="IDivisionTransition"/> that pauses the parent cell
/// at the <see cref="IWorkflowHost"/> boundary, waits for in-flight work
/// to drain, and resumes if division is aborted.
/// </summary>
/// <remarks>
/// <para>
/// "Stop the world" means the parent's execution loop is paused via
/// <see cref="IWorkflowHost.PauseAsync"/> during the drain window.
/// New requests routed to the parent during this window will queue at
/// the capability-map level until <see cref="SwitchoverAsync"/> redirects
/// them to children.
/// </para>
/// <para>
/// If division fails after <see cref="BeginDrainAsync"/>, the caller is
/// responsible for calling <see cref="IWorkflowHost.ResumeAsync"/> on the
/// parent so it resumes serving requests. <see cref="WorkflowDivider"/> does
/// this automatically in its catch block.
/// </para>
/// </remarks>
/// <param name="host">The cell host used to pause and resume the parent.</param>
/// <param name="drainTimeout">
/// Reserved for future use by production adapters that poll an in-flight
/// counter. The in-process implementation completes drain immediately after
/// pausing the parent, so this value is currently unused.
/// </param>
public sealed class StopTheWorldDivisionTransition(
    IWorkflowHost host,
    TimeSpan? drainTimeout = null) : IDivisionTransition
{
    // drainTimeout is reserved for future production adapters that poll an
    // in-flight counter; the in-process implementation drains immediately.
    private readonly TimeSpan _reservedDrainTimeout = drainTimeout ?? TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public async Task BeginDrainAsync(string parentCellId, TimeSpan timeout, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentCellId);

        // Pause the parent's loop — no new iteration starts after this.
        await host.PauseAsync(parentCellId).ConfigureAwait(false);

        // Wait for in-flight work to drain. The in-process host has no
        // concurrent execution (each loop iteration runs sequentially), so
        // the pause above is sufficient and drain completes immediately.
        // A future production adapter would poll an in-flight counter here,
        // exiting early as soon as the count reaches zero and using the
        // timeout only as a hard deadline, not as a mandatory sleep.
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task SwitchoverAsync(DivisionPlan plan, IReadOnlyList<string> childCellIds, CancellationToken ct = default)
    {
        // Capability-map updates happen in WorkflowDivider / OrganicHost after
        // children are spawned. This implementation is intentionally a no-op
        // because the host-level pause already stops new requests from being
        // dispatched to the parent, and OrganicHost registers the children
        // immediately after DivideAsync returns.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task CompleteAsync(string parentCellId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentCellId);

        // Parent will be killed by WorkflowDivider.DivideAsync (Step 8).
        // Nothing to release here — the pause is permanent until kill.
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
