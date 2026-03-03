using Ananke.Abstractions;
using Ananke.Orchestration.Jobs;
using Ananke.StateMachine;

namespace Ananke.Bridge;

/// <summary>
/// An orchestration job that fires a state machine transition when executed.
/// Maps workflow state to an FSM context + transition, executes the transition,
/// and optionally maps the <see cref="TransitionResult{S}"/> back into the workflow state.
/// </summary>
/// <typeparam name="TWorkflowState">The orchestration workflow state type.</typeparam>
/// <typeparam name="TContext">FSM context type implementing <see cref="IBaseContext"/>.</typeparam>
/// <typeparam name="TState">FSM state enum type.</typeparam>
/// <typeparam name="TTransition">FSM transition enum type.</typeparam>
/// <typeparam name="TNotification">FSM notification enum type.</typeparam>
/// <param name="name">Display name of this job (appears in traces, logs, and history).</param>
/// <param name="stateMachine">The state machine instance to transition.</param>
/// <param name="contextSelector">Maps workflow state to the FSM context for the transition.</param>
/// <param name="transitionSelector">Maps workflow state to the FSM transition to fire.</param>
/// <param name="resultMapper">
/// Optional mapper that incorporates the <see cref="TransitionResult{S}"/> into
/// the workflow state. When <c>null</c>, the workflow state passes through unchanged.
/// </param>
public sealed class StateMachineTriggerJob<TWorkflowState, TContext, TState, TTransition, TNotification>(
    string name,
    IActionStateMachine<TContext, TState, TTransition, TNotification> stateMachine,
    Func<TWorkflowState, TContext> contextSelector,
    Func<TWorkflowState, TTransition> transitionSelector,
    Func<TWorkflowState, TransitionResult<TState>, TWorkflowState>? resultMapper = null)
    : IJob<TWorkflowState>
    where TContext : IBaseContext
    where TState : Enum
    where TTransition : Enum
    where TNotification : Enum
{
    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public async Task<TWorkflowState> ExecuteAsync(TWorkflowState state, CancellationToken ct = default)
    {
        var context = contextSelector(state);
        var transition = transitionSelector(state);
        var result = await stateMachine.TransitionAsync(context, transition);

        if (!result.Success)
            throw new InvalidOperationException(
                $"State machine transition '{transition}' failed: {result.ErrorMessage}");

        return resultMapper is not null ? resultMapper(state, result) : state;
    }
}
