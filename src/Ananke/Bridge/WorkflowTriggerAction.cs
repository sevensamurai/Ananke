using Ananke.Orchestration;
using Ananke.Orchestration.Execution;

namespace Ananke.Bridge;

/// <summary>
/// Creates <c>Func&lt;Task&gt;</c> callbacks suitable for FSM <c>OnEnter</c> hooks
/// that start an orchestration workflow. Each invocation runs the workflow
/// with a fresh initial state produced by <paramref name="initialStateFactory"/>.
/// </summary>
/// <typeparam name="TWorkflowState">The orchestration workflow state type.</typeparam>
/// <param name="definition">The compiled workflow definition to execute.</param>
/// <param name="initialStateFactory">Factory that produces the initial workflow state for each run.</param>
/// <param name="runner">The workflow runner that executes the definition.</param>
public sealed class WorkflowTriggerAction<TWorkflowState>(
    WorkflowDefinition<TWorkflowState> definition,
    Func<TWorkflowState> initialStateFactory,
    IWorkflowRunner runner)
{
    private WorkflowExecution<TWorkflowState>? _lastExecution;

    /// <summary>
    /// The most recent workflow execution, or <c>null</c> if no workflow has been triggered yet.
    /// </summary>
    public WorkflowExecution<TWorkflowState>? LastExecution => _lastExecution;

    /// <summary>
    /// Creates a <c>Func&lt;Task&gt;</c> suitable for FSM <c>OnEnter</c> hooks.
    /// Each invocation runs the workflow to completion with a fresh initial state.
    /// </summary>
    public Func<Task> CreateTrigger() => async () =>
    {
        var state = initialStateFactory();
        _lastExecution = await runner.RunAsync(definition, state);
    };

    /// <summary>
    /// Creates a <c>Func&lt;Task&gt;</c> suitable for FSM <c>OnEnter</c> hooks.
    /// Accepts a <see cref="CancellationToken"/> that is captured into the callback.
    /// </summary>
    public Func<Task> CreateTrigger(CancellationToken ct) => async () =>
    {
        var state = initialStateFactory();
        _lastExecution = await runner.RunAsync(definition, state, ct);
    };
}
