using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class WorkflowRunnerTests
{
    [Test]
    public async Task RunAsync_SingleJob_ExecutesAndCompletes()
    {
        var execution = await new Workflow<CounterState>("single")
            .Job("increment", (s, _) => Task.FromResult(s with { Value = s.Value + 1 }))
            .Then("increment", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result.ShouldNotBeNull();
        execution.Result.Success.ShouldBeTrue();
        execution.Result.FinalState.Value.ShouldBe(1);
        execution.Result.JobsExecuted.ShouldBe(1);
    }

    [Test]
    public async Task RunAsync_LinearChain_ExecutesInOrder()
    {
        var execution = await new Workflow<CounterState>("chain")
            .Job("step-a", (s, _) => Task.FromResult(s with
            {
                Value = s.Value + 1,
                Trail = [.. s.Trail, "a"]
            }))
            .Job("step-b", (s, _) => Task.FromResult(s with
            {
                Value = s.Value + 10,
                Trail = [.. s.Trail, "b"]
            }))
            .Job("step-c", (s, _) => Task.FromResult(s with
            {
                Value = s.Value + 100,
                Trail = [.. s.Trail, "c"]
            }))
            .Then("step-a", "step-b")
            .Then("step-b", "step-c")
            .Then("step-c", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.Success.ShouldBeTrue();
        execution.Result.FinalState.Value.ShouldBe(111);
        execution.Result.FinalState.Trail.ShouldBe(new[] { "a", "b", "c" });
        execution.Result.JobsExecuted.ShouldBe(3);
    }

    [Test]
    public async Task RunAsync_WithDecide_RoutesCorrectly()
    {
        var execution = await new Workflow<CounterState>("decide")
            .Job("start", (s, _) => Task.FromResult(s with { Value = 5 }))
            .Job("high", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "high"] }))
            .Job("low", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "low"] }))
            .Then("start", Workflow.Decide<CounterState>(s =>
                s.Value >= 5 ? "high" : "low"))
            .Then("high", Workflow.End)
            .Then("low", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.FinalState.Trail.ShouldBe(new[] { "high" });
    }

    [Test]
    public async Task RunAsync_WithDecide_RoutesToAlternatePath()
    {
        var execution = await new Workflow<CounterState>("decide-low")
            .Job("start", (s, _) => Task.FromResult(s with { Value = 2 }))
            .Job("high", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "high"] }))
            .Job("low", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "low"] }))
            .Then("start", Workflow.Decide<CounterState>(s =>
                s.Value >= 5 ? "high" : "low"))
            .Then("high", Workflow.End)
            .Then("low", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.FinalState.Trail.ShouldBe(new[] { "low" });
    }

    [Test]
    public async Task RunAsync_WithLoop_EventuallyExits()
    {
        var execution = await new Workflow<CounterState>("loop")
            .Job("increment", (s, _) => Task.FromResult(s with { Value = s.Value + 1 }))
            .Job("done", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "done"] }))
            .Then("increment", Workflow.Decide<CounterState>(s =>
                s.Value >= 3 ? "done" : "increment"))
            .Then("done", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.FinalState.Value.ShouldBe(3);
        execution.Result.FinalState.Trail.ShouldBe(new[] { "done" });
        execution.Result.JobsExecuted.ShouldBe(4); // 3 increments + 1 done
    }

    [Test]
    public async Task RunAsync_JobThrows_ResultIsFaulted()
    {
        var execution = await new Workflow<CounterState>("failing")
            .Job("boom", (_, _) => throw new InvalidOperationException("Something broke"))
            .Then("boom", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Faulted);
        execution.Result.ShouldNotBeNull();
        execution.Result.Success.ShouldBeFalse();
        execution.Result.Error.ShouldBe("Something broke");
        execution.Result.Exception.ShouldBeOfType<InvalidOperationException>();
    }

    [Test]
    public async Task RunAsync_CancellationRequested_ResultIsCancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var execution = await new Workflow<CounterState>("cancelled")
            .Job("never-runs", (s, _) => Task.FromResult(s))
            .Then("never-runs", Workflow.End)
            .RunAsync(new CounterState(), cts.Token);

        execution.Status.ShouldBe(ExecutionStatus.Cancelled);
        execution.Result!.Success.ShouldBeFalse();
        execution.Result.Error.ShouldBe("Workflow cancelled.");
    }

    [Test]
    public async Task RunAsync_JobTimeout_FaultsWithTimeoutException()
    {
        var execution = await new Workflow<CounterState>("timeout-test")
            .Job("slow", async (s, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                return s;
            })
            .Then("slow", Workflow.End)
            .Timeout("slow", TimeSpan.FromMilliseconds(150))
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Faulted);
        execution.Result!.Success.ShouldBeFalse();
        execution.History.Count.ShouldBe(1);
        execution.History[0].Success.ShouldBeFalse();
        execution.History[0].Error?.ShouldContain("timed out");
    }

    [Test]
    public async Task RunAsync_Timeout_DoesNotAffectFasterJobs()
    {
        var execution = await new Workflow<CounterState>("fast-with-timeout")
            .Job("fast", (s, _) => Task.FromResult(s with { Value = 42 }))
            .Then("fast", Workflow.End)
            .Timeout("fast", TimeSpan.FromSeconds(5))
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.FinalState.Value.ShouldBe(42);
    }

    [Test]
    public async Task RunAsync_TracksHistory()
    {
        var execution = await new Workflow<CounterState>("history")
            .Job("a", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("b", (s, _) => Task.FromResult(s with { Value = 2 }))
            .Then("a", "b")
            .Then("b", Workflow.End)
            .RunAsync(new CounterState());

        execution.History.Count.ShouldBe(2);
        execution.History[0].JobName.ShouldBe("a");
        execution.History[0].Success.ShouldBeTrue();
        execution.History[1].JobName.ShouldBe("b");
        execution.History[1].Success.ShouldBeTrue();

        execution.Result!.History.ShouldBe(execution.History);
    }

    [Test]
    public async Task RunAsync_FailedJobRecordedInHistory()
    {
        var execution = await new Workflow<CounterState>("fail-history")
            .Job("ok", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("boom", (_, _) => throw new InvalidOperationException("fail"))
            .Then("ok", "boom")
            .Then("boom", Workflow.End)
            .RunAsync(new CounterState());

        execution.History.Count.ShouldBe(2);
        execution.History[0].JobName.ShouldBe("ok");
        execution.History[0].Success.ShouldBeTrue();
        execution.History[1].JobName.ShouldBe("boom");
        execution.History[1].Success.ShouldBeFalse();
        execution.History[1].Error.ShouldBe("fail");
    }

    [Test]
    public async Task RunAsync_OnEnterOnExit_CalledInOrder()
    {
        var events = new List<string>();

        var execution = await new Workflow<CounterState>("lifecycle")
            .Job("work", (s, _) =>
            {
                events.Add("execute");
                return Task.FromResult(s with { Value = 42 });
            })
            .OnEnter("work", _ => { events.Add("enter"); return Task.CompletedTask; })
            .OnExit("work", _ => { events.Add("exit"); return Task.CompletedTask; })
            .Then("work", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        events.ShouldBe(new[] { "enter", "execute", "exit" });
    }

    [Test]
    public async Task RunAsync_TotalDurationIsPositive()
    {
        var execution = await new Workflow<CounterState>("timed")
            .Job("wait", async (s, _) =>
            {
                await Task.Delay(10);
                return s;
            })
            .Then("wait", Workflow.End)
            .RunAsync(new CounterState());

        execution.Result!.TotalDuration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Test]
    public async Task RunAsync_ExecutionId_IsUnique()
    {
        var workflow = new Workflow<CounterState>("id-test")
            .Job("noop", (s, _) => Task.FromResult(s))
            .Then("noop", Workflow.End);

        var exec1 = await workflow.RunAsync(new CounterState());
        var exec2 = await workflow.RunAsync(new CounterState());

        exec1.Id.ShouldNotBe(exec2.Id);
    }

    [Test]
    public async Task RunAsync_Chain_ExecutesInOrder()
    {
        var execution = await new Workflow<CounterState>("chained")
            .Job("a", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "a"] }))
            .Job("b", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "b"] }))
            .Job("c", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "c"] }))
            .Chain("a", "b", "c", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.FinalState.Trail.ShouldBe(new[] { "a", "b", "c" });
        execution.Result.JobsExecuted.ShouldBe(3);
    }

    [Test]
    public async Task RunAsync_ChainThenDecide_MixedWiring()
    {
        var execution = await new Workflow<CounterState>("mixed")
            .Job("init", (s, _) => Task.FromResult(s with { Value = 10 }))
            .Job("validate", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "validated"] }))
            .Job("approve", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "approved"] }))
            .Job("reject", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "rejected"] }))
            .Chain("init", "validate")
            .Then("validate", Workflow.Decide<CounterState>(s =>
                s.Value >= 5 ? "approve" : "reject"))
            .Then("approve", Workflow.End)
            .Then("reject", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.FinalState.Trail.ShouldBe(new[] { "validated", "approved" });
    }
}
