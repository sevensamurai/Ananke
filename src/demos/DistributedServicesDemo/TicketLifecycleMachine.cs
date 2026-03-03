using Ananke.Abstractions;
using Ananke.Abstractions.Distributed;
using Ananke.StateMachine;
using Ananke.StateMachine.Builder;

// ═══════════════════════════════════════════════════════════════════
// Ticket Lifecycle FSM — tracked via Bridge convenience layer
// ═══════════════════════════════════════════════════════════════════
//
//  New ──[BeginTriage]──► Triaging ──[Resolve]──► Resolved ──[Close]──► Closed
//
//  Each transition is fired by a .StateMachineJob() step in the workflow.
//  The Bridge extension infers all generic type parameters — compare
//  with the raw StateMachineTriggerJob<TWorkflowState, TContext, TState,
//  TTransition, TNotification> constructor that requires 5 type arguments.

enum LifecycleState { New, Triaging, Resolved, Closed }
enum LifecycleAction { BeginTriage, Resolve, Close }
enum LifecycleNotification { None }

sealed record TicketLifecycleContext(long Id) : IBaseContext
{
    public string? Command { get; set; }
}

sealed class TicketLifecycleMachine(IDistributedLock locker)
    : AbstractStateMachine<TicketLifecycleContext, LifecycleState, LifecycleAction, LifecycleNotification>(
        LifecycleState.New, locker)
{
    protected override Action<ITransitionBuilder<LifecycleState, LifecycleAction>> Transitions => b => b
        .From(LifecycleState.New).On(LifecycleAction.BeginTriage).To(LifecycleState.Triaging)
        .From(LifecycleState.Triaging).On(LifecycleAction.Resolve).To(LifecycleState.Resolved)
        .From(LifecycleState.Resolved).On(LifecycleAction.Close).To(LifecycleState.Closed);

    public override Task<TransitionResult<LifecycleState>> TransitionAsync(
        TicketLifecycleContext ctx, LifecycleAction t) =>
        InternalTransitionAsync(ctx, t);

    public override Task NotifyAsync(
        TicketLifecycleContext ctx, LifecycleNotification n) =>
        Task.CompletedTask;
}
