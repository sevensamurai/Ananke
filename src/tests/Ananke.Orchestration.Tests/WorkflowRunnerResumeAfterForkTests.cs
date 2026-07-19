using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Execution;
using Ananke.Orchestration.Routing;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Phase 6.3 — <see cref="WorkflowRunner"/> resume-after-fork tests.
/// Verifies that a workflow paused (via checkpoint or interrupt) at or after a fork
/// resumes correctly: all fork branches re-execute, the join fires, and the merged
/// state is correct.
/// </summary>
[TestFixture]
public class WorkflowRunnerResumeAfterForkTests
{
    private InMemoryCheckpointStore _store = null!;

    [SetUp]
    public void Setup() => _store = new InMemoryCheckpointStore();

    // ── helpers ──────────────────────────────────────────────────────────────

    private WorkflowDefinition<CounterState> BuildForkWorkflow(string name = "resume-fork") =>
        new Workflow<CounterState>(name)
            .Job("start", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("branch-a", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "a"], Value = s.Value + 10 }))
            .Job("branch-b", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "b"], Value = s.Value + 100 }))
            .Job("merge", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "merged"] }))
            .Then("start", Workflow.Fork("branch-a", "branch-b"))
            .Join(["branch-a", "branch-b"], "merge",
                states => new CounterState
                {
                    Value = states.Sum(x => x.Value),
                    Trail = [.. states.SelectMany(x => x.Trail)]
                })
            .Then("merge", Workflow.End)
            .UseCheckpointing(_store)
            .Build();

    // ── tests ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task ResumeAsync_FromBeforeFork_ExecutesBothBranchesAndMerges()
    {
        // Run until "start" produces a checkpoint, then simulate the runner
        // being restarted from that checkpoint (before the fork fires).
        var definition = BuildForkWorkflow("resume-before-fork");
        var runner = new WorkflowRunner(_store);

        // First run — will complete normally; we capture the checkpoint at "start"
        // by intercepting the store after the job runs.
        var initial = await runner.RunAsync(definition, new CounterState());
        initial.Status.ShouldBe(ExecutionStatus.Completed);

        // Rebuild a synthetic checkpoint at "start" so we can resume from that point.
        var syntheticCheckpoint = new Checkpoint<CounterState>
        {
            ExecutionId = Guid.NewGuid().ToString(),
            WorkflowName = definition.Name,
            CurrentJob = "start",
            State = new CounterState { Value = 1 },
            Status = ExecutionStatus.Running,
            History = [],
            CreatedAt = DateTimeOffset.UtcNow
        };

        var resumed = await runner.ResumeAsync(definition, syntheticCheckpoint);

        resumed.Status.ShouldBe(ExecutionStatus.Completed);
        resumed.Result!.Success.ShouldBeTrue();
        resumed.Result.FinalState.Trail.ShouldContain("a", "branch-a must execute on resume");
        resumed.Result.FinalState.Trail.ShouldContain("b", "branch-b must execute on resume");
        resumed.Result.FinalState.Trail.ShouldContain("merged", "merge job must fire on resume");
    }

    [Test]
    public async Task ResumeAsync_WithStateTransform_AppliedBeforeForkReplay()
    {
        var definition = BuildForkWorkflow("resume-transform");
        var runner = new WorkflowRunner(_store);

        var checkpoint = new Checkpoint<CounterState>
        {
            ExecutionId = Guid.NewGuid().ToString(),
            WorkflowName = definition.Name,
            CurrentJob = "start",
            State = new CounterState { Value = 1 },
            Status = ExecutionStatus.Running,
            History = [],
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Transform doubles Value before replay — branches see Value=2 instead of 1.
        var resumed = await runner.ResumeAsync(
            definition,
            checkpoint,
            s => s with { Value = s.Value * 2 });

        resumed.Status.ShouldBe(ExecutionStatus.Completed);
        // branch-a: 2+10=12, branch-b: 2+100=102 → merged = 114
        resumed.Result!.FinalState.Value.ShouldBe(114,
            "State transform must be applied before fork branches execute.");
    }

    [Test]
    public async Task ResumeAsync_FromInterruptedBeforeMerge_CompletesWorkflow()
    {
        // Build a workflow that saves a checkpoint after "branch-a" completes
        // (simulating an interrupt before "merge"). Resume must still reach Completed.
        var definition = BuildForkWorkflow("resume-before-merge");
        var runner = new WorkflowRunner(_store);

        // Full run to capture real checkpoint state after "start"
        await runner.RunAsync(definition, new CounterState());

        // Synthetic: interrupted BEFORE merge, both branches logically done
        var checkpoint = new Checkpoint<CounterState>
        {
            ExecutionId = Guid.NewGuid().ToString(),
            WorkflowName = definition.Name,
            CurrentJob = "branch-a",
            InterruptedBeforeJob = "merge",
            State = new CounterState { Value = 111, Trail = ["a", "b"] },
            Status = ExecutionStatus.Running,
            History = [],
            CreatedAt = DateTimeOffset.UtcNow
        };

        var resumed = await runner.ResumeAsync(definition, checkpoint);

        resumed.Status.ShouldBe(ExecutionStatus.Completed);
        resumed.Result!.FinalState.Trail.ShouldContain("merged",
            "Merge job must fire when resuming from an interrupt-before-merge checkpoint.");
    }

    [Test]
    public async Task ResumeAsync_Fork_BestEffortMode_OneFaultyBranch_Completes()
    {
        var definition = new Workflow<CounterState>("resume-fork-besteffort")
            .Job("start", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("ok-branch", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "ok"] }))
            .Job("bad-branch", (CounterState _, CancellationToken _) =>
                throw new InvalidOperationException("bad"))
            .Job("merge", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "merged"] }))
            .Then("start", Workflow.Fork(ForkMode.BestEffort, "ok-branch", "bad-branch"))
            .Join(["ok-branch", "bad-branch"], "merge", states => states[0])
            .Then("merge", Workflow.End)
            .UseCheckpointing(_store)
            .Build();

        var runner = new WorkflowRunner(_store);

        var checkpoint = new Checkpoint<CounterState>
        {
            ExecutionId = Guid.NewGuid().ToString(),
            WorkflowName = definition.Name,
            CurrentJob = "start",
            State = new CounterState { Value = 1 },
            Status = ExecutionStatus.Running,
            History = [],
            CreatedAt = DateTimeOffset.UtcNow
        };

        var resumed = await runner.ResumeAsync(definition, checkpoint);

        // BestEffort — workflow completes even when one branch fails
        resumed.Status.ShouldBe(ExecutionStatus.Completed,
            "BestEffort fork resumed from checkpoint must complete despite a faulting branch.");
        resumed.Result!.FinalState.Trail.ShouldContain("ok");
        resumed.Result.FinalState.Trail.ShouldContain("merged");
    }

    [Test]
    public async Task RunAsync_ForkThenResume_JobsExecutedCountIsCorrect()
    {
        var definition = BuildForkWorkflow("fork-jobs-count");
        var runner = new WorkflowRunner(_store);

        var checkpoint = new Checkpoint<CounterState>
        {
            ExecutionId = Guid.NewGuid().ToString(),
            WorkflowName = definition.Name,
            CurrentJob = "start",
            State = new CounterState { Value = 1 },
            Status = ExecutionStatus.Running,
            History = [],
            CreatedAt = DateTimeOffset.UtcNow
        };

        var resumed = await runner.ResumeAsync(definition, checkpoint);

        resumed.Status.ShouldBe(ExecutionStatus.Completed);
        // branch-a, branch-b, merge = 3 jobs run after resume
        resumed.Result!.JobsExecuted.ShouldBeGreaterThanOrEqualTo(3,
            "At minimum both fork branches and the merge job must be counted.");
    }
}
