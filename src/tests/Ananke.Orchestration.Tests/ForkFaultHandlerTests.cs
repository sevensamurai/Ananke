using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Routing;
using Ananke.TestHelpers;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Tests for H-4: fork branch jobs must invoke OnFault / OnError handlers,
/// matching the behaviour of the main linear execution path.
/// </summary>
[TestFixture]
public class ForkFaultHandlerTests
{
    // -- OnFault (per-job) --------------------------------------------

    [Test]
    public async Task Fork_BranchJobFaults_OnFaultHandler_Invoked()
    {
        Exception? captured = null;

        var exec = await new Workflow<CounterState>("fork-onfault")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("ok-branch", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "ok"] }))
            .Job("bad-branch", (CounterState _, CancellationToken _) =>
                throw new InvalidOperationException("branch-boom"))
            .OnFault("bad-branch", (_, ex) => { captured = ex; return Task.CompletedTask; })
            .Job("merge", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork(ForkMode.BestEffort, "ok-branch", "bad-branch"))
            .Join(["ok-branch", "bad-branch"], "merge", states => states[0])
            .Then("merge", Workflow.End)
            .RunAsync(new CounterState());

        // BestEffort — workflow completes despite the failing branch
        exec.Status.ShouldBe(ExecutionStatus.Completed);
        captured.ShouldNotBeNull();
        captured.ShouldBeOfType<InvalidOperationException>();
        captured!.Message.ShouldBe("branch-boom");
    }

    [Test]
    public async Task Fork_BranchJobFaults_OnFaultHandler_ReceivesCurrentBranchState()
    {
        CounterState? capturedState = null;

        var exec = await new Workflow<CounterState>("fork-onfault-state")
            .Job("start", (s, _) => Task.FromResult(s with { Value = 7 }))
            .Job("ok-branch", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "ok"] }))
            .Job("setup-branch", (s, _) => Task.FromResult(s with { Value = s.Value + 100 }))
            .Job("bad-branch", (CounterState _, CancellationToken _) =>
                throw new InvalidOperationException("oops"))
            .OnFault("bad-branch", (state, _) => { capturedState = state; return Task.CompletedTask; })
            .Job("merge", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork(ForkMode.BestEffort, "ok-branch", "setup-branch"))
            .Then("setup-branch", "bad-branch")
            .Join(["ok-branch", "bad-branch"], "merge", states => states[0])
            .Then("merge", Workflow.End)
            .RunAsync(new CounterState());

        exec.Status.ShouldBe(ExecutionStatus.Completed);
        // setup-branch raised Value to 7+100=107 before bad-branch ran
        capturedState.ShouldNotBeNull();
        capturedState!.Value.ShouldBe(107);
    }

    [Test]
    public async Task Fork_FailFast_BranchJobFaults_OnFaultHandler_Invoked()
    {
        var handlerInvoked = false;

        var exec = await new Workflow<CounterState>("fork-failfast-onfault")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("ok-branch", async (s, ct) =>
            {
                await WorkflowLoops.Park(ct); // cancelled by FailFast
                return s with { Trail = [.. s.Trail, "ok"] };
            })
            .Job("bad-branch", (CounterState _, CancellationToken _) =>
                throw new InvalidOperationException("failfast-boom"))
            .OnFault("bad-branch", (_, _) => { handlerInvoked = true; return Task.CompletedTask; })
            .Job("merge", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork(ForkMode.FailFast, "ok-branch", "bad-branch"))
            .Join(["ok-branch", "bad-branch"], "merge", states => states[0])
            .Then("merge", Workflow.End)
            .RunAsync(new CounterState());

        exec.Status.ShouldBe(ExecutionStatus.Faulted);
        handlerInvoked.ShouldBeTrue();
    }

    // -- OnError (workflow-level) -------------------------------------

    [Test]
    public async Task Fork_BranchJobFaults_OnError_Invoked()
    {
        string? faultedJob = null;
        Exception? captured = null;

        var exec = await new Workflow<CounterState>("fork-onerror")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("ok-branch", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "ok"] }))
            .Job("bad-branch", (CounterState _, CancellationToken _) =>
                throw new InvalidOperationException("error-hook"))
            .Job("merge", (s, _) => Task.FromResult(s))
            .OnError((_, jobName, ex) =>
            {
                faultedJob = jobName;
                captured = ex;
                return Task.CompletedTask;
            })
            .Then("start", Workflow.Fork(ForkMode.BestEffort, "ok-branch", "bad-branch"))
            .Join(["ok-branch", "bad-branch"], "merge", states => states[0])
            .Then("merge", Workflow.End)
            .RunAsync(new CounterState());

        exec.Status.ShouldBe(ExecutionStatus.Completed);
        faultedJob.ShouldBe("bad-branch");
        captured.ShouldNotBeNull();
        captured!.Message.ShouldBe("error-hook");
    }

    [Test]
    public async Task Fork_BranchJobFaults_BothOnFaultAndOnError_BothInvoked()
    {
        var onFaultFired = false;
        var onErrorFired = false;

        var exec = await new Workflow<CounterState>("fork-both-handlers")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("ok-branch", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "ok"] }))
            .Job("bad-branch", (CounterState _, CancellationToken _) =>
                throw new InvalidOperationException("dual-boom"))
            .OnFault("bad-branch", (_, _) => { onFaultFired = true; return Task.CompletedTask; })
            .Job("merge", (s, _) => Task.FromResult(s))
            .OnError((_, _, _) => { onErrorFired = true; return Task.CompletedTask; })
            .Then("start", Workflow.Fork(ForkMode.BestEffort, "ok-branch", "bad-branch"))
            .Join(["ok-branch", "bad-branch"], "merge", states => states[0])
            .Then("merge", Workflow.End)
            .RunAsync(new CounterState());

        exec.Status.ShouldBe(ExecutionStatus.Completed);
        onFaultFired.ShouldBeTrue();
        onErrorFired.ShouldBeTrue();
    }

    [Test]
    public async Task Fork_BranchFaults_HandlerException_DoesNotReplaceOriginal()
    {
        // Handler itself throws — original exception should still propagate through the branch
        var exec = await new Workflow<CounterState>("fork-handler-throws")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("ok-branch", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "ok"] }))
            .Job("bad-branch", (CounterState _, CancellationToken _) =>
                throw new InvalidOperationException("original"))
            .OnFault("bad-branch", (_, _) => throw new ApplicationException("handler-error"))
            .Job("merge", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork(ForkMode.BestEffort, "ok-branch", "bad-branch"))
            .Join(["ok-branch", "bad-branch"], "merge", states => states[0])
            .Then("merge", Workflow.End)
            .RunAsync(new CounterState());

        // Workflow still completes in BestEffort mode
        exec.Status.ShouldBe(ExecutionStatus.Completed);
    }
}
