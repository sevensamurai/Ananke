using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Routing;
using Ananke.Orchestration.Streaming;
using Shouldly;

namespace Ananke.Orchestration.Tests;

public record LoopState
{
    public int Iteration { get; init; }
    public double Score { get; init; }
    public List<string> Trail { get; init; } = [];
}

[TestFixture]
public class LoopTests
{
    // ── Loop primitive: condition-based exit ─────────────────────

    [Test]
    public async Task Loop_ExitsWhenConditionMet()
    {
        var workflow = new Workflow<LoopState>("cond-exit")
            .Job("work", (s, _) => Task.FromResult(
                s with { Iteration = s.Iteration + 1, Score = s.Iteration + 1 >= 3 ? 1.0 : 0.1 }))
            .Loop("work", loopTarget: "work", exitTarget: Workflow.End,
                  until: s => s.Score >= 0.9, maxIterations: 10);

        var result = await workflow.RunAsync(new LoopState());

        result.State.Score.ShouldBeGreaterThanOrEqualTo(0.9);
        result.State.Iteration.ShouldBe(3);
        result.Status.ShouldBe(ExecutionStatus.Completed);
    }

    // ── Loop primitive: max-iterations exit ──────────────────────

    [Test]
    public async Task Loop_ExitsAtMaxIterations()
    {
        var workflow = new Workflow<LoopState>("max-exit")
            .Job("work", (s, _) => Task.FromResult(
                s with { Iteration = s.Iteration + 1, Score = 0.1 }))
            .Loop("work", loopTarget: "work", exitTarget: Workflow.End,
                  until: s => s.Score >= 0.9, maxIterations: 4);

        var result = await workflow.RunAsync(new LoopState());

        result.State.Iteration.ShouldBe(4);
        result.Status.ShouldBe(ExecutionStatus.Completed);
    }

    // ── Loop with exit to another job ────────────────────────────

    [Test]
    public async Task Loop_ExitsToNamedJob()
    {
        var workflow = new Workflow<LoopState>("exit-to-job")
            .Job("refine", (s, _) => Task.FromResult(
                s with { Iteration = s.Iteration + 1, Score = 1.0 }))
            .Job("finalize", (s, _) => Task.FromResult(
                s with { Trail = [.. s.Trail, "finalized"] }))
            .Loop("refine", loopTarget: "refine", exitTarget: "finalize",
                  until: s => s.Score >= 0.9, maxIterations: 5)
            .Then("finalize", Workflow.End);

        var result = await workflow.RunAsync(new LoopState());

        result.State.Trail.ShouldContain("finalized");
        result.State.Iteration.ShouldBe(1);
    }

    // ── Two-job loop (generate → critique) ───────────────────────

    [Test]
    public async Task Loop_TwoJobCycle_GenerateCritique()
    {
        var workflow = new Workflow<LoopState>("gen-critique")
            .Job("generate", (s, _) => Task.FromResult(
                s with { Iteration = s.Iteration + 1, Trail = [.. s.Trail, "gen"] }))
            .Job("critique", (s, _) => Task.FromResult(
                s with { Score = s.Iteration >= 2 ? 0.95 : 0.3, Trail = [.. s.Trail, "crit"] }))
            .Then("generate", "critique")
            .Loop("critique", loopTarget: "generate", exitTarget: Workflow.End,
                  until: s => s.Score >= 0.9, maxIterations: 5);

        var result = await workflow.RunAsync(new LoopState());

        result.State.Score.ShouldBeGreaterThanOrEqualTo(0.9);
        result.State.Iteration.ShouldBe(2);
        result.State.Trail.ShouldBe(["gen", "crit", "gen", "crit"]);
    }

    // ── LoopExited stream event ──────────────────────────────────

    [Test]
    public async Task Loop_EmitsLoopExitedEvent_ConditionMet()
    {
        var workflow = new Workflow<LoopState>("stream-cond")
            .Job("work", (s, _) => Task.FromResult(
                s with { Iteration = s.Iteration + 1, Score = 1.0 }))
            .Loop("work", loopTarget: "work", exitTarget: Workflow.End,
                  until: s => s.Score >= 0.9, maxIterations: 10);

        LoopExited<LoopState>? loopEvent = null;

        await foreach (var evt in workflow.StreamAsync(new LoopState()))
        {
            if (evt is LoopExited<LoopState> le)
                loopEvent = le;
        }

        loopEvent.ShouldNotBeNull();
        loopEvent.LoopFrom.ShouldBe("work");
        loopEvent.LoopTarget.ShouldBe("work");
        loopEvent.Reason.ShouldBe(LoopExitReason.ConditionMet);
        loopEvent.IterationsCompleted.ShouldBe(1);
    }

    [Test]
    public async Task Loop_EmitsLoopExitedEvent_MaxIterations()
    {
        var workflow = new Workflow<LoopState>("stream-max")
            .Job("work", (s, _) => Task.FromResult(
                s with { Iteration = s.Iteration + 1 }))
            .Loop("work", loopTarget: "work", exitTarget: Workflow.End,
                  until: _ => false, maxIterations: 3);

        LoopExited<LoopState>? loopEvent = null;

        await foreach (var evt in workflow.StreamAsync(new LoopState()))
        {
            if (evt is LoopExited<LoopState> le)
                loopEvent = le;
        }

        loopEvent.ShouldNotBeNull();
        loopEvent.Reason.ShouldBe(LoopExitReason.MaxIterationsReached);
        loopEvent.IterationsCompleted.ShouldBe(3);
    }

    // ── Validation ──────────────────────────────────────────────

