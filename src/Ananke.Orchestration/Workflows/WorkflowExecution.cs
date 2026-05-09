using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Jobs;

namespace Ananke.Orchestration.Workflows;

/// <summary>
/// Mutable runtime context of a running or finished workflow execution.
/// Returned by <see cref="Execution.IWorkflowRunner.RunAsync{TState}"/> and
/// <see cref="Execution.IWorkflowRunner.ResumeAsync{TState}(WorkflowDefinition{TState}, Checkpointing.Checkpoint{TState}, CancellationToken)"/>.
/// </summary>
public sealed class WorkflowExecution<TState>
{
    /// <summary>Unique identifier for this execution, used to load checkpoints on resume.</summary>
    public string Id { get; }

    /// <summary>The name of the workflow being executed.</summary>
    public string WorkflowName { get; }

    /// <summary>Current lifecycle status.</summary>
    public ExecutionStatus Status { get; internal set; } = ExecutionStatus.NotStarted;

    /// <summary>Name of the job currently being executed, or <see langword="null"/> when finished.</summary>
    public string? CurrentJob { get; internal set; }

    /// <summary>The most recent workflow state.</summary>
    public TState State { get; internal set; }

    /// <summary>Final result populated once the execution reaches a terminal status.</summary>
    public WorkflowResult<TState>? Result { get; internal set; }

    /// <summary>
    /// Workflow-level key/value metadata attached via
    /// <see cref="Workflow{TState}.WithMetadata"/>. Empty when no metadata was configured.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    private readonly List<JobExecution> _history = [];
    private readonly Dictionary<string, int> _loopCounters = [];

    /// <summary>Ordered record of every job that has been executed in this run.</summary>
    public IReadOnlyList<JobExecution> History => _history;

    /// <summary>Current iteration counts for active loops, keyed by the loop source job name.</summary>
    internal IReadOnlyDictionary<string, int> LoopCounters => _loopCounters;

    /// <summary>Cumulative token usage across all LLM calls in this execution.</summary>
    public TokenUsage CumulativeUsage { get; internal set; } = TokenUsage.Zero;

    /// <summary>Estimated cumulative cost in the unit defined by the workflow's cost model.</summary>
    public decimal EstimatedCost { get; internal set; }

    /// <summary>
    /// Returns <see langword="true"/> when the workflow completed successfully
    /// (<see cref="Status"/> is <see cref="ExecutionStatus.Completed"/>).
    /// </summary>
    public bool IsSuccess => Status == ExecutionStatus.Completed;

    /// <summary>
    /// Returns <see langword="true"/> when the workflow terminated due to a fault, cancellation,
    /// or budget exhaustion.
    /// </summary>
    public bool IsFailure => Status is ExecutionStatus.Faulted or ExecutionStatus.Cancelled
        or ExecutionStatus.BudgetExceeded;

    /// <summary>
    /// Converts this execution into an immutable <see cref="WorkflowResult{TState}"/>.
    /// Unlike <see cref="Result"/> (which is <see langword="null"/> while running),
    /// this method synthesises a result from current state at any point in the lifecycle.
    /// </summary>
    public WorkflowResult<TState> ToResult() =>
        Result ?? new WorkflowResult<TState>
        {
            Success = IsSuccess,
            FinalState = State,
            TotalDuration = TimeSpan.Zero,
            JobsExecuted = _history.Count,
            History = _history,
            Error = Status switch
            {
                ExecutionStatus.Running => "Workflow is still running.",
                ExecutionStatus.NotStarted => "Workflow has not started.",
                ExecutionStatus.Interrupted => "Workflow is paused at an interrupt point.",
                _ => null
            }
        };

    internal WorkflowExecution(string workflowName, TState initialState,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Id = Guid.NewGuid().ToString("N");
        WorkflowName = workflowName;
        State = initialState;
        Metadata = metadata ?? new Dictionary<string, string>();
    }

    private WorkflowExecution(string id, string workflowName, TState state,
        List<JobExecution> history, IReadOnlyDictionary<string, string>? metadata = null,
        Dictionary<string, int>? loopCounters = null)
    {
        Id = id;
        WorkflowName = workflowName;
        State = state;
        _history = history;
        Metadata = metadata ?? new Dictionary<string, string>();
        if (loopCounters is not null)
            _loopCounters = loopCounters;
    }

    internal static WorkflowExecution<TState> FromCheckpoint(Checkpoint<TState> checkpoint) =>
        new(checkpoint.ExecutionId, checkpoint.WorkflowName, checkpoint.State,
            [.. checkpoint.History], checkpoint.Metadata,
            checkpoint.LoopCounters is { Count: > 0 }
                ? new Dictionary<string, int>(checkpoint.LoopCounters)
                : null)
        {
            CurrentJob = checkpoint.CurrentJob,
            Status = checkpoint.Status
        };

    /// <summary>Increments the loop counter for <paramref name="loopSource"/> and returns the new value.</summary>
    internal int IncrementLoopCounter(string loopSource)
    {
        _loopCounters.TryGetValue(loopSource, out var count);
        count++;
        _loopCounters[loopSource] = count;
        return count;
    }

    /// <summary>Resets the loop counter for <paramref name="loopSource"/> when the loop exits.</summary>
    internal void ResetLoopCounter(string loopSource) =>
        _loopCounters.Remove(loopSource);

    internal void RecordJobExecution(JobExecution execution) =>
        _history.Add(execution);
}
