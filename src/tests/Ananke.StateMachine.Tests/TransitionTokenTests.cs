using Ananke.Abstractions.Distributed;
using Ananke.StateMachine.Channels;
using Shouldly;

namespace Ananke.StateMachine.Tests;

/// <summary>
/// The token stops a transition of <see cref="AbstractStateMachine{C,S,T,N}"/> up to its exit
/// action; once the exit action has run, the state is stored.
/// </summary>
[TestFixture]
public class TransitionTokenTests
{
    private InMemoryDistributedLock _store = new();

    [SetUp]
    public void SetUp() => _store = new InMemoryDistributedLock();

    [TearDown]
    public ValueTask TearDown() => _store.DisposeAsync();

    [Test]
    public async Task TransitionAsync_CancelledToken_ThrowsAndStoresNothing()
    {
        var machine = new HookedLightMachine(_store, _store);
        var ctx = new TestContext("1");
        var exitRan = false;
        machine.ExitOff = () => exitRan = true;

        await Should.ThrowAsync<OperationCanceledException>(
            () => machine.TransitionAsync(ctx, LightAction.TurnOn, new CancellationToken(canceled: true)));

        exitRan.ShouldBeFalse();
        (await _store.ExistsAsync(machine.GetContextKey(ctx.Id))).ShouldBeFalse();
    }

    [Test]
    public async Task TransitionAsync_CancelledWhileWaitingForLock_Throws()
    {
        var machine = new LightMachine(_store, _store);
        var ctx = new TestContext("1");
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = _store.RunCoordinatedActionAsync(machine.GetContextKey(ctx.Id), () => release.Task);
        using var cts = new CancellationTokenSource();

        var waiting = machine.TransitionAsync(ctx, LightAction.TurnOn, cts.Token);
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(5)));
        release.SetResult(true);
        await held.WaitAsync(TimeSpan.FromSeconds(5));
        (await machine.GetStatusAsync(ctx)).State.ShouldBe(Light.Off);
    }

    [Test]
    public async Task TransitionAsync_CancelledBeforeExitAction_ThrowsAndStoresNothing()
    {
        var machine = new HookedLightMachine(_store, _store);
        var ctx = new TestContext("1");
        using var cts = new CancellationTokenSource();
        var exitRan = false;
        machine.Guard = cts.Cancel;
        machine.ExitOff = () => exitRan = true;

        await Should.ThrowAsync<OperationCanceledException>(
            () => machine.TransitionAsync(ctx, LightAction.TurnOn, cts.Token));

        exitRan.ShouldBeFalse();
        (await machine.GetStatusAsync(ctx)).State.ShouldBe(Light.Off);
    }

    [Test]
    public async Task TransitionAsync_CancelledInExitAction_StoresTheState()
    {
        var machine = new HookedLightMachine(_store, _store);
        var ctx = new TestContext("1");
        using var cts = new CancellationTokenSource();
        machine.ExitOff = cts.Cancel;

        var result = await machine.TransitionAsync(ctx, LightAction.TurnOn, cts.Token);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.CurrentState.ShouldBe(Light.On);
        (await machine.GetStatusAsync(ctx)).State.ShouldBe(Light.On);
    }

    [Test]
    public async Task FaultAsync_CancelledToken_ThrowsAndStatusUnchanged()
    {
        var machine = new LightMachine(_store, _store);
        var ctx = new TestContext("1");

        await Should.ThrowAsync<OperationCanceledException>(
            () => machine.FaultAsync(ctx, "sensor", new CancellationToken(canceled: true)));

        (await machine.GetStatusAsync(ctx)).OperationalStatus.ShouldBe(OperationalStatus.Operative);
    }

    [Test]
    public async Task ChannelWorker_HandleAsync_PassesItsToken()
    {
        var machine = new LightMachine(_store, _store);
        var ctx = new TestContext("1");
        await using var worker = new StateMachineChannelWorker<TestContext, Light, LightAction, LightNotify>(machine);

        await Should.ThrowAsync<OperationCanceledException>(
            () => worker.HandleAsync(ctx, LightAction.TurnOn, new CancellationToken(canceled: true)));

        (await machine.GetStatusAsync(ctx)).State.ShouldBe(Light.Off);
    }
}
