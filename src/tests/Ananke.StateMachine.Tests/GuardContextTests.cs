using System.Collections.Concurrent;
using Ananke.Abstractions;
using Ananke.Abstractions.Distributed;
using Ananke.StateMachine.Builder;
using Shouldly;

namespace Ananke.StateMachine.Tests;

/// <summary>
/// Guards and actions get the context being transitioned and the transition's token, so one
/// instance serving many contexts checks each against its own context.
/// </summary>
[TestFixture]
public class GuardContextTests
{
    private InMemoryDistributedLock _store = new();

    [SetUp]
    public void SetUp() => _store = new InMemoryDistributedLock();

    [TearDown]
    public ValueTask TearDown() => _store.DisposeAsync();

    [Test]
    public async Task TransitionAsync_TwoContextsAtOnce_EachGuardSeesItsOwnContext()
    {
        var machine = new TankMachine(_store, _store);
        var empty = new TankContext("tank-empty", 0);
        var full = new TankContext("tank-full", 5);
        var seen = new ConcurrentDictionary<string, int>();
        var inside = 0;
        var bothInside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        machine.Guard = async (tank, _) =>
        {
            seen[tank.Id] = tank.Fuel;
            if (Interlocked.Increment(ref inside) == 2)
                bothInside.SetResult();
            await bothInside.Task.WaitAsync(TimeSpan.FromSeconds(5));
        };

        var results = await Task.WhenAll(
            machine.TransitionAsync(empty, LightAction.TurnOn),
            machine.TransitionAsync(full, LightAction.TurnOn)).WaitAsync(TimeSpan.FromSeconds(10));

        seen.ShouldBe(new Dictionary<string, int> { ["tank-empty"] = 0, ["tank-full"] = 5 }, ignoreOrder: true);
        results[0].ErrorMessage.ShouldBe(TransitionResult<Light>.GuardFailed(Light.Off).ErrorMessage);
        results[0].CurrentState.ShouldBe(Light.Off);
        results[1].Success.ShouldBeTrue(results[1].ErrorMessage);
        results[1].CurrentState.ShouldBe(Light.On);
    }

    [Test]
    public async Task TransitionAsync_CancelledWhileGuardRuns_GuardSeesTheCancellation()
    {
        var machine = new TankMachine(_store, _store);
        var tank = new TankContext("tank-1", 5);
        var guardStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        machine.Guard = async (_, ct) =>
        {
            guardStarted.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        };
        using var cts = new CancellationTokenSource();

        var transition = machine.TransitionAsync(tank, LightAction.TurnOn, cts.Token);
        await guardStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => transition.WaitAsync(TimeSpan.FromSeconds(5)));
        (await machine.GetStatusAsync(tank)).State.ShouldBe(Light.Off);
    }

    [Test]
    public async Task TransitionAsync_Actions_GetTheContextAndToken()
    {
        var machine = new TankMachine(_store, _store);
        var tank = new TankContext("tank-1", 5);
        using var cts = new CancellationTokenSource();

        await machine.TransitionAsync(tank, LightAction.TurnOn, cts.Token);
        await machine.TransitionAsync(tank, LightAction.Blink, cts.Token);

        machine.Calls.ShouldBe(["exit:Off:tank-1", "enter:On:tank-1", "after:Blink:tank-1"]);
        machine.Tokens.ShouldAllBe(token => token == cts.Token);
    }

    private sealed record TankContext(string Id, int Fuel) : IBaseContext;

    //  Off ──[TurnOn]──► On (guard: fuel > 0; exit Off, enter On)
    //  On  ──[Blink]──► Blinking (after-transition action)
    private sealed class TankMachine(IDistributedLock locker, IKeyValueDataAdapter store)
        : AbstractStateMachine<TankContext, Light, LightAction, LightNotify>(Light.Off, locker, store)
    {
        public Func<TankContext, CancellationToken, Task> Guard { get; set; } = (_, _) => Task.CompletedTask;
        public List<string> Calls { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];

        protected override Action<ITransitionBuilder<Light, LightAction>> Transitions => b => b
            .From(Light.Off).On(LightAction.TurnOn).To(Light.On)
                .WhenAsync(async (context, ct) =>
                {
                    var tank = (TankContext)context;
                    await Guard(tank, ct);
                    return tank.Fuel > 0;
                })
            .From(Light.On).On(LightAction.Blink).To(Light.Blinking)
                .WithAction((context, ct) => Record("after:Blink", context, ct))
            .State(Light.Off)
                .OnExit((context, ct) => Record("exit:Off", context, ct))
            .State(Light.On)
                .OnEnter((context, ct) => Record("enter:On", context, ct));

        private Task Record(string call, IBaseContext context, CancellationToken ct)
        {
            Calls.Add($"{call}:{context.Id}");
            Tokens.Add(ct);
            return Task.CompletedTask;
        }

        public override Task<TransitionResult<Light>> TransitionAsync(
            TankContext context, LightAction transition, CancellationToken ct = default) =>
            InternalTransitionAsync(context, transition, ct);

        public override Task NotifyAsync(
            TankContext context, LightNotify notification, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
