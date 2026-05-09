using Ananke.Orchestration.Jobs;

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

    internal static WorkflowResult<TState> Succeeded(
        TState finalState,
        TimeSpan duration,
        IReadOnlyList<JobExecution> history) => new()
    {
        Success = true,
        FinalState = finalState,
        TotalDuration = duration,
        JobsExecuted = history.Count,
        History = history
    };

    internal static WorkflowResult<TState> Failed(
        TState currentState,
        TimeSpan duration,
        IReadOnlyList<JobExecution> history,
        string error,
        Exception? exception = null) => new()
    {
        Success = false,
        FinalState = currentState,
        TotalDuration = duration,
        JobsExecuted = history.Count,
        History = history,
        Error = error,
        Exception = exception
    };

    internal static WorkflowResult<TState> Cancelled(
        TState currentState,
        TimeSpan duration,
        IReadOnlyList<JobExecution> history) => new()
    {
        Success = false,
        FinalState = currentState,
        TotalDuration = duration,
        JobsExecuted = history.Count,
        History = history,
        Error = "Workflow cancelled."
    };
}
