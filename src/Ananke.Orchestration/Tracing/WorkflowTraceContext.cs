using Ananke.Abstractions.Tracing;

namespace Ananke.Orchestration.Tracing;

/// <summary>
/// Ambient execution context that flows through async operations.
/// Set by <see cref="Execution.WorkflowRunner"/> before each job runs,
/// read by <see cref="Agents.AgentJob{TState, TResponse}"/> when building requests.
/// </summary>
public static class WorkflowTraceContext
{
    private static readonly AsyncLocal<TraceInfo?> Current = new();

    public static TraceInfo? Value
    {
        get => Current.Value;
        internal set => Current.Value = value;
    }
}

public sealed record TraceInfo(
    string WorkflowName,
    string ExecutionId,
    string? CurrentJob = null,
    ITrace? Trace = null,
    ISpan? CurrentSpan = null,
    bool StoreCompletions = false);
