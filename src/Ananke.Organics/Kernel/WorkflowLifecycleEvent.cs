namespace Ananke.Organics.Kernel;

/// <summary>Observable events for cell structural changes within a kernel.</summary>
public abstract record WorkflowLifecycleEvent(string WorkflowName, DateTimeOffset Timestamp);

/// <summary>A new cell was spawned (either original or from division).</summary>
/// <param name="WorkflowName">Name of the spawned cell.</param>
/// <param name="Timestamp">When the cell was spawned.</param>
/// <param name="SplitFrom">
/// The parent cell this was divided from, if any. <see langword="null"/> for
/// genesis cells that were not created through division or replication.
/// </param>
public sealed record WorkflowStarted(string WorkflowName, DateTimeOffset Timestamp, string? SplitFrom)
    : WorkflowLifecycleEvent(WorkflowName, Timestamp);

/// <summary>
/// A cell has divided. The cell named in <see cref="WorkflowLifecycleEvent.WorkflowName"/>
/// is now DEAD — it has been replaced by its <see cref="Successors"/>.
/// </summary>
/// <param name="WorkflowName">Name of the cell that divided (now dead).</param>
/// <param name="Timestamp">When the division occurred.</param>
/// <param name="Successors">Names of the peer cells that emerged from this division.</param>
public sealed record WorkflowDivided(string WorkflowName, DateTimeOffset Timestamp, IReadOnlyList<string> Successors)
    : WorkflowLifecycleEvent(WorkflowName, Timestamp);

/// <summary>
/// A cell was cloned — an identical copy was spawned alongside the original,
/// which keeps running. Used for scaling and redundancy, not specialization.
/// </summary>
/// <param name="WorkflowName">Name of the source cell that was cloned.</param>
/// <param name="Timestamp">When the replication occurred.</param>
/// <param name="CloneName">Name of the newly spawned clone.</param>
public sealed record WorkflowReplicated(string WorkflowName, DateTimeOffset Timestamp, string CloneName)
    : WorkflowLifecycleEvent(WorkflowName, Timestamp);

/// <summary>A cell was terminated (after division, or retired due to low usage).</summary>
/// <param name="WorkflowName">Name of the terminated cell.</param>
/// <param name="Timestamp">When the cell was terminated.</param>
/// <param name="Reason">Human-readable reason for termination.</param>
public sealed record WorkflowStopped(string WorkflowName, DateTimeOffset Timestamp, string Reason)
    : WorkflowLifecycleEvent(WorkflowName, Timestamp);
