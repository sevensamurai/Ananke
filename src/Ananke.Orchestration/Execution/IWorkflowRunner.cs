using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Streaming;

namespace Ananke.Orchestration.Execution;

/// <summary>
/// Executes and resumes <see cref="WorkflowDefinition{TState}"/> instances.
/// The default implementation is <see cref="WorkflowRunner"/>.
/// </summary>
public interface IWorkflowRunner
{
    /// <summary>Starts a new workflow execution from the entry job.</summary>
    Task<WorkflowExecution<TState>> RunAsync<TState>(
        WorkflowDefinition<TState> definition,
        TState initialState,
        CancellationToken ct = default);

    /// <summary>
    /// Resumes a previously checkpointed execution from the job that follows
    /// <see cref="Checkpoint{TState}.CurrentJob"/>.
    /// </summary>
    Task<WorkflowExecution<TState>> ResumeAsync<TState>(
        WorkflowDefinition<TState> definition,
        Checkpoint<TState> checkpoint,
        CancellationToken ct = default);

    /// <summary>
    /// Resumes a previously checkpointed execution, applying <paramref name="stateTransform"/>
    /// to the checkpointed state before continuing. Used for human-in-the-loop scenarios.
    /// </summary>
    Task<WorkflowExecution<TState>> ResumeAsync<TState>(
        WorkflowDefinition<TState> definition,
        Checkpoint<TState> checkpoint,
        Func<TState, TState> stateTransform,
        CancellationToken ct = default);

    /// <summary>
    /// Starts a new workflow execution and streams orchestration progress events
    /// as an <see cref="IAsyncEnumerable{T}"/>. Events include <see cref="JobStarted{TState}"/>,
    /// <see cref="JobCompleted{TState}"/>, <see cref="ForkStarted{TState}"/>,
    /// <see cref="BranchFaulted{TState}"/>, <see cref="JoinCompleted{TState}"/>, and terminal
    /// events (<see cref="WorkflowCompleted{TState}"/> or <see cref="WorkflowFaulted{TState}"/>).
    /// </summary>
    /// <remarks>
    /// The internal channel provides back-pressure: when the buffer is full, the runner
    /// blocks until the consumer reads. Configure via <paramref name="options"/>.
    /// </remarks>
    IAsyncEnumerable<WorkflowEvent<TState>> StreamAsync<TState>(
        WorkflowDefinition<TState> definition,
        TState initialState,
        WorkflowStreamOptions? options = null,
        CancellationToken ct = default);
}
