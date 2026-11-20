using Ananke.Abstractions.Distributed;
using Ananke.StateMachine;
using Ananke.StateMachine.Builder;

namespace StateMachineDemo;

// -- State machine --------------------------------------------------------
sealed class CarEngineStateMachine(
    IDistributedLock locker,
    IKeyValueDataAdapter store,
    StateMachineOptions? options = null)
    : AbstractStateMachine<CarContext, EngineState, EngineTransition, EngineNotification>(
        EngineState.Parked, locker, store, options)
{
    protected override Action<ITransitionBuilder<EngineState, EngineTransition>> Transitions => b => b
        .From(EngineState.Parked)
            .On(EngineTransition.Start).To(EngineState.Running)
        .From(EngineState.Running)
            .On(EngineTransition.Drive).To(EngineState.Moving)
                .When((car, _) => ((CarContext)car).FuelLevel > 0)
        .From(EngineState.Moving)
            .On(EngineTransition.Halt).To(EngineState.Idle)
        .From(EngineState.Idle)
            .On(EngineTransition.Resume).To(EngineState.Running)
        .From(EngineState.Idle)
            .On(EngineTransition.Park).To(EngineState.Parked)
        // Lifecycle hooks
        .State(EngineState.Running)
            .OnEnter(async (_, _) => Console.WriteLine("    ?? [OnEnter] Running — engine started"))
            .OnExit(async (_, _) => Console.WriteLine("    ?? [OnExit]  Running — engine state changing"))
        .State(EngineState.Moving)
            .OnEnter(async (_, _) => Console.WriteLine("    ?? [OnEnter] Moving — trip segment started"))
            .OnExit(async (_, _) => Console.WriteLine("    ?? [OnExit]  Moving — trip segment ended"));

    public override Task<TransitionResult<EngineState>> TransitionAsync(
        CarContext ctx, EngineTransition t, CancellationToken ct = default) =>
        InternalTransitionAsync(ctx, t, ct);

    public override Task NotifyAsync(CarContext ctx, EngineNotification n, CancellationToken ct = default) =>
        Task.CompletedTask;
}

