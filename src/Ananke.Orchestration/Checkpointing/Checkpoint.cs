using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Jobs;

namespace Ananke.Orchestration.Checkpointing;

/// <summary>
/// Snapshot of a workflow execution captured after each successful job, enabling resume semantics.
/// Created automatically by <see cref="Execution.WorkflowRunner"/> when a checkpoint store is configured.
/// </summary>
public record Checkpoint<TState>
{
    /// <summary>The unique identifier of the originating execution.</summary>
    public required string ExecutionId { get; init; }

    /// <summary>The name of the workflow this checkpoint belongs to.</summary>
    public required string WorkflowName { get; init; }

    /// <summary>The name of the last successfully completed job at the time this checkpoint was created.</summary>
    public required string CurrentJob { get; init; }

    /// <summary>The workflow state after <see cref="CurrentJob"/> completed.</summary>
    public required TState State { get; init; }

    /// <summary>The execution status at checkpoint time.</summary>
    public required ExecutionStatus Status { get; init; }

    /// <summary>Job execution history up to and including <see cref="CurrentJob"/>.</summary>
    public required List<JobExecution> History { get; init; }

    /// <summary>UTC timestamp when this checkpoint was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// UTC timestamp after which this checkpoint is considered expired and may be deleted.
    /// Defaults to <see cref="DateTimeOffset.MaxValue"/> (never expires).
    /// </summary>
    public DateTimeOffset ExpiresAt { get; init; } = DateTimeOffset.MaxValue;

    /// <summary>
    /// Workflow-level metadata snapshot copied from <see cref="WorkflowExecution{TState}.Metadata"/>
    /// at the time of checkpointing. Restored on resume.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// When non-null, indicates the checkpoint was created by an <see cref="Jobs.InterruptMode.Before"/>
    /// interrupt. Resume should start at this job (rather than resolving the next job from
    /// <see cref="CurrentJob"/>).
    /// </summary>
    public string? InterruptedBeforeJob { get; init; }

    /// <summary>
    /// Active loop iteration counters at the time of checkpointing, keyed by the loop
    /// source job name. Restored on resume so loop iteration caps survive interrupts.
    /// </summary>
    public IReadOnlyDictionary<string, int> LoopCounters { get; init; } = new Dictionary<string, int>();

    internal static Checkpoint<TState> Create(WorkflowExecution<TState> execution, TimeSpan? ttl = null) => new()
    {
        ExecutionId = execution.Id,
        WorkflowName = execution.WorkflowName,
        CurrentJob = execution.CurrentJob ?? string.Empty,
        State = execution.State,
        Status = execution.Status,
        History = [.. execution.History],
        Metadata = execution.Metadata,
        LoopCounters = new Dictionary<string, int>(execution.LoopCounters),
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow + (ttl ?? TimeSpan.FromDays(7))
    };

    internal static Checkpoint<TState> CreateInterrupt(
        WorkflowExecution<TState> execution,
        string interruptedBeforeJob,
        TimeSpan? ttl = null) => new()
    {
        ExecutionId = execution.Id,
        WorkflowName = execution.WorkflowName,
        CurrentJob = execution.CurrentJob ?? string.Empty,
        State = execution.State,
        Status = ExecutionStatus.Interrupted,
        History = [.. execution.History],
        Metadata = execution.Metadata,
        LoopCounters = new Dictionary<string, int>(execution.LoopCounters),
        InterruptedBeforeJob = interruptedBeforeJob,
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow + (ttl ?? TimeSpan.FromDays(7))
    };
}
