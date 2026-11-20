using System.Collections.Concurrent;
using Ananke.Abstractions.Distributed;
using Shouldly;

namespace Ananke.StateMachine.Tests;

/// <summary>
/// Each context's record and lock live under <c>{KeyPrefix}:{MachineName}:{context id}</c>, and the
/// record carries a format version.
/// </summary>
[TestFixture]
public class ContextKeyTests
{
    private InMemoryDistributedLock _store = new();

    [SetUp]
    public void SetUp() => _store = new InMemoryDistributedLock();

    [TearDown]
    public ValueTask TearDown() => _store.DisposeAsync();

    [Test]
    public void GetContextKey_Defaults_PrefixMachineTypeAndId()
    {
        var machine = new LightMachine(_store, _store);

        machine.MachineName.ShouldBe(nameof(LightMachine));
        machine.GetContextKey("device-1").ShouldBe("statemachine:LightMachine:device-1");
    }

    [TestCase("fleet", "motion", "fleet:motion:device-1")]
    [TestCase("", "motion", "motion:device-1")]
    [TestCase("fleet", "", "fleet:device-1")]
    [TestCase("", "", "device-1")]
    public void GetContextKey_FromOptions_LeavesOutEmptyParts(string prefix, string machineName, string expected)
    {
        var options = new StateMachineOptions { KeyPrefix = prefix, MachineName = machineName };
        var machine = new LightMachine(_store, _store, options);

        machine.GetContextKey("device-1").ShouldBe(expected);
    }

    [Test]
    public void GetContextKey_GenericMachine_NamesItsTypeArguments()
    {
        var retail = new GenericLightMachine<Light>(_store, _store);
        var door = new GenericLightMachine<DoorState>(_store, _store);

        retail.GetContextKey("1").ShouldBe("statemachine:GenericLightMachine<Light>:1");
        door.GetContextKey("1").ShouldBe("statemachine:GenericLightMachine<DoorState>:1");
    }

    [Test]
    public async Task TransitionAsync_TwoMachineTypesSameContextId_KeepSeparateRecordsAndLocks()
    {
        var locker = new RecordingLock(_store);
        var light = new LightMachine(locker, _store);
        var door = new DoorMachine(locker, _store);
        var ctx = new TestContext("1");

        (await light.TransitionAsync(ctx, LightAction.TurnOn)).Success.ShouldBeTrue();
        (await door.TransitionAsync(ctx, DoorAction.Unlock)).Success.ShouldBeTrue();
        (await light.TransitionAsync(ctx, LightAction.Blink)).Success.ShouldBeTrue();

        (await light.GetStatusAsync(ctx)).State.ShouldBe(Light.Blinking);
        (await door.GetStatusAsync(ctx)).State.ShouldBe(DoorState.Closed);
        locker.Resources.Distinct().ShouldBe(
            ["statemachine:LightMachine:1", "statemachine:DoorMachine:1"], ignoreOrder: true);
        (await _store.ExistsAsync(ctx.Id)).ShouldBeFalse();
    }

    [Test]
    public async Task TransitionAsync_SameTypeDifferentMachineNames_KeepSeparateRecords()
    {
        var kitchen = new LightMachine(_store, _store, new StateMachineOptions { MachineName = "kitchen" });
        var hall = new LightMachine(_store, _store, new StateMachineOptions { MachineName = "hall" });
        var ctx = new TestContext("1");

        (await kitchen.TransitionAsync(ctx, LightAction.TurnOn)).Success.ShouldBeTrue();

        (await kitchen.GetStatusAsync(ctx)).State.ShouldBe(Light.On);
        (await hall.GetStatusAsync(ctx)).State.ShouldBe(Light.Off);
    }

    [Test]
    public async Task FaultAsync_TakesTheContextLock()
    {
        var locker = new RecordingLock(_store);
        var machine = new LightMachine(locker, _store);
        var ctx = new TestContext("device-1");

        await machine.FaultAsync(ctx, "sensor");
        await machine.ResetAsync(ctx, "replaced");

        locker.Resources.ShouldBe(["statemachine:LightMachine:device-1", "statemachine:LightMachine:device-1"]);
    }

    [Test]
    public async Task TransitionAsync_WritesFormatVersion()
    {
        var machine = new LightMachine(_store, _store);
        var ctx = new TestContext("1");

        await machine.TransitionAsync(ctx, LightAction.TurnOn);

        var stored = await _store.GetValueAsync<LightMachine.PersistedContext<Light>>(machine.GetContextKey(ctx.Id));
        stored.ShouldNotBeNull();
        stored.Version.ShouldBe(LightMachine.PersistedContext<Light>.FormatVersion);
    }

