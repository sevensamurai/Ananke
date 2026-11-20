using Ananke.Abstractions.Distributed;
using Ananke.StateMachine.Builder;
using Shouldly;

namespace Ananke.StateMachine.Tests;

[TestFixture]
public class StateEntryExitActionTests
{
    private InMemoryDistributedLock _lock = new();

    [TearDown]
    public ValueTask TearDown() => _lock.DisposeAsync();

    [SetUp]
    public void SetUp() => _lock = new InMemoryDistributedLock();

    [Test]
    public async Task Transition_FiresExitAction_OnPreviousState()
    {
        var events = new List<string>();
        var machine = new EntryExitMachine(_lock, _lock, events);
        var ctx = new TestContext("1");

        await machine.TransitionAsync(ctx, LightAction.TurnOn);

        events.ShouldContain("exit:Off");
    }

    [Test]
    public async Task Transition_FiresEntryAction_OnNewState()
    {
        var events = new List<string>();
        var machine = new EntryExitMachine(_lock, _lock, events);
        var ctx = new TestContext("1");

        await machine.TransitionAsync(ctx, LightAction.TurnOn);

        events.ShouldContain("enter:On");
    }

    [Test]
    public async Task Transition_ExitBeforeEntry_CorrectOrder()
    {
        var events = new List<string>();
        var machine = new EntryExitMachine(_lock, _lock, events);
        var ctx = new TestContext("1");

        await machine.TransitionAsync(ctx, LightAction.TurnOn);

        var exitIdx = events.IndexOf("exit:Off");
        var enterIdx = events.IndexOf("enter:On");
        exitIdx.ShouldBeLessThan(enterIdx);
    }

    // ── An action that throws: the result says whether the state was stored ──

    [Test]
    public async Task Transition_EnterActionThrows_FailsAndSaysTheStateMoved()
    {
        var machine = new HookedLightMachine(_lock, _lock)
        {
            EnterOn = () => throw new InvalidOperationException("lamp driver")
        };
        var ctx = new TestContext("1");

        var result = await machine.TransitionAsync(ctx, LightAction.TurnOn);

        result.Success.ShouldBeFalse();
        result.StateStored.ShouldBeTrue();
        result.PreviousState.ShouldBe(Light.Off);
        result.CurrentState.ShouldBe(Light.On);
        result.Exception.ShouldBeOfType<InvalidOperationException>();
        result.ErrorMessage!.ShouldContain("lamp driver");
        (await machine.GetStatusAsync(ctx)).State.ShouldBe(Light.On);
    }

    [Test]
    public async Task Transition_AfterTransitionActionThrows_FailsAndSaysTheStateMoved()
    {
        var machine = new HookedLightMachine(_lock, _lock)
        {
            AfterBlink = () => throw new InvalidOperationException("blinker")
        };
        var ctx = new TestContext("1");
        (await machine.TransitionAsync(ctx, LightAction.TurnOn)).Success.ShouldBeTrue();

        var result = await machine.TransitionAsync(ctx, LightAction.Blink);

        result.Success.ShouldBeFalse();
        result.StateStored.ShouldBeTrue();
        result.CurrentState.ShouldBe(Light.Blinking);
        (await machine.GetStatusAsync(ctx)).State.ShouldBe(Light.Blinking);
    }

    [Test]
    public async Task Transition_ExitActionThrows_FailsAndStateNotStored()
    {
        var machine = new HookedLightMachine(_lock, _lock)
        {
            ExitOff = () => throw new InvalidOperationException("relay")
        };
        var ctx = new TestContext("1");

        var result = await machine.TransitionAsync(ctx, LightAction.TurnOn);

        result.Success.ShouldBeFalse();
        result.StateStored.ShouldBeFalse();
        result.CurrentState.ShouldBe(Light.Off);
        (await machine.GetStatusAsync(ctx)).State.ShouldBe(Light.Off);
    }

    [Test]
    public async Task Transition_Succeeds_StateStored()
    {
        var machine = new HookedLightMachine(_lock, _lock);

        var result = await machine.TransitionAsync(new TestContext("1"), LightAction.TurnOn);

        result.Success.ShouldBeTrue();
        result.StateStored.ShouldBeTrue();
    }

    // ── Machine with entry/exit actions ──────────────────────────────

    private sealed class EntryExitMachine(IDistributedLock locker, IKeyValueDataAdapter store, List<string> events)
        : AbstractStateMachine<TestContext, Light, LightAction, LightNotify>(
            Light.Off, locker, store)
    {
        protected override Action<ITransitionBuilder<Light, LightAction>> Transitions => b => b
            .From(Light.Off).On(LightAction.TurnOn).To(Light.On)
            .From(Light.On).On(LightAction.TurnOff).To(Light.Off)
            .State(Light.Off)
                .OnExit((_, _) => { events.Add("exit:Off"); return Task.CompletedTask; })
            .State(Light.On)
                .OnEnter((_, _) => { events.Add("enter:On"); return Task.CompletedTask; })
                .OnExit((_, _) => { events.Add("exit:On"); return Task.CompletedTask; });

        public override Task<TransitionResult<Light>> TransitionAsync(
            TestContext context, LightAction transition, CancellationToken ct = default) =>
            InternalTransitionAsync(context, transition, ct);

        public override Task NotifyAsync(
            TestContext context, LightNotify notification, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
