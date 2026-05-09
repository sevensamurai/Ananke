using Ananke.Orchestration.Workflows;
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
}
