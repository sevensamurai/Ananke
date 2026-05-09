namespace Ananke.Orchestration.Workflows;

/// <summary>Represents the lifecycle state of a workflow execution.</summary>
public enum ExecutionStatus
{
    /// <summary>The execution has not yet been started by a runner.</summary>
    NotStarted,

    /// <summary>The execution is actively processing jobs.</summary>
    Running,

    /// <summary>All jobs completed successfully.</summary>
    Completed,

    /// <summary>A job or the runner itself threw an unhandled exception.</summary>
    Faulted,

    /// <summary>
    /// Execution was stopped in response to a <see cref="System.Threading.CancellationToken"/>.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Execution paused at an interrupt point, awaiting human input before resuming.
    /// </summary>
    Interrupted,

    /// <summary>
    /// The workflow was terminated because its cost budget was exceeded.
    /// </summary>
    BudgetExceeded
}
