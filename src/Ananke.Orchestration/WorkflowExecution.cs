using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Jobs;

namespace Ananke.Orchestration;

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

    /// <summary>Ordered record of every job that has been executed in this run.</summary>
    public IReadOnlyList<JobExecution> History => _history;

    internal WorkflowExecution(string workflowName, TState initialState,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Id = Guid.NewGuid().ToString("N");
        WorkflowName = workflowName;
        State = initialState;
        Metadata = metadata ?? new Dictionary<string, string>();
    }

    private WorkflowExecution(string id, string workflowName, TState state,
        List<JobExecution> history, IReadOnlyDictionary<string, string>? metadata = null)
    {
        Id = id;
        WorkflowName = workflowName;
        State = state;
        _history = history;
        Metadata = metadata ?? new Dictionary<string, string>();
    }

    internal static WorkflowExecution<TState> FromCheckpoint(Checkpoint<TState> checkpoint) =>
        new(checkpoint.ExecutionId, checkpoint.WorkflowName, checkpoint.State,
            [.. checkpoint.History], checkpoint.Metadata)
        {
            CurrentJob = checkpoint.CurrentJob,
            Status = checkpoint.Status
        };

    internal void RecordJobExecution(JobExecution execution) =>
        _history.Add(execution);
}
