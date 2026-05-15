using Ananke.Abstractions;
using Ananke.Abstractions.Distributed;
using Ananke.StateMachine.Builder;
using Ananke.StateMachine.Middleware;

namespace Ananke.StateMachine.Tests;

// ── Shared enums and types for all state machine tests ───────────

enum Light { Off, On, Blinking }
enum LightAction { TurnOn, TurnOff, Blink, Stabilize }
enum LightNotify { None }

enum DoorState { Locked, Closed, Open }
enum DoorAction { Unlock, Lock, OpenDoor, CloseDoor }
enum DoorNotify { None }

sealed record TestContext(string Id) : IBaseContext;

// ── Minimal concrete state machine for testing ───────────────────
//  Off ──[TurnOn]──► On ──[TurnOff]──► Off
//  On  ──[Blink]──► Blinking ──[Stabilize]──► On

sealed class LightMachine(IDistributedLock locker, IKeyValueDataAdapter store, StateMachineOptions? options = null)
    : AbstractStateMachine<TestContext, Light, LightAction, LightNotify>(
        Light.Off, locker, store, options)
{
    protected override Action<ITransitionBuilder<Light, LightAction>> Transitions => b => b
        .From(Light.Off).On(LightAction.TurnOn).To(Light.On)
        .From(Light.On).On(LightAction.TurnOff).To(Light.Off)
        .From(Light.On).On(LightAction.Blink).To(Light.Blinking)
        .From(Light.Blinking).On(LightAction.Stabilize).To(Light.On);

    public override Task<TransitionResult<Light>> TransitionAsync(
        TestContext context, LightAction transition) =>
        InternalTransitionAsync(context, transition);

    public override Task NotifyAsync(
        TestContext context, LightNotify notification) =>
        Task.CompletedTask;
}

// ── Door machine with guard conditions and actions ───────────────
//  Locked ──[Unlock]──► Closed ──[OpenDoor]──► Open ──[CloseDoor]──► Closed
//  Closed ──[Lock]──► Locked

sealed class DoorMachine : AbstractStateMachine<TestContext, DoorState, DoorAction, DoorNotify>
{
    public bool HasKey { get; set; } = true;
    public List<string> ActionLog { get; } = [];

    public DoorMachine(IDistributedLock locker, IKeyValueDataAdapter store, StateMachineOptions? options = null)
        : base(DoorState.Locked, locker, store, options) { }

    protected override Action<ITransitionBuilder<DoorState, DoorAction>> Transitions => b => b
        .From(DoorState.Locked).On(DoorAction.Unlock).To(DoorState.Closed)
            .When(() => HasKey)
        .From(DoorState.Closed).On(DoorAction.Lock).To(DoorState.Locked)
        .From(DoorState.Closed).On(DoorAction.OpenDoor).To(DoorState.Open)
            .WithAction(async () =>
            {
                await Task.CompletedTask;
                ActionLog.Add("door_opened");
            })
        .From(DoorState.Open).On(DoorAction.CloseDoor).To(DoorState.Closed);

    public override Task<TransitionResult<DoorState>> TransitionAsync(
        TestContext context, DoorAction transition) =>
        InternalTransitionAsync(context, transition);

    public override Task NotifyAsync(
        TestContext context, DoorNotify notification) =>
        Task.CompletedTask;
}

// ── ITimestamped payload helper for tests ────────────────────────
sealed record TimestampedPayload(DateTimeOffset EventTime) : ITimestamped;

