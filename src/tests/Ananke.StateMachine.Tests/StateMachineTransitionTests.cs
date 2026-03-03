using Ananke.Abstractions.Distributed;
using Shouldly;

namespace Ananke.StateMachine.Tests;

[TestFixture]
public class StateMachineTransitionTests
{
    private InMemoryDistributedLock _lock = new();

    [TearDown]
    public ValueTask TearDown() => _lock.DisposeAsync();

    [SetUp]
    public void SetUp() => _lock = new InMemoryDistributedLock();

    // ── Basic transitions ────────────────────────────────────────────

    [Test]
    public async Task Transition_Valid_SucceedsAndUpdatesState()
    {
        var machine = new LightMachine(_lock);
        var ctx = new TestContext(1);

        var result = await machine.TransitionAsync(ctx, LightAction.TurnOn);

        result.Success.ShouldBeTrue();
        result.PreviousState.ShouldBe(Light.Off);
        result.CurrentState.ShouldBe(Light.On);
        machine.CurrentState.ShouldBe(Light.On);
    }

    [Test]
    public async Task Transition_Chain_FollowsCorrectPath()
    {
        var machine = new LightMachine(_lock);
        var ctx = new TestContext(1);

        (await machine.TransitionAsync(ctx, LightAction.TurnOn)).Success.ShouldBeTrue();
        (await machine.TransitionAsync(ctx, LightAction.Blink)).Success.ShouldBeTrue();
        (await machine.TransitionAsync(ctx, LightAction.Stabilize)).Success.ShouldBeTrue();

        machine.CurrentState.ShouldBe(Light.On);
    }

    [Test]
    public async Task Transition_Invalid_FailsWithMessage()
    {
        var options = new StateMachineOptions { AllowImplicitSelfTransitions = false };
        var machine = new LightMachine(_lock, options);
        var ctx = new TestContext(1);

        // Can't turn off when already off (no explicit transition defined)
        var result = await machine.TransitionAsync(ctx, LightAction.TurnOff);

        result.Success.ShouldBeFalse();
        result.ErrorMessage!.ShouldContain("Invalid transition");
        result.CurrentState.ShouldBe(Light.Off);
    }

    [Test]
    public async Task Transition_Invalid_StateUnchanged()
    {
        var options = new StateMachineOptions { AllowImplicitSelfTransitions = false };
        var machine = new LightMachine(_lock, options);
        var ctx = new TestContext(1);

        await machine.TransitionAsync(ctx, LightAction.TurnOff); // invalid

        machine.CurrentState.ShouldBe(Light.Off);
    }

    // ── Self-transitions ─────────────────────────────────────────────

    [Test]
    public async Task Transition_ImplicitSelfTransition_AllowedByDefault()
    {
        var machine = new LightMachine(_lock);
        var ctx = new TestContext(1);

        // TurnOn from Off is valid (not a self-transition, just testing setup)
        await machine.TransitionAsync(ctx, LightAction.TurnOn);

        // TurnOn from On: not explicitly defined, but implicit self-transition is allowed
        var result = await machine.TransitionAsync(ctx, LightAction.TurnOn);

        result.Success.ShouldBeTrue();
        result.IsSelfTransition.ShouldBeTrue();
        result.CurrentState.ShouldBe(Light.On);
    }

    [Test]
    public async Task Transition_ImplicitSelfTransition_DisabledByOption()
    {
        var options = new StateMachineOptions { AllowImplicitSelfTransitions = false };
        var machine = new LightMachine(_lock, options);
        var ctx = new TestContext(1);

        await machine.TransitionAsync(ctx, LightAction.TurnOn);

        // Self-transition not allowed
        var result = await machine.TransitionAsync(ctx, LightAction.TurnOn);

        result.Success.ShouldBeFalse();
        result.ErrorMessage!.ShouldContain("Invalid transition");
    }

    // ── Guard conditions

    [Test]
    public async Task Transition_GuardPasses_Succeeds()
    {
        var machine = new DoorMachine(_lock) { HasKey = true };
        var ctx = new TestContext(1);

        var result = await machine.TransitionAsync(ctx, DoorAction.Unlock);

        result.Success.ShouldBeTrue();
        result.CurrentState.ShouldBe(DoorState.Closed);
    }

    [Test]
    public async Task Transition_GuardFails_BlocksTransition()
    {
        var machine = new DoorMachine(_lock) { HasKey = false };
        var ctx = new TestContext(1);

        var result = await machine.TransitionAsync(ctx, DoorAction.Unlock);

        result.Success.ShouldBeFalse();
        result.ErrorMessage!.ShouldContain("guard");
        result.CurrentState.ShouldBe(DoorState.Locked);
    }

