using Ananke.Orchestration.Workflows;
using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Streaming;

/// <summary>
/// Base type for workflow-level progress events emitted by
/// <see cref="Execution.IWorkflowRunner.StreamAsync{TState}"/> and
/// <see cref="Workflow{TState}.StreamAsync"/>.
/// Use pattern matching to handle specific event types.
/// </summary>
public abstract record WorkflowEvent<TState>
{
    /// <summary>Name of the workflow that produced this event.</summary>
    public required string WorkflowName { get; init; }

    /// <summary>Unique execution identifier.</summary>
    public required string ExecutionId { get; init; }

    /// <summary>UTC timestamp when the event was created.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The fork branch this event came from, identified by the branch's start-job name.
    /// <c>null</c> for events on the main path.
    /// </summary>
    /// <remarks>
    /// A discriminator on the existing event types rather than a parallel branch-specific
    /// hierarchy: consumers already switch on <see cref="JobStarted{TState}"/> and
    /// <see cref="JobCompleted{TState}"/>, and doubling that would double their handling.
    /// The start-job name is what fork spans already use, so it is not a new concept.
    /// </remarks>
    public string? Branch { get; init; }
}

/// <summary>Emitted when a job is about to execute.</summary>
public sealed record JobStarted<TState> : WorkflowEvent<TState>
{
    /// <summary>Name of the job that is starting.</summary>
    public required string JobName { get; init; }
}

/// <summary>Emitted after a job completes successfully.</summary>
public sealed record JobCompleted<TState> : WorkflowEvent<TState>
{
    /// <summary>Name of the completed job.</summary>
    public required string JobName { get; init; }

    /// <summary>Wall-clock duration of the job execution.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>Workflow state after the job completed.</summary>
    public required TState State { get; init; }
}

/// <summary>Emitted whenever the workflow state is updated (after job completion or join merge).</summary>
public sealed record StateUpdated<TState> : WorkflowEvent<TState>
{
    /// <summary>The updated workflow state.</summary>
    public required TState State { get; init; }
}

/// <summary>Emitted when the workflow is interrupted at an interrupt point.</summary>
public sealed record Interrupted<TState> : WorkflowEvent<TState>
{
    /// <summary>Name of the job at the interrupt point.</summary>
    public required string JobName { get; init; }

    /// <summary>Workflow state at the time of interruption.</summary>
    public required TState State { get; init; }
}

/// <summary>Emitted when parallel branches begin execution.</summary>
public sealed record ForkStarted<TState> : WorkflowEvent<TState>
{
    /// <summary>Names of the target jobs being forked to.</summary>
    public required IReadOnlyList<string> Targets { get; init; }
}

/// <summary>
/// Emitted for each forked branch that did not succeed, before the join is evaluated.
/// </summary>
/// <remarks>
/// Under <see cref="Routing.ForkMode.BestEffort"/> the workflow continues after this event —
/// it reports a branch that was dropped from the merge, not a terminal failure.
/// </remarks>
public sealed record BranchFaulted<TState> : WorkflowEvent<TState>
{
    /// <summary>The outcome of the branch that did not succeed.</summary>
    public required Routing.BranchOutcome Outcome { get; init; }
}

/// <summary>Emitted when parallel branches have been merged back together.</summary>
public sealed record JoinCompleted<TState> : WorkflowEvent<TState>
{
    /// <summary>Name of the target job after the join.</summary>
    public required string Target { get; init; }

    /// <summary>Merged workflow state.</summary>
    public required TState State { get; init; }
}

/// <summary>Emitted when the workflow completes successfully.</summary>
public sealed record WorkflowCompleted<TState> : WorkflowEvent<TState>
{
    /// <summary>Final workflow result.</summary>
    public required WorkflowResult<TState> Result { get; init; }
}

/// <summary>Emitted when the workflow fails with an unhandled exception.</summary>
public sealed record WorkflowFaulted<TState> : WorkflowEvent<TState>
{
    /// <summary>The exception that caused the fault.</summary>
    public required Exception Exception { get; init; }

    /// <summary>Workflow state at the time of failure.</summary>
    public required TState State { get; init; }
}

/// <summary>Emitted when a loop terminates, either because the condition was met or the iteration cap was reached.</summary>
public sealed record LoopExited<TState> : WorkflowEvent<TState>
{
    /// <summary>The job whose loop connection was evaluated (the loop source).</summary>
    public required string LoopFrom { get; init; }

    /// <summary>The job the loop would have cycled back to.</summary>
    public required string LoopTarget { get; init; }

    /// <summary>Number of iterations completed before the loop exited.</summary>
    public required int IterationsCompleted { get; init; }

    /// <summary>Why the loop terminated.</summary>
    public required Routing.LoopExitReason Reason { get; init; }
}

/// <summary>
/// Emitted once per execution when estimated cost first crosses
/// <see cref="Budget.BudgetConfig.WarnAtCost"/>. The run continues.
/// </summary>
public sealed record BudgetWarning<TState> : WorkflowEvent<TState>
{
    /// <summary>Estimated cost at the time the warning threshold was crossed.</summary>
    public required decimal EstimatedCost { get; init; }

    /// <summary>The configured warning threshold.</summary>
    public required decimal WarnAtCost { get; init; }

    /// <summary>The configured hard limit the run is still heading toward.</summary>
    public required decimal Budget { get; init; }

    /// <summary>Cumulative token usage across all LLM calls in this execution.</summary>
    public required TokenUsage CumulativeUsage { get; init; }
}

/// <summary>Emitted when the workflow reached its cost budget and was stopped.</summary>
public sealed record BudgetExceeded<TState> : WorkflowEvent<TState>
{
    /// <summary>Estimated cost at the time the budget was exceeded.</summary>
    public required decimal EstimatedCost { get; init; }

    /// <summary>The configured budget limit.</summary>
    public required decimal Budget { get; init; }

    /// <summary>Cumulative token usage across all LLM calls in this execution.</summary>
    public required TokenUsage CumulativeUsage { get; init; }
}