    [Test]
    public void Loop_UndefinedLoopTarget_Throws()
    {
#pragma warning disable ANANKE001 // intentional: testing runtime validation of undefined loop target
        var workflow = new Workflow<LoopState>("bad-loop")
            .Job("a", (s, _) => Task.FromResult(s))
            .Loop("a", loopTarget: "nonexistent", exitTarget: Workflow.End,
                  until: _ => true, maxIterations: 3);
#pragma warning restore ANANKE001

        Should.Throw<InvalidOperationException>(() => workflow.Build())
            .Message.ShouldContain("nonexistent");
    }

    [Test]
    public void Loop_UndefinedExitTarget_Throws()
    {
#pragma warning disable ANANKE001 // intentional: testing runtime validation of undefined exit target
        var workflow = new Workflow<LoopState>("bad-exit")
            .Job("a", (s, _) => Task.FromResult(s))
            .Loop("a", loopTarget: "a", exitTarget: "nonexistent",
                  until: _ => true, maxIterations: 3);
#pragma warning restore ANANKE001

        Should.Throw<InvalidOperationException>(() => workflow.Build())
            .Message.ShouldContain("nonexistent");
    }

    [Test]
    public void Loop_MaxIterationsLessThan1_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new Workflow<LoopState>("bad-max")
                .Job("a", (s, _) => Task.FromResult(s))
                .Loop("a", loopTarget: "a", exitTarget: Workflow.End,
                      until: _ => true, maxIterations: 0));
    }

    [Test]
    public void Loop_UndefinedSource_Throws()
    {
#pragma warning disable ANANKE001 // intentional: testing runtime validation of undefined loop source
        var workflow = new Workflow<LoopState>("bad-source")
            .Job("a", (s, _) => Task.FromResult(s))
            .Loop("nonexistent", loopTarget: "a", exitTarget: Workflow.End,
                  until: _ => true, maxIterations: 3);
#pragma warning restore ANANKE001

        Should.Throw<InvalidOperationException>(() => workflow.Build())
            .Message.ShouldContain("nonexistent");
    }

    // ── Checkpoint: loop counters persist across resume ──────────

    [Test]
    public async Task Loop_CheckpointPreservesLoopCounters()
    {
        var store = new InMemoryCheckpointStore();

        var workflow = new Workflow<LoopState>("checkpoint-loop")
            .Job("work", (s, _) => Task.FromResult(
                s with { Iteration = s.Iteration + 1, Score = 0.1 }))
            .Job("after", (s, _) => Task.FromResult(
                s with { Trail = [.. s.Trail, "after"] }))
            .Loop("work", loopTarget: "work", exitTarget: "after",
                  until: s => s.Score >= 0.9, maxIterations: 5)
            .Then("after", Workflow.End)
            .InterruptAfter("work")
            .UseCheckpointing(store);

        // First run — executes one loop iteration, then interrupts after "work".
        // The loop hasn't been evaluated yet at checkpoint time (InterruptAfter
        // fires before loop resolution), so the counter is 0.
        var exec1 = await workflow.RunAsync(new LoopState());
        exec1.Status.ShouldBe(ExecutionStatus.Interrupted);
        exec1.State.Iteration.ShouldBe(1);

        // Resume — the loop evaluates (counter → 1), routes back to "work".
        // InterruptAfter fires again after each "work" execution.
        // Resume repeatedly until the loop cap is hit.
        var currentId = exec1.Id;
        WorkflowExecution<LoopState> latest = exec1;
        while (latest.Status == ExecutionStatus.Interrupted)
        {
            latest = await workflow.ResumeAsync(currentId);
            currentId = latest.Id;
        }

        latest.Status.ShouldBe(ExecutionStatus.Completed);
        latest.State.Iteration.ShouldBe(5);
        latest.State.Trail.ShouldContain("after");
    }

    // ── Self-loop (same job as source and target) ────────────────

    [Test]
    public async Task Loop_SelfLoop_WorksCorrectly()
    {
        var workflow = new Workflow<LoopState>("self-loop")
            .Job("refine", (s, _) => Task.FromResult(
                s with { Iteration = s.Iteration + 1, Score = s.Iteration + 1 >= 3 ? 1.0 : 0.0 }))
            .Loop("refine", loopTarget: "refine", exitTarget: Workflow.End,
                  until: s => s.Score >= 0.9, maxIterations: 10);

        var result = await workflow.RunAsync(new LoopState());

        result.State.Iteration.ShouldBe(3);
        result.State.Score.ShouldBe(1.0);
    }

    // ── First iteration satisfies condition → single execution ──

    [Test]
    public async Task Loop_ConditionMetOnFirstIteration_ExitsImmediately()
    {
        var workflow = new Workflow<LoopState>("first-pass")
            .Job("work", (s, _) => Task.FromResult(s with { Score = 1.0 }))
            .Loop("work", loopTarget: "work", exitTarget: Workflow.End,
                  until: s => s.Score >= 0.9, maxIterations: 10);

        var result = await workflow.RunAsync(new LoopState());

        result.State.Score.ShouldBe(1.0);
        result.History.Count(h => h.JobName == "work").ShouldBe(1);
    }

    // ── Loop counter resets after exit ──────────────────────────

    [Test]
    public async Task Loop_CounterResetsAfterExit()
    {
        // Two separate runs of the same workflow should both start at iteration 0
        var workflow = new Workflow<LoopState>("counter-reset")
            .Job("work", (s, _) => Task.FromResult(
                s with { Iteration = s.Iteration + 1, Score = 0.0 }))
            .Loop("work", loopTarget: "work", exitTarget: Workflow.End,
                  until: _ => false, maxIterations: 3);

        var result1 = await workflow.RunAsync(new LoopState());
        result1.State.Iteration.ShouldBe(3);

        var result2 = await workflow.RunAsync(new LoopState());
        result2.State.Iteration.ShouldBe(3);
    }
}