    // ── After-transition actions ─────────────────────────────────────

    [Test]
    public async Task Transition_WithAction_ActionExecuted()
    {
        var machine = new DoorMachine(_lock);
        var ctx = new TestContext(1);

        await machine.TransitionAsync(ctx, DoorAction.Unlock);
        await machine.TransitionAsync(ctx, DoorAction.OpenDoor);

        machine.ActionLog.ShouldContain("door_opened");
    }

    [Test]
    public async Task Transition_WithAction_CorrectFinalState()
    {
        // This validates the WithAction(Func<Task>) closure bug fix:
        // the action must return the target state defined at builder time.
        var machine = new DoorMachine(_lock);
        var ctx = new TestContext(1);

        await machine.TransitionAsync(ctx, DoorAction.Unlock);
        var result = await machine.TransitionAsync(ctx, DoorAction.OpenDoor);

        result.Success.ShouldBeTrue();
        result.CurrentState.ShouldBe(DoorState.Open);
    }

    // ── Persisted state across transitions ───────────────────────────

    [Test]
    public async Task Transition_PersistedState_SurvivesMultipleTransitions()
    {
        var machine = new LightMachine(_lock);
        var ctx = new TestContext(42);

        await machine.TransitionAsync(ctx, LightAction.TurnOn);
        await machine.TransitionAsync(ctx, LightAction.Blink);

        // Verify the persisted context has the correct state
        var persisted = await machine.GetPersistedContextAsync(42);
        persisted.State.ShouldBe(Light.Blinking);
        persisted.Step.ShouldBe(2);
    }

    // ── Multiple contexts ────────────────────────────────────────────

    [Test]
    public async Task Transition_DifferentContexts_IndependentState()
    {
        var machine = new LightMachine(_lock);
        var ctx1 = new TestContext(1);
        var ctx2 = new TestContext(2);

        await machine.TransitionAsync(ctx1, LightAction.TurnOn);
        // ctx2 should still be at initial state
        var persisted2 = await machine.GetPersistedContextAsync(2);
        persisted2.State.ShouldBe(Light.Off);
        persisted2.Step.ShouldBe(0);
    }

    // ── InitialState property ────────────────────────────────────────

    [Test]
    public void InitialState_ReturnsConstructorValue()
    {
        var machine = new LightMachine(_lock);

        machine.InitialState.ShouldBe(Light.Off);
    }

    // ── TransitionResult static factories ────────────────────────────

    [Test]
    public void TransitionResult_Succeeded_SetsFields()
    {
        var result = TransitionResult<Light>.Succeeded(Light.Off, Light.On);

        result.Success.ShouldBeTrue();
        result.PreviousState.ShouldBe(Light.Off);
        result.CurrentState.ShouldBe(Light.On);
        result.IsSelfTransition.ShouldBeFalse();
        result.ErrorMessage.ShouldBeNull();
        result.Exception.ShouldBeNull();
    }

    [Test]
    public void TransitionResult_Failed_SetsFields()
    {
        var ex = new InvalidOperationException("test");
        var result = TransitionResult<Light>.Failed(Light.On, "msg", ex);

        result.Success.ShouldBeFalse();
        result.PreviousState.ShouldBe(Light.On);
        result.CurrentState.ShouldBe(Light.On);
        result.ErrorMessage.ShouldBe("msg");
        result.Exception.ShouldBe(ex);
    }

    [Test]
    public void TransitionResult_LockFailed_SetsFields()
    {
        var result = TransitionResult<Light>.LockFailed(Light.On);

        result.Success.ShouldBeFalse();
        result.ErrorMessage!.ShouldContain("lock");
    }

    [Test]
    public void TransitionResult_InvalidTransition_SetsFields()
    {
        var result = TransitionResult<Light>.InvalidTransition(Light.Off, "TurnOff");

        result.Success.ShouldBeFalse();
        result.ErrorMessage!.ShouldContain("TurnOff");
        result.ErrorMessage!.ShouldContain("Off");
    }

    [Test]
    public void TransitionResult_GuardFailed_SetsFields()
    {
        var result = TransitionResult<Light>.GuardFailed(Light.On, "custom reason");

        result.Success.ShouldBeFalse();
        result.ErrorMessage!.ShouldBe("custom reason");
    }

    [Test]
    public void TransitionResult_GuardFailed_DefaultReason()
    {
        var result = TransitionResult<Light>.GuardFailed(Light.On);

        result.ErrorMessage!.ShouldContain("guard");
    }

    [Test]
    public void TransitionResult_IsSelfTransition_TrueWhenSameState()
    {
        var result = TransitionResult<Light>.Succeeded(Light.On, Light.On);

        result.IsSelfTransition.ShouldBeTrue();
    }
}
