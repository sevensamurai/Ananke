using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Routing;

namespace Ananke.Orchestration.Workflows;

/// <summary>
/// Immutable summary of a finished workflow execution.
/// Inspect <see cref="Success"/> to branch on outcome, and <see cref="History"/> for per-job timings.
/// </summary>
public record WorkflowResult<TState>
{
    public required bool Success { get; init; }
    public required TState FinalState { get; init; }
    public required TimeSpan TotalDuration { get; init; }
    public required int JobsExecuted { get; init; }
    public required IReadOnlyList<JobExecution> History { get; init; }
    public string? Error { get; init; }
    public Exception? Exception { get; init; }

    /// <summary>
    /// Outcomes for forked branches that did <b>not</b> succeed. Empty for workflows with no fork,
    /// and for forks where every branch succeeded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Under <see cref="ForkMode.BestEffort"/> a run can be <see cref="Success"/> <c>== true</c>
    /// while this is non-empty: opting into that mode declares a partial result acceptable, so the
    /// runner reports what was dropped rather than overriding the caller's choice. A caller that
    /// wants to escalate a partial fork to a failure checks this collection and decides.
    /// </para>
    /// </remarks>
    public IReadOnlyList<BranchOutcome> BranchOutcomes { get; init; } = [];

    internal static WorkflowResult<TState> Succeeded(
        TState finalState,
        TimeSpan duration,
        IReadOnlyList<JobExecution> history,
        IReadOnlyList<BranchOutcome>? branchOutcomes = null) => new()
        {
            Success = true,
            FinalState = finalState,
            TotalDuration = duration,
            JobsExecuted = history.Count,
            History = history,
            BranchOutcomes = branchOutcomes ?? []
        };

    internal static WorkflowResult<TState> Failed(
        TState currentState,
        TimeSpan duration,
        IReadOnlyList<JobExecution> history,
        string error,
        Exception? exception = null,
        IReadOnlyList<BranchOutcome>? branchOutcomes = null) => new()
        {
            Success = false,
            FinalState = currentState,
            TotalDuration = duration,
            JobsExecuted = history.Count,
            History = history,
            Error = error,
            Exception = exception,
            BranchOutcomes = branchOutcomes ?? []
        };

    internal static WorkflowResult<TState> Cancelled(
        TState currentState,
        TimeSpan duration,
        IReadOnlyList<JobExecution> history,
        IReadOnlyList<BranchOutcome>? branchOutcomes = null) => new()
        {
            Success = false,
            FinalState = currentState,
            TotalDuration = duration,
            JobsExecuted = history.Count,
            History = history,
            Error = "Workflow cancelled.",
            BranchOutcomes = branchOutcomes ?? []
        };
}
