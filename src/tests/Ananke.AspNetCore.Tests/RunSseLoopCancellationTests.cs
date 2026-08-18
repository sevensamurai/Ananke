using Ananke.AspNetCore.Sse;
using Ananke.StateMachine;
using Shouldly;

// 'StateMachine' resolves to the namespace from inside Ananke.AspNetCore.Tests, not the factory class.
using SM = Ananke.StateMachine.StateMachine;

namespace Ananke.AspNetCore.Tests;

/// <summary>
/// Q30: the SSE loop awaited the state machine's current work with no way to observe
/// <c>HttpContext.RequestAborted</c>, so a disconnected client left the loop running until the
/// machine happened to finish.
/// </summary>
/// <remarks>
/// The subtlety these cover is the exception filter in <c>RunSseLoopAsync</c>: two different
/// cancellations surface at the same <c>await</c>. The machine cancelling its own state work (on an
/// interrupt) must be swallowed so the loop continues; the caller's token firing must propagate,
/// because "the client went away" is not the same outcome as "the machine went idle", and returning
/// <see langword="false"/> for both would report a normal result for an abandoned request.
/// </remarks>
[TestFixture]
public class RunSseLoopCancellationTests
{
    private enum Phase { Idle, Working, Done }
    private enum Step { Start, Finish }

    /// <summary>Machine parked in <see cref="Phase.Working"/> with work that never completes.</summary>
    private static async Task<StateMachine<Phase, Step>> ParkedInWorkingAsync()
    {
        var machine = SM.Create<Phase, Step>(
                Phase.Idle, b => b
                    .From(Phase.Idle).On(Step.Start).To(Phase.Working)
                    .From(Phase.Working).On(Step.Finish).To(Phase.Done))
            .OnEnter(Phase.Working, async ct => await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false));

        await machine.FireAsync(Step.Start).ConfigureAwait(false);
        return machine;
    }

    [Test]
    public async Task RunSseLoopAsync_WhenCallerTokenCancelsMidLoop_Propagates()
    {
        using var machine = await ParkedInWorkingAsync();
        using var cts = new CancellationTokenSource();

        var loop = machine.RunSseLoopAsync(Phase.Done, cts.Token);
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await loop);
    }

    [Test]
    public async Task RunSseLoopAsync_WithAlreadyCancelledToken_DoesNotReportANormalResult()
    {
        using var machine = await ParkedInWorkingAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // The regression this guards: swallowing the cancellation and returning false, which a
        // caller cannot distinguish from "the machine went idle before reaching Done".
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await machine.RunSseLoopAsync(Phase.Done, cts.Token));
    }

    [Test]
    public async Task RunSseLoopAsync_WithNoToken_StillReportsReachingTerminalState()
    {
        // The token is optional; existing callers pass nothing and must be unaffected.
        using var machine = SM.Create<Phase, Step>(
            Phase.Idle, b => b
                .From(Phase.Idle).On(Step.Start).To(Phase.Working)
                .From(Phase.Working).On(Step.Finish).To(Phase.Done));

        await machine.FireAsync(Step.Start);
        await machine.FireAsync(Step.Finish);

        (await machine.RunSseLoopAsync(Phase.Done)).ShouldBeTrue();
    }
}
