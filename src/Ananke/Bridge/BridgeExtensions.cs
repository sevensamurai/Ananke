using Ananke.Abstractions;
using Ananke.Orchestration;
using Ananke.Orchestration.Execution;
using Ananke.StateMachine;
using Ananke.StateMachine.Builder;

namespace Ananke.Bridge;

/// <summary>
/// Convenience extensions that simplify wiring between
/// <c>Ananke.StateMachine</c> and <c>Ananke.Orchestration</c>.
/// Each method wraps the underlying Bridge primitives
/// (<see cref="WorkflowTriggerAction{TWorkflowState}"/>,
///  <see cref="StateMachineTriggerJob{TWorkflowState,TContext,TState,TTransition,TNotification}"/>,
///  <see cref="WorkflowCompletionTrigger{TWorkflowState,TContext,TState,TTransition,TNotification}"/>)
/// and lets the compiler infer all generic type parameters from the arguments.
/// </summary>
public static class BridgeExtensions
{
    // ── Pattern A: FSM state entered → start a workflow ──────────────

    /// <summary>
    /// Registers a workflow as an <c>OnEnter</c> action for the current state.
    /// Each time the FSM enters this state, a fresh workflow execution starts
    /// using the supplied <paramref name="initialStateFactory"/>.
    /// </summary>
    /// <returns>
    /// The builder, so further <c>.OnExit()</c> or <c>.From()</c> calls can be chained.
    /// </returns>
    public static IStateConfigBuilder<S, T> OnEnterRunWorkflow<S, T, TWorkflowState>(
        this IStateConfigBuilder<S, T> builder,
        WorkflowDefinition<TWorkflowState> definition,
        Func<TWorkflowState> initialStateFactory,
        IWorkflowRunner runner)
        where S : Enum
        where T : Enum
    {
        var trigger = new WorkflowTriggerAction<TWorkflowState>(definition, initialStateFactory, runner);
        return builder.OnEnter(trigger.CreateTrigger());
    }

    /// <inheritdoc cref="OnEnterRunWorkflow{S,T,TWorkflowState}(IStateConfigBuilder{S,T},WorkflowDefinition{TWorkflowState},Func{TWorkflowState},IWorkflowRunner)"/>
    /// <param name="builder">The state configuration builder.</param>
    /// <param name="definition">The compiled workflow definition to execute.</param>
    /// <param name="initialStateFactory">Factory that produces fresh initial workflow state.</param>
    /// <param name="runner">The workflow runner.</param>
    /// <param name="triggerOut">
    /// Receives the <see cref="WorkflowTriggerAction{TWorkflowState}"/> so callers
    /// can inspect <see cref="WorkflowTriggerAction{TWorkflowState}.LastExecution"/> later.
    /// </param>
    public static IStateConfigBuilder<S, T> OnEnterRunWorkflow<S, T, TWorkflowState>(
        this IStateConfigBuilder<S, T> builder,
        WorkflowDefinition<TWorkflowState> definition,
        Func<TWorkflowState> initialStateFactory,
        IWorkflowRunner runner,
        out WorkflowTriggerAction<TWorkflowState> triggerOut)
        where S : Enum
        where T : Enum
    {
        triggerOut = new WorkflowTriggerAction<TWorkflowState>(definition, initialStateFactory, runner);
        return builder.OnEnter(triggerOut.CreateTrigger());
    }

    // ── Pattern B: Workflow step → fire FSM transition ───────────────

    /// <summary>
    /// Adds a job to the workflow that fires an FSM transition when reached.
    /// All five generic type parameters are inferred from the arguments.
    /// </summary>
    /// <example>
    /// <code>
    /// new Workflow&lt;OrderState&gt;("process-order")
    ///     .Job("validate", ...)
    ///     .StateMachineJob("update-status", orderMachine,
    ///         s =&gt; new OrderContext(s.OrderId),
    ///         s =&gt; OrderTransition.MarkValidated,
    ///         (s, r) =&gt; s with { MachineState = r.CurrentState.ToString() })
    ///     .Job("ship", ...)
    /// </code>
    /// </example>
    public static Workflow<TWorkflowState> StateMachineJob<TWorkflowState, TContext, TState, TTransition, TNotification>(
        this Workflow<TWorkflowState> workflow,
        string name,
        IActionStateMachine<TContext, TState, TTransition, TNotification> stateMachine,
        Func<TWorkflowState, TContext> contextSelector,
        Func<TWorkflowState, TTransition> transitionSelector,
        Func<TWorkflowState, TransitionResult<TState>, TWorkflowState>? resultMapper = null)
        where TContext : IBaseContext
        where TState : Enum
        where TTransition : Enum
        where TNotification : Enum
    {
        var job = new StateMachineTriggerJob<TWorkflowState, TContext, TState, TTransition, TNotification>(
            name, stateMachine, contextSelector, transitionSelector, resultMapper);
        return workflow.Job(name, job);
    }

    // ── Pattern C: Run workflow → fire FSM on completion ─────────────

    /// <summary>
    /// Runs a workflow to completion and then fires an FSM transition
    /// determined by the <paramref name="transitionSelector"/>.
    /// Combines <see cref="IWorkflowRunner.RunAsync{TState}"/> with
    /// <see cref="WorkflowCompletionTrigger{TWorkflowState,TContext,TState,TTransition,TNotification}"/>
    /// in a single awaitable call with full type inference.
    /// </summary>
    /// <example>
    /// <code>
    /// var execution = await orderMachine.RunWorkflowAsync(
    ///     new OrderContext(42),
    ///     orderWorkflowDef,
    ///     new OrderState { OrderId = 42 },
    ///     runner,
    ///     result =&gt; result.Success ? OrderTransition.Complete : OrderTransition.Fail);
    /// </code>
    /// </example>
    public static Task<WorkflowExecution<TWorkflowState>> RunWorkflowAsync
        <TWorkflowState, TContext, TState, TTransition, TNotification>(
        this IActionStateMachine<TContext, TState, TTransition, TNotification> stateMachine,
        TContext fsmContext,
        WorkflowDefinition<TWorkflowState> definition,
        TWorkflowState initialState,
        IWorkflowRunner runner,
        Func<WorkflowResult<TWorkflowState>, TTransition> transitionSelector,
        CancellationToken ct = default)
        where TContext : IBaseContext
        where TState : Enum
        where TTransition : Enum
        where TNotification : Enum
    {
        var trigger = new WorkflowCompletionTrigger<TWorkflowState, TContext, TState, TTransition, TNotification>(
            stateMachine, fsmContext, transitionSelector);
        return trigger.RunAsync(definition, initialState, runner, ct);
    }
}
