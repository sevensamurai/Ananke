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

    // ── Machine with entry/exit actions ──────────────────────────────

    private sealed class EntryExitMachine(IDistributedLock locker, IKeyValueDataAdapter store, List<string> events)
        : AbstractStateMachine<TestContext, Light, LightAction, LightNotify>(
            Light.Off, locker, store)
    {
        protected override Action<ITransitionBuilder<Light, LightAction>> Transitions => b => b
            .From(Light.Off).On(LightAction.TurnOn).To(Light.On)
            .From(Light.On).On(LightAction.TurnOff).To(Light.Off)
            .State(Light.Off)
                .OnExit(() => { events.Add("exit:Off"); return Task.CompletedTask; })
            .State(Light.On)
                .OnEnter(() => { events.Add("enter:On"); return Task.CompletedTask; })
                .OnExit(() => { events.Add("exit:On"); return Task.CompletedTask; });

        public override Task<TransitionResult<Light>> TransitionAsync(
            TestContext context, LightAction transition) =>
            InternalTransitionAsync(context, transition);

        public override Task NotifyAsync(
            TestContext context, LightNotify notification) =>
            Task.CompletedTask;
    }
}
