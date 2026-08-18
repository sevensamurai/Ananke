using Shouldly;

namespace Ananke.StateMachine.Tests;

/// <summary>
/// Q31: <see cref="StateMachine{S,T}.FireAsync"/> and
/// <see cref="StateMachine{S,T}.SignalInsightAsync{TInsight}"/> are serialized behind one gate and
/// took no <see cref="CancellationToken"/>, so a caller queued behind an in-flight transition had
/// no way to abandon the wait.
/// </summary>
/// <remarks>
/// The machine's own per-state <c>CancellationTokenSource</c> is deliberately left alone — it
/// scopes a state's background work to that state's lifetime, which is a different question from
/// "abandon my call". See the comment at <c>StartStateWork</c>.
/// </remarks>
[TestFixture]
public class TransitionCancellationTests
{
    private static StateMachine<SimplePhase, SimpleAction> CreateMachine() =>
        StateMachine.Create<SimplePhase, SimpleAction>(
            SimplePhase.Searching, b => b
                .From(SimplePhase.Searching).On(SimpleAction.StartPaperwork).To(SimplePhase.Paperwork)
                .From(SimplePhase.Paperwork).On(SimpleAction.Complete).To(SimplePhase.Done));

    [Test]
    public async Task FireAsync_WithAlreadyCancelledToken_DoesNotTransition()
    {
        using var machine = CreateMachine();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await machine.FireAsync(SimpleAction.StartPaperwork, ct: cts.Token));

        machine.CurrentState.ShouldBe(SimplePhase.Searching,
            "a cancelled fire must not leave the machine half-transitioned");
    }

    [Test]
    public async Task FireAsync_WithLiveToken_StillTransitions()
    {
        // Guards the obvious regression: threading a token must not break the default path.
        using var machine = CreateMachine();
        using var cts = new CancellationTokenSource();

        var result = await machine.FireAsync(SimpleAction.StartPaperwork, ct: cts.Token);

        result.Success.ShouldBeTrue();
        machine.CurrentState.ShouldBe(SimplePhase.Paperwork);
    }

    [Test]
    public async Task FireAsync_WithNoToken_StillTransitions()
    {
        // The token is optional; every existing caller passes nothing.
        using var machine = CreateMachine();

        var result = await machine.FireAsync(SimpleAction.StartPaperwork);

        result.Success.ShouldBeTrue();
        machine.CurrentState.ShouldBe(SimplePhase.Paperwork);
    }

    [Test]
    public async Task SignalInsightAsync_WithAlreadyCancelledToken_Throws()
    {
        using var machine = CreateMachine();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await machine.SignalInsightAsync("some insight", cts.Token));
    }

    [Test]
    public async Task FireAsync_CancelledCaller_DoesNotPoisonTheGateForOthers()
    {
        // The gate is released in a finally, but a token that fires *before* the wait completes
        // means the gate was never taken — so the next caller must still get through.
        using var machine = CreateMachine();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await machine.FireAsync(SimpleAction.StartPaperwork, ct: cancelled.Token));

        var result = await machine.FireAsync(SimpleAction.StartPaperwork);

        result.Success.ShouldBeTrue("a cancelled caller must not leave the gate held");
        machine.CurrentState.ShouldBe(SimplePhase.Paperwork);
    }
}
