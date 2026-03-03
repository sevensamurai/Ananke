using Ananke.Abstractions;
using Ananke.Orchestration;
using Ananke.Orchestration.Execution;
using Ananke.StateMachine;

namespace Ananke.Bridge;

/// <summary>
/// Runs an orchestration workflow and fires an FSM transition when the workflow completes.
/// The <paramref name="transitionSelector"/> maps the <see cref="WorkflowResult{TState}"/>
/// to the FSM transition to fire, allowing success/failure to route to different states.
/// </summary>
/// <typeparam name="TWorkflowState">The orchestration workflow state type.</typeparam>
/// <typeparam name="TContext">FSM context type implementing <see cref="IBaseContext"/>.</typeparam>
/// <typeparam name="TState">FSM state enum type.</typeparam>
/// <typeparam name="TTransition">FSM transition enum type.</typeparam>
/// <typeparam name="TNotification">FSM notification enum type.</typeparam>
/// <param name="stateMachine">The state machine to transition on workflow completion.</param>
/// <param name="fsmContext">The FSM context identifying the state machine instance.</param>
/// <param name="transitionSelector">
/// Maps the workflow result to an FSM transition. Called for both successful
/// and failed workflows, allowing the caller to route each outcome to a different FSM state.
/// </param>
public sealed class WorkflowCompletionTrigger<TWorkflowState, TContext, TState, TTransition, TNotification>(
    IActionStateMachine<TContext, TState, TTransition, TNotification> stateMachine,
    TContext fsmContext,
    Func<WorkflowResult<TWorkflowState>, TTransition> transitionSelector)
    where TContext : IBaseContext
    where TState : Enum
    where TTransition : Enum
    where TNotification : Enum
{
    /// <summary>
    /// Runs the workflow and fires the mapped FSM transition when it completes.
    /// </summary>
    /// <param name="definition">The compiled workflow definition to execute.</param>
    /// <param name="initialState">Initial workflow state.</param>
    /// <param name="runner">The workflow runner.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The workflow execution with its final result.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the FSM transition fails after workflow completion.
    /// </exception>
    public async Task<WorkflowExecution<TWorkflowState>> RunAsync(
        WorkflowDefinition<TWorkflowState> definition,
        TWorkflowState initialState,
        IWorkflowRunner runner,
        CancellationToken ct = default)
    {
        var execution = await runner.RunAsync(definition, initialState, ct);

        if (execution.Result is not null)
        {
            var transition = transitionSelector(execution.Result);
            var result = await stateMachine.TransitionAsync(fsmContext, transition);

            if (!result.Success)
                throw new InvalidOperationException(
                    $"Completion transition '{transition}' failed: {result.ErrorMessage}");
        }

        return execution;
    }
}
