using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Checkpointing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Tests for H-6: <c>ResumeAsync</c> must be fully async end-to-end.
/// Specifically exercises the <c>DecideAsync</c> router path on resume,
/// which previously blocked with <c>.GetAwaiter().GetResult()</c>.
/// </summary>
[TestFixture]
public class ResumeAsyncRouterTests
{
    private InMemoryCheckpointStore _store = null!;

    [SetUp]
    public void Setup() => _store = new InMemoryCheckpointStore();

    // -- Direct connection resume (baseline) --------------------------

    [Test]
    public async Task ResumeAsync_DirectConnection_ContinuesFromCheckpoint()
    {
        var callCount = 0;

        var workflow = new Workflow<CounterState>("resume-direct")
            .Job("a", (s, _) => Task.FromResult(s with { Value = 1, Trail = [.. s.Trail, "a"] }))
            .Job("b", (s, _) =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("transient");
                return Task.FromResult(s with { Value = 2, Trail = [.. s.Trail, "b"] });
            })
            .Job("c", (s, _) => Task.FromResult(s with { Value = 3, Trail = [.. s.Trail, "c"] }))
            .Chain("a", "b", "c", Workflow.End)
            .UseCheckpointing(_store);

        var first = await workflow.RunAsync(new CounterState());
        first.Status.ShouldBe(ExecutionStatus.Faulted);

        var resumed = await workflow.ResumeAsync(first.Id);
        resumed.Status.ShouldBe(ExecutionStatus.Completed);
        resumed.Result!.FinalState.Trail.ShouldBe(new[] { "a", "b", "c" });
    }

    // -- DecideAsync router resume — this path previously deadlocked --

    [Test]
    public async Task ResumeAsync_WithDecideAsyncRouter_ResolvesWithoutBlocking()
    {
        var callCount = 0;

        var workflow = new Workflow<CounterState>("resume-router")
            .Job("start", (s, _) => Task.FromResult(s with { Value = 10 }))
            .Job("high", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "high"] }))
            .Job("low",  (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "low"] }))
            .Job("after-route", (s, _) =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("transient after route");
                return Task.FromResult(s with { Trail = [.. s.Trail, "done"] });
            })
            // Router outgoing from "start" — previously .GetAwaiter().GetResult() on resume
            .Then("start", Workflow.DecideAsync<CounterState>(async s =>
            {
                await Task.Yield(); // Force async continuation, would deadlock under sync
                return s.Value >= 5 ? "high" : "low";
            }))
            .Then("high", "after-route")
            .Then("low",  "after-route")
            .Then("after-route", Workflow.End)
            .UseCheckpointing(_store);

        // First run: start ? route ? high ? after-route (throws)
        var first = await workflow.RunAsync(new CounterState());
        first.Status.ShouldBe(ExecutionStatus.Faulted);

        // Checkpoint is on "high" (last successful job before after-route failed)
        _store.Count.ShouldBe(1);

        // Resume — must resolve "after route" target async without blocking
        var resumed = await workflow.ResumeAsync(first.Id);
        resumed.Status.ShouldBe(ExecutionStatus.Completed);
        resumed.Result!.FinalState.Trail.ShouldContain("done");
    }

    [Test]
    public async Task ResumeAsync_RouterReturnsCorrectBranch_BasedOnCheckpointState()
    {
        // Verifies that the state stored in the checkpoint is used by the router,
        // not the initial state.
        var callCount = 0;

        var workflow = new Workflow<CounterState>("resume-router-state")
            .Job("start",  (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("high",   (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "high"] }))
            .Job("low",    (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "low"] }))
            .Job("finish", (s, _) =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("transient");
                return Task.FromResult(s with { Trail = [.. s.Trail, "finish"] });
            })
            .Then("start", Workflow.DecideAsync<CounterState>(async s =>
            {
                await Task.Yield();
                return s.Value >= 5 ? "high" : "low";
            }))
            .Then("high",  "finish")
            .Then("low",   "finish")
            .Then("finish", Workflow.End)
            .UseCheckpointing(_store);

        // Value = 1 ? router picks "low"
        var first = await workflow.RunAsync(new CounterState());
        first.Status.ShouldBe(ExecutionStatus.Faulted);

        var resumed = await workflow.ResumeAsync(first.Id);
        resumed.Status.ShouldBe(ExecutionStatus.Completed);
        // Must have gone through "low", not "high"
        resumed.Result!.FinalState.Trail.ShouldContain("low");
        resumed.Result.FinalState.Trail.ShouldNotContain("high");
    }

    // -- stateTransform overload --------------------------------------

    [Test]
    public async Task ResumeAsync_StateTransform_TransformedStateReachesNextJob()
    {
        // The stateTransform is applied to the checkpoint state before the next job
        // runs. This verifies the transformed value is visible to jobs after resume.
        var callCount = 0;
        int? valueSeenByFinish = null;

        var workflow = new Workflow<CounterState>("resume-transform")
            .Job("start",  (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("middle", (s, _) =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("transient");
                return Task.FromResult(s with { Trail = [.. s.Trail, "middle"] });
            })
            .Job("finish", (s, _) =>
            {
                valueSeenByFinish = s.Value;
                return Task.FromResult(s with { Trail = [.. s.Trail, "finish"] });
            })
            .Chain("start", "middle", "finish", Workflow.End)
            .UseCheckpointing(_store);

        // First run: start sets Value=1, middle throws. Checkpoint is on "start".
        var first = await workflow.RunAsync(new CounterState());
        first.Status.ShouldBe(ExecutionStatus.Faulted);

        // Resume with transform that bumps Value to 99. Middle and finish see 99.
        var resumed = await workflow.ResumeAsync(first.Id, s => s with { Value = 99 });
        resumed.Status.ShouldBe(ExecutionStatus.Completed);
        resumed.Result!.FinalState.Trail.ShouldContain("middle");
        resumed.Result.FinalState.Trail.ShouldContain("finish");
        valueSeenByFinish.ShouldBe(99);
    }

    // -- Loop resume (uses separate path — ensure no regression) -----

    [Test]
    public async Task ResumeAsync_LoopConnection_ResumesCorrectly()
    {
        var callCount = 0;

        var workflow = new Workflow<CounterState>("resume-loop")
            .Job("increment", (s, _) => Task.FromResult(s with { Value = s.Value + 1 }))
            .Job("after", (s, _) =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("transient");
                return Task.FromResult(s with { Trail = [.. s.Trail, "done"] });
            })
            .Loop("increment", loopTarget: "increment", exitTarget: "after",
                until: s => s.Value >= 3, maxIterations: 10)
            .Then("after", Workflow.End)
            .UseCheckpointing(_store);

        var first = await workflow.RunAsync(new CounterState());
        first.Status.ShouldBe(ExecutionStatus.Faulted);

        var resumed = await workflow.ResumeAsync(first.Id);
        resumed.Status.ShouldBe(ExecutionStatus.Completed);
        resumed.Result!.FinalState.Trail.ShouldContain("done");
    }
}
