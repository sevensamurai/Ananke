using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Routing;
using Ananke.TestHelpers;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class ForkJoinTests
{
    [Test]
    public async Task Fork_TwoBranches_ExecuteInParallel()
    {
        var execution = await new Workflow<CounterState>("fork-basic")
            .Job("start", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("branch-a", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "a"], Value = s.Value + 10 }))
            .Job("branch-b", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "b"], Value = s.Value + 100 }))
            .Job("merge-point", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "merged"] }))
            .Then("start", Workflow.Fork("branch-a", "branch-b"))
            .Join(["branch-a", "branch-b"], "merge-point",
                states => new CounterState
                {
                    Value = states.Sum(s => s.Value),
                    Trail = [.. states.SelectMany(s => s.Trail)]
                })
            .Then("merge-point", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.Success.ShouldBeTrue();
        // Both branches ran with Value=1 from start: (1+10) + (1+100) = 112
        execution.Result.FinalState.Value.ShouldBe(112);
        execution.Result.FinalState.Trail.ShouldContain("a");
        execution.Result.FinalState.Trail.ShouldContain("b");
        execution.Result.FinalState.Trail.ShouldContain("merged");
    }

    [Test]
    public async Task Fork_FailFast_CancelsOnFirstFailure()
    {
        var execution = await new Workflow<CounterState>("fork-failfast")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("ok-branch", async (s, ct) =>
            {
                await WorkflowLoops.Park(ct);
                return s with { Trail = [.. s.Trail, "ok"] };
            })
            .Job("bad-branch", (_, _) => throw new InvalidOperationException("branch failed"))
            .Job("after", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork(ForkMode.FailFast, "ok-branch", "bad-branch"))
            .Join(["ok-branch", "bad-branch"], "after",
                states => states[0])
            .Then("after", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Faulted);
        execution.Result!.Success.ShouldBeFalse();
        execution.Result.Error.ShouldNotBeNull();
        execution.Result.Error.ShouldContain("branch failed");
    }

    [Test]
    public async Task Fork_BestEffort_ContinuesOnBranchFailure()
    {
        var execution = await new Workflow<CounterState>("fork-besteffort")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("ok-branch", (s, _) => Task.FromResult(s with { Value = 42, Trail = [.. s.Trail, "ok"] }))
            .Job("bad-branch", (_, _) => throw new InvalidOperationException("branch failed"))
            .Job("after", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "after"] }))
            .Then("start", Workflow.Fork(ForkMode.BestEffort, "ok-branch", "bad-branch"))
            .Join(["ok-branch", "bad-branch"], "after",
                states => states.Length == 2
                    ? new CounterState { Value = states.Sum(s => s.Value) }
                    : states[0])
            .Then("after", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.FinalState.Value.ShouldBe(42);
        execution.Result.FinalState.Trail.ShouldContain("ok");
        execution.Result.FinalState.Trail.ShouldContain("after");
    }

    [Test]
    public async Task Fork_BranchHistoryRecordedInExecution()
    {
        var execution = await new Workflow<CounterState>("fork-history")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("left", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "left"] }))
            .Job("right", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "right"] }))
            .Job("end", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork("left", "right"))
            .Join(["left", "right"], "end", states => new CounterState
            {
                Trail = [.. states.SelectMany(s => s.Trail)]
            })
            .Then("end", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);

        var jobNames = execution.History.Select(h => h.JobName).ToList();
        jobNames.ShouldContain("start");
        jobNames.ShouldContain("left");
        jobNames.ShouldContain("right");
        jobNames.ShouldContain("end");
    }

    // ── Branch outcomes ───────────────────────────────────────────────────────
    //
    // Before these, a branch dropped under BestEffort left no program-visible trace at all: its
    // exception was discarded unless every branch failed, its history never reached the execution,
    // and the run reported plain success.

    [Test]
    public async Task Fork_BestEffort_FaultedBranch_ReportedInBranchOutcomes()
    {
        var execution = await ThreeBranchBestEffort().RunAsync(new CounterState());

        var outcomes = execution.Result!.BranchOutcomes;
        outcomes.Count.ShouldBe(1, "only the branch that failed should be reported");

        var failed = outcomes[0];
        failed.BranchTarget.ShouldBe("bad-branch");
        failed.Kind.ShouldBe(BranchOutcomeKind.Faulted);
        failed.Succeeded.ShouldBeFalse();
        failed.Exception.ShouldBeOfType<InvalidOperationException>()
            .Message.ShouldBe("branch failed");
    }

    [Test]
    public async Task Fork_BestEffort_FaultedBranch_HistoryIsRecorded()
    {
        var execution = await ThreeBranchBestEffort().RunAsync(new CounterState());

        // The failed branch's own job entry — recorded before the throw, and previously lost
        // with it because the history list lived inside ExecuteBranchAsync.
        var failedEntry = execution.History.SingleOrDefault(h => h.JobName == "bad-branch");
        failedEntry.ShouldNotBeNull();
        failedEntry!.Success.ShouldBeFalse();
        failedEntry.Error.ShouldBe("branch failed");
    }

    [Test]
    public async Task Fork_AllBranchesSucceed_BranchOutcomesIsEmpty()
    {
        var execution = await new Workflow<CounterState>("fork-all-ok")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("left", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("right", (s, _) => Task.FromResult(s with { Value = 2 }))
            .Job("after", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork(ForkMode.BestEffort, "left", "right"))
            .Join(["left", "right"], "after",
                states => new CounterState { Value = states.Sum(s => s.Value) })
            .Then("after", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.BranchOutcomes.ShouldBeEmpty(
            "a healthy fork must not put noise on the result");
    }

    [Test]
    public async Task Fork_BestEffort_FaultedBranch_StillCompletesSuccessfully()
    {
        // Opting into BestEffort declares a partial result acceptable, so
        // the run still reports success and the caller escalates via BranchOutcomes if it wants to.
        // Changing this is an ADR amendment, not a drive-by.
        var execution = await ThreeBranchBestEffort().RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.Success.ShouldBeTrue();
        execution.Result.BranchOutcomes.ShouldNotBeEmpty();
    }

    [Test]
    public async Task Fork_FailFast_FaultedBranch_StillThrows()
    {
        // Pins that FailFast behaviour is unchanged by the outcome plumbing.
        var execution = await new Workflow<CounterState>("fork-failfast-throws")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("ok-branch", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("bad-branch", (_, _) => throw new InvalidOperationException("failfast boom"))
            .Job("after", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork(ForkMode.FailFast, "ok-branch", "bad-branch"))
            .Join(["ok-branch", "bad-branch"], "after", states => states[0])
            .Then("after", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Faulted);
        execution.Result!.Success.ShouldBeFalse();
        execution.Result.Exception!.Message.ShouldBe("failfast boom");
    }

    [Test]
    public async Task Fork_FailFast_FaultedBranch_ReportedInBranchOutcomes()
    {
        // FailFast used to yield BranchOutcomes == [] because the rethrow fired before the
        // recording loop ever ran. The exception and control flow are unchanged (see
        // StillThrows above) — only the reporting is new.
        var execution = await new Workflow<CounterState>("fork-failfast-outcomes")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("ok-branch", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("bad-branch", (_, _) => throw new InvalidOperationException("branch failed"))
            .Job("after", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork(ForkMode.FailFast, "ok-branch", "bad-branch"))
            .Join(["ok-branch", "bad-branch"], "after", states => states[0])
            .Then("after", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Faulted);

        var failed = execution.Result!.BranchOutcomes.SingleOrDefault(o => o.BranchTarget == "bad-branch");
        failed.ShouldNotBeNull();
        failed!.Kind.ShouldBe(BranchOutcomeKind.Faulted);
        failed.Exception.ShouldBeOfType<InvalidOperationException>()
            .Message.ShouldBe("branch failed");
    }

    [Test]
    public async Task Fork_FailFast_FaultedBranch_HistoryIsRecorded()
    {
        // Same B1 reasoning as ReportedInBranchOutcomes above, for the history side.
        var execution = await new Workflow<CounterState>("fork-failfast-history")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("ok-branch", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("bad-branch", (_, _) => throw new InvalidOperationException("branch failed"))
            .Job("after", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork(ForkMode.FailFast, "ok-branch", "bad-branch"))
            .Join(["ok-branch", "bad-branch"], "after", states => states[0])
            .Then("after", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Faulted);

        var failedEntry = execution.History.SingleOrDefault(h => h.JobName == "bad-branch");
        failedEntry.ShouldNotBeNull();
        failedEntry!.Success.ShouldBeFalse();
        failedEntry.Error.ShouldBe("branch failed");
    }

    [Test]
    public async Task Fork_BestEffort_AllBranchesFail_ThrowsWithoutRecordingOutcomes()
    {
        // B1 deliberately leaves this path alone (the plan's own instruction while reordering
        // the FailFast throw) — pinned so a future change here is a decision, not an accident.
        var execution = await new Workflow<CounterState>("fork-besteffort-allfail")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("bad-branch-1", (_, _) => throw new InvalidOperationException("one"))
            .Job("bad-branch-2", (_, _) => throw new InvalidOperationException("two"))
            .Job("after", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork(ForkMode.BestEffort, "bad-branch-1", "bad-branch-2"))
            .Join(["bad-branch-1", "bad-branch-2"], "after", states => states[0])
            .Then("after", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Faulted);
        execution.Result!.Exception.ShouldBeOfType<AggregateException>();
        execution.Result.BranchOutcomes.ShouldBeEmpty();
        execution.History.ShouldNotContain(h => h.JobName == "bad-branch-1" || h.JobName == "bad-branch-2");
    }

    [Test]
    public async Task Fork_BestEffort_BranchSelfCancels_ClassifiedAsFaultedNotCancelled()
    {
        // R6: `ex is OperationCanceledException` alone conflated "cancelled by the fork" with
        // "the job threw OCE from its own unrelated token." Under BestEffort with every branch
        // self-cancelling this used to produce an AggregateException with zero inner exceptions,
        // because Cancelled outcomes carry a null Exception.
        var execution = await new Workflow<CounterState>("fork-besteffort-self-cancel")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("ok-branch", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("self-cancel-branch", (CounterState _, CancellationToken _) =>
            {
                var unrelatedCts = new CancellationTokenSource();
                unrelatedCts.Cancel();
                return Task.FromCanceled<CounterState>(unrelatedCts.Token);
            })
            .Job("after", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork(ForkMode.BestEffort, "ok-branch", "self-cancel-branch"))
            .Join(["ok-branch", "self-cancel-branch"], "after", states => states[0])
            .Then("after", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);

        var outcome = execution.Result!.BranchOutcomes.SingleOrDefault(o => o.BranchTarget == "self-cancel-branch");
        outcome.ShouldNotBeNull();
        outcome!.Kind.ShouldBe(BranchOutcomeKind.Faulted);
        outcome.Exception.ShouldNotBeNull();
    }

    [Test]
    public async Task Fork_FailFast_BranchSelfCancels_StillFaultsInsteadOfMissingJoin()
    {
        // R6's more expensive failure mode: previously the self-cancelled branch was
        // misclassified Cancelled, so faulted.Count stayed 0, FailFast's throw never fired, and
        // execution fell through to join matching with an empty endpoint set — surfacing
        // "No matching Join found for branch endpoints: []" instead of the real fault.
        var execution = await new Workflow<CounterState>("fork-failfast-self-cancel")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("ok-branch", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("self-cancel-branch", (CounterState _, CancellationToken _) =>
            {
                var unrelatedCts = new CancellationTokenSource();
                unrelatedCts.Cancel();
                return Task.FromCanceled<CounterState>(unrelatedCts.Token);
            })
            .Job("after", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork(ForkMode.FailFast, "ok-branch", "self-cancel-branch"))
            .Join(["ok-branch", "self-cancel-branch"], "after", states => states[0])
            .Then("after", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Faulted);
        execution.Result!.Error.ShouldNotBeNull();
        execution.Result.Error.ShouldNotContain("No matching Join found");
        execution.Result.Exception.ShouldNotBeNull();
    }

    [Test]
    public async Task Join_WithContextMerge_SeesFailedBranchOutcome()
    {
        // The D4 path: the merge callback is the coordinator, so it can see what was dropped and
        // decide. The array-form overload cannot — it just receives a shorter list.
        var sawFailure = false;

        var execution = await new Workflow<CounterState>("fork-context-merge")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("ok-branch", (s, _) => Task.FromResult(s with { Value = 7 }))
            .Job("bad-branch", (_, _) => throw new InvalidOperationException("dropped"))
            .Job("after", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork(ForkMode.BestEffort, "ok-branch", "bad-branch"))
            .Join(["ok-branch", "bad-branch"], "after", ctx =>
            {
                sawFailure = ctx.HasFailures;
                // Substitute a sentinel rather than accept the partial merge silently.
                return ctx.HasFailures
                    ? new CounterState { Value = -1 }
                    : new CounterState { Value = ctx.States.Sum(s => s.Value) };
            })
            .Then("after", Workflow.End)
            .RunAsync(new CounterState());

        sawFailure.ShouldBeTrue("the merge callback must see that a branch was dropped");
        execution.Result!.FinalState.Value.ShouldBe(-1);

        var outcomes = execution.Result.BranchOutcomes;
        outcomes.Count.ShouldBe(1);
        outcomes[0].BranchTarget.ShouldBe("bad-branch");
    }

    [Test]
    public async Task Fork_BranchWithInterrupt_ThrowsNotSupported()
    {
        // Silently ignoring the interrupt — the prior behaviour — would turn
        // "pause for a human" into "skip the gated work".
        var execution = await new Workflow<CounterState>("fork-interrupt")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("left", (s, _) => Task.FromResult(s))
            .Job("right", (s, _) => Task.FromResult(s))
            .Job("after", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork(ForkMode.FailFast, "left", "right"))
            .Join(["left", "right"], "after", states => states[0])
            .Then("after", Workflow.End)
            .InterruptBefore("right")
            .UseCheckpointing(new InMemoryCheckpointStore())
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Faulted);
        execution.Result!.Exception.ShouldBeOfType<NotSupportedException>()
            .Message.ShouldContain("not supported inside forks");
    }

    /// <summary>
    /// Three branches under <see cref="ForkMode.BestEffort"/>, one of which throws. Shared by the
    /// outcome tests so they all describe the same scenario.
    /// </summary>
    private static Workflow<CounterState> ThreeBranchBestEffort() =>
        new Workflow<CounterState>("fork-outcomes")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("ok-one", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("ok-two", (s, _) => Task.FromResult(s with { Value = 2 }))
            .Job("bad-branch", (_, _) => throw new InvalidOperationException("branch failed"))
            .Job("after", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork(ForkMode.BestEffort, "ok-one", "ok-two", "bad-branch"))
            .Join(["ok-one", "ok-two", "bad-branch"], "after",
                states => new CounterState { Value = states.Sum(s => s.Value) })
            .Then("after", Workflow.End);

    [Test]
    public void Build_ForkWithUndefinedTarget_Throws()
    {
#pragma warning disable ANANKE001 // intentional: testing runtime validation of undefined fork target
        var workflow = new Workflow<CounterState>("bad-fork")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("branch-a", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork("branch-a", "nonexistent"))
            .Join(["branch-a", "nonexistent"], "end", states => states[0]);
#pragma warning restore ANANKE001

        Should.Throw<InvalidOperationException>(() => workflow.Build());
    }

    /// <summary>
    /// A router that records whether the token it was handed can actually be cancelled.
    /// <c>CancellationToken.None.CanBeCanceled</c> is <c>false</c>, so this distinguishes
    /// "the workflow's token reached me" from "I was given the default".
    /// </summary>
    private sealed class TokenObservingRouter : IRouter<CounterState>
    {
        public bool? TokenWasCancellable { get; private set; }

        public Task<string> RouteAsync(CounterState state, CancellationToken ct)
        {
            TokenWasCancellable = ct.CanBeCanceled;
            return Task.FromResult("branch-a-tail");
        }
    }

    [Test]
    public async Task Fork_BranchRouter_ReceivesWorkflowCancellationToken()
    {
        // The router sits mid-branch: a join source may not have outgoing
        // connections, so "branch-a" routes on to "branch-a-tail", which joins.
        var router = new TokenObservingRouter();
        using var cts = new CancellationTokenSource();

        var execution = await new Workflow<CounterState>("fork-branch-router-ct")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("branch-a", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "a"] }))
            .Job("branch-a-tail", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "a-tail"] }))
            .Job("branch-b", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "b"] }))
            .Job("merge", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork("branch-a", "branch-b"))
            .Then("branch-a", router)
            .Join(["branch-a-tail", "branch-b"], "merge", states => states[0])
            .Then("merge", Workflow.End)
            .RunAsync(new CounterState(), cts.Token);

        execution.Status.ShouldBe(ExecutionStatus.Completed);

        router.TokenWasCancellable.ShouldNotBeNull(
            "the branch never consulted the router at all");
        router.TokenWasCancellable!.Value.ShouldBeTrue(
            "an async router inside a fork branch must receive the workflow's CancellationToken, " +
            "not CancellationToken.None — otherwise it can never be cancelled with the workflow");
    }

    [Test]
    public async Task Fork_LoopInsideBranch_Iterates()
    {
        var execution = await new Workflow<CounterState>("fork-loop-in-branch")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("looper", (s, _) => Task.FromResult(s with { Value = s.Value + 1 }))
            .Job("branch-a-tail", (s, _) => Task.FromResult(s))
            .Job("branch-b", (s, _) => Task.FromResult(s))
            .Job("merge", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork("looper", "branch-b"))
            .Loop("looper", loopTarget: "looper", exitTarget: "branch-a-tail",
                  until: s => s.Value >= 3, maxIterations: 10)
            .Join(["branch-a-tail", "branch-b"], "merge",
                  states => states.MaxBy(s => s.Value)!)
            .Then("merge", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.FinalState.Value.ShouldBe(3,
            "a loop connection inside a fork branch must iterate until its condition — " +
            "the branch resolved only direct and router edges, so it ran once and silently stopped");
    }

    [Test]
    public async Task Fork_LoopInsideBranch_HonoursMaxIterations()
    {
        var execution = await new Workflow<CounterState>("fork-loop-maxiter")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("looper", (s, _) => Task.FromResult(s with { Value = s.Value + 1 }))
            .Job("branch-a-tail", (s, _) => Task.FromResult(s))
            .Job("branch-b", (s, _) => Task.FromResult(s))
            .Job("merge", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork("looper", "branch-b"))
            .Loop("looper", loopTarget: "looper", exitTarget: "branch-a-tail",
                  until: _ => false, maxIterations: 4)
            .Join(["branch-a-tail", "branch-b"], "merge",
                  states => states.MaxBy(s => s.Value)!)
            .Then("merge", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.FinalState.Value.ShouldBe(4,
            "the cap must bind inside a branch exactly as it does on the main path");
    }
}