    [Test]
    public async Task TransitionAsync_RecordWithoutVersion_IsRead()
    {
        var machine = new LightMachine(_store, _store);
        var ctx = new TestContext("1");
        await _store.SetValueAsync(machine.GetContextKey(ctx.Id), """{"State":1,"Step":4}""");

        var result = await machine.TransitionAsync(ctx, LightAction.Blink);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.PreviousState.ShouldBe(Light.On);
        result.CurrentState.ShouldBe(Light.Blinking);
    }

    [Test]
    public async Task TransitionAsync_RecordOfNewerFormat_FailsAndWritesNothing()
    {
        var machine = new LightMachine(_store, _store);
        var ctx = new TestContext("1");
        var key = machine.GetContextKey(ctx.Id);
        const string newer = """{"Version":2,"State":1,"Step":4}""";
        await _store.SetValueAsync(key, newer);

        var result = await machine.TransitionAsync(ctx, LightAction.Blink);

        result.Success.ShouldBeFalse();
        result.ErrorMessage!.ShouldContain("format version 2");
        (await _store.GetValueAsync(key)).ShouldBe(newer);
    }

    [Test]
    public async Task GetStatusAsync_UnknownContext_InitialAndOperative()
    {
        var machine = new LightMachine(_store, _store);

        var status = await machine.GetStatusAsync(new TestContext("never-seen"));

        status.ShouldBe(new ContextStatus<Light>(Light.Off, 0, OperationalStatus.Operative, null, false));
    }

    [Test]
    public async Task TransitionAsync_FirstCallsInParallel_BuildTableOnce()
    {
        var machine = new CountingTableMachine(_store, _store);

        await Task.WhenAll(Enumerable.Range(0, 64).Select(i =>
            Task.Run(() => machine.TransitionAsync(new TestContext($"device-{i}"), LightAction.TurnOn))));

        machine.TableBuilds.ShouldBe(1);
    }

    [Test]
    public async Task Middleware_ReceivesTheContextsStoredState()
    {
        var machine = new LightMachine(_store, _store);
        var seen = new ConcurrentQueue<(string Id, Light State)>();
        machine.UseMiddleware(new RecordingMiddleware(seen));
        var on = new TestContext("on");
        var off = new TestContext("off");

        await machine.TransitionAsync(on, LightAction.TurnOn);
        await machine.TransitionAsync(off, LightAction.TurnOff);
        await machine.TransitionAsync(on, LightAction.Blink);

        seen.ShouldBe([("on", Light.Off), ("off", Light.Off), ("on", Light.On)]);
    }

    private sealed class RecordingMiddleware(ConcurrentQueue<(string Id, Light State)> seen)
        : Middleware.ITransitionMiddleware<TestContext, Light, LightAction>
    {
        public Task<TransitionResult<Light>> InvokeAsync(
            TestContext context, LightAction transition, Light currentState,
            Func<CancellationToken, Task<TransitionResult<Light>>> next, CancellationToken ct)
        {
            seen.Enqueue((context.Id, currentState));
            return next(ct);
        }
    }

    private sealed class GenericLightMachine<TTag>(IDistributedLock locker, IKeyValueDataAdapter store)
        : AbstractStateMachine<TestContext, Light, LightAction, LightNotify>(Light.Off, locker, store)
    {
        protected override Action<Builder.ITransitionBuilder<Light, LightAction>> Transitions => b =>
            b.From(Light.Off).On(LightAction.TurnOn).To(Light.On);

        public override Task<TransitionResult<Light>> TransitionAsync(
            TestContext context, LightAction transition, CancellationToken ct = default) =>
            InternalTransitionAsync(context, transition, ct);

        public override Task NotifyAsync(TestContext context, LightNotify notification, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class CountingTableMachine(IDistributedLock locker, IKeyValueDataAdapter store)
        : AbstractStateMachine<TestContext, Light, LightAction, LightNotify>(Light.Off, locker, store)
    {
        private int _tableBuilds;

        public int TableBuilds => _tableBuilds;

        protected override Action<Builder.ITransitionBuilder<Light, LightAction>> Transitions => b =>
        {
            Interlocked.Increment(ref _tableBuilds);
            Thread.Sleep(20);
            b.From(Light.Off).On(LightAction.TurnOn).To(Light.On);
        };

        public override Task<TransitionResult<Light>> TransitionAsync(
            TestContext context, LightAction transition, CancellationToken ct = default) =>
            InternalTransitionAsync(context, transition, ct);

        public override Task NotifyAsync(TestContext context, LightNotify notification, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    /// <summary>Records the resource of every attempt to take the lock.</summary>
    private sealed class RecordingLock(IDistributedLock inner) : IDistributedLock
    {
        private readonly ConcurrentQueue<string> _resources = new();

        public IReadOnlyCollection<string> Resources => _resources;

        public Task<CoordinatedActionResult<R>> RunCoordinatedActionAsync<R>(
            string resourceId, Func<Task<R>> action, CancellationToken ct = default)
        {
            _resources.Enqueue(resourceId);
            return inner.RunCoordinatedActionAsync(resourceId, action, ct);
        }
    }
}
