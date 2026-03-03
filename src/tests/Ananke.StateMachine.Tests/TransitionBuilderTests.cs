using Ananke.StateMachine.Builder;
using Shouldly;

namespace Ananke.StateMachine.Tests;

[TestFixture]
public class TransitionBuilderTests
{
    // ── Basic fluent builder ─────────────────────────────────────────

    [Test]
    public void Build_SingleTransition_RegisteredCorrectly()
    {
        var builder = new TransitionBuilder<Light, LightAction>();
        builder.From(Light.Off).On(LightAction.TurnOn).To(Light.On);
        builder.Build();

        builder.Transitions.Count.ShouldBe(1);
        var key = TransitionBuilder<Light, LightAction>.GetKey(Light.Off, LightAction.TurnOn);
        builder.Transitions.ShouldContainKey(key);

        var config = builder.Transitions[key];
        config.InitialState.ShouldBe(Light.Off);
        config.Transition.ShouldBe(LightAction.TurnOn);
        config.FinalState.ShouldBe(Light.On);
    }

    [Test]
    public void Build_MultipleTransitions_AllRegistered()
    {
        var builder = new TransitionBuilder<Light, LightAction>();
        builder
            .From(Light.Off).On(LightAction.TurnOn).To(Light.On)
            .From(Light.On).On(LightAction.TurnOff).To(Light.Off)
            .From(Light.On).On(LightAction.Blink).To(Light.Blinking);
        builder.Build();

        builder.Transitions.Count.ShouldBe(3);
    }

    // ── FromAny ──────────────────────────────────────────────────────

    [Test]
    public void Build_FromAny_RegistersTransitionForEachState()
    {
        var builder = new TransitionBuilder<Light, LightAction>();
        builder.FromAny(Light.On, Light.Blinking).On(LightAction.TurnOff).To(Light.Off);
        builder.Build();

        builder.Transitions.Count.ShouldBe(2);

        var key1 = TransitionBuilder<Light, LightAction>.GetKey(Light.On, LightAction.TurnOff);
        var key2 = TransitionBuilder<Light, LightAction>.GetKey(Light.Blinking, LightAction.TurnOff);
        builder.Transitions.ShouldContainKey(key1);
        builder.Transitions.ShouldContainKey(key2);
    }

    // ── Guard conditions ─────────────────────────────────────────────

    [Test]
    public void Build_WithGuard_GuardConditionStored()
    {
        var builder = new TransitionBuilder<Light, LightAction>();
        builder.From(Light.Off).On(LightAction.TurnOn).To(Light.On)
            .When(() => true);
        builder.Build();

        var key = TransitionBuilder<Light, LightAction>.GetKey(Light.Off, LightAction.TurnOn);
        builder.Transitions[key].GuardCondition.ShouldNotBeNull();
    }

    [Test]
    public void Build_WithAsyncGuard_GuardConditionStored()
    {
        var builder = new TransitionBuilder<Light, LightAction>();
        builder.From(Light.Off).On(LightAction.TurnOn).To(Light.On)
            .WhenAsync(() => Task.FromResult(true));
        builder.Build();

        var key = TransitionBuilder<Light, LightAction>.GetKey(Light.Off, LightAction.TurnOn);
        builder.Transitions[key].GuardCondition.ShouldNotBeNull();
    }

    // ── WithAction ───────────────────────────────────────────────────

    [Test]
    public void Build_WithAction_ActionStored()
    {
        var builder = new TransitionBuilder<Light, LightAction>();
        builder.From(Light.Off).On(LightAction.TurnOn).To(Light.On)
            .WithAction(() => Task.CompletedTask);
        builder.Build();

        var key = TransitionBuilder<Light, LightAction>.GetKey(Light.Off, LightAction.TurnOn);
        builder.Transitions[key].AfterTransitionAction.ShouldNotBeNull();
    }

    [Test]
    public async Task Build_WithAction_FuncTask_ReturnsCapturedTargetState()
    {
        // This tests the closure bug fix: the action must return the target
        // state that was active at definition time, not the builder's field
        // value at invocation time (which would be default/reset).
        var builder = new TransitionBuilder<Light, LightAction>();
        builder
            .From(Light.Off).On(LightAction.TurnOn).To(Light.On)
                .WithAction(() => Task.CompletedTask)
            .From(Light.On).On(LightAction.Blink).To(Light.Blinking);
        builder.Build();

        var key = TransitionBuilder<Light, LightAction>.GetKey(Light.Off, LightAction.TurnOn);
        var action = builder.Transitions[key].AfterTransitionAction!;
        var result = await action();

        // Must be Light.On (the target state at definition time), not Light.Off (default)
        result.ShouldBe(Light.On);
    }

    [Test]
    public void Build_WithActionReturningState_ActionStored()
    {
        var builder = new TransitionBuilder<Light, LightAction>();
        builder.From(Light.Off).On(LightAction.TurnOn).To(Light.On)
            .WithAction(() => Task.FromResult(Light.Blinking));
        builder.Build();

        var key = TransitionBuilder<Light, LightAction>.GetKey(Light.Off, LightAction.TurnOn);
        builder.Transitions[key].AfterTransitionAction.ShouldNotBeNull();
    }

    // ── State entry/exit actions ─────────────────────────────────────

    [Test]
    public void Build_StateOnEnter_ActionStored()
    {
        var builder = new TransitionBuilder<Light, LightAction>();
        builder
            .State(Light.On).OnEnter(() => Task.CompletedTask);
        builder.Build();

        builder.StateConfigs.ShouldContainKey(Light.On);
        builder.StateConfigs[Light.On].OnEnterAction.ShouldNotBeNull();
    }

    [Test]
    public void Build_StateOnExit_ActionStored()
    {
        var builder = new TransitionBuilder<Light, LightAction>();
        builder
            .State(Light.On).OnExit(() => Task.CompletedTask);
        builder.Build();

        builder.StateConfigs[Light.On].OnExitAction.ShouldNotBeNull();
    }

    [Test]
    public void Build_StateOnEnterAndOnExit_BothStored()
    {
        var builder = new TransitionBuilder<Light, LightAction>();
        builder
            .State(Light.On)
                .OnEnter(() => Task.CompletedTask)
                .OnExit(() => Task.CompletedTask);
        builder.Build();

        builder.StateConfigs[Light.On].OnEnterAction.ShouldNotBeNull();
        builder.StateConfigs[Light.On].OnExitAction.ShouldNotBeNull();
    }

    // ── Duplicate transitions ────────────────────────────────────────

    [Test]
    public void Build_DuplicateTransition_FirstWins()
    {
        var builder = new TransitionBuilder<Light, LightAction>();
        builder
            .From(Light.Off).On(LightAction.TurnOn).To(Light.On)
            .From(Light.Off).On(LightAction.TurnOn).To(Light.Blinking);
        builder.Build();

        var key = TransitionBuilder<Light, LightAction>.GetKey(Light.Off, LightAction.TurnOn);
        builder.Transitions[key].FinalState.ShouldBe(Light.On);
    }

    // ── GetKey ────────────────────────────────────────────────────────

    [Test]
    public void GetKey_ProducesDeterministicKey()
    {
        var key1 = TransitionBuilder<Light, LightAction>.GetKey(Light.Off, LightAction.TurnOn);
        var key2 = TransitionBuilder<Light, LightAction>.GetKey(Light.Off, LightAction.TurnOn);

        key1.ShouldBe(key2);
        key1.ShouldContain("Off");
        key1.ShouldContain("TurnOn");
    }

    [Test]
    public void GetKey_DifferentTransitions_DifferentKeys()
    {
        var key1 = TransitionBuilder<Light, LightAction>.GetKey(Light.Off, LightAction.TurnOn);
        var key2 = TransitionBuilder<Light, LightAction>.GetKey(Light.On, LightAction.TurnOff);

        key1.ShouldNotBe(key2);
    }
}
