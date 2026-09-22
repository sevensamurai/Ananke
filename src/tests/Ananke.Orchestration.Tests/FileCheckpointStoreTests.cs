using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Patterns;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Workflows;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// A paused run that outlives the process that paused it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The distance between the tier's claim and what shipped.</b> A workflow that runs for a long
/// time and asks a person only when it must spends most of its life waiting — and what it is waiting
/// to be told is not in the plan tree. The question, its options, the change count and the halt are
/// all workflow state, and workflow state had one built-in store, in memory. The demonstrated pause
/// required the process to stay alive while somebody thought.
/// </para>
/// <para>
/// So the test that matters here is the last one: a run stops for a person, the process it was in
/// goes away, and a different one picks the question up and answers it.
/// </para>
/// </remarks>
[TestFixture]
public class FileCheckpointStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    private sealed record Counter
    {
        public int Value { get; init; }
    }

    private sealed record Delivery
    {
        public PlanCoordination? Coordination { get; init; }
    }

    [Test]
    public async Task ACheckpoint_SurvivesANewStoreOverTheSameFolder()
    {
        await InAFolder(async root =>
        {
            // The checkpoint expires a week after T0, so the stores read it at T0 rather than today.
            var clock = new FakeTimeProvider();
            clock.SetUtcNow(T0);

            await new FileCheckpointStore(root, clock).SaveAsync(Checkpoint("run-1", new Counter { Value = 7 }));

            // A different instance, reading the same files. Nothing was carried in memory.
            var resumed = await new FileCheckpointStore(root, clock).LoadAsync<Counter>("run-1");

            resumed.ShouldNotBeNull().State.Value.ShouldBe(7);
            resumed.CurrentJob.ShouldBe("choose");
            resumed.Status.ShouldBe(ExecutionStatus.Interrupted);
        });
    }

    [Test]
    public async Task AnExpiredCheckpoint_IsNotReturned()
    {
        await InAFolder(async root =>
        {
            var clock = new FakeTimeProvider();
            clock.SetUtcNow(T0);

            var store = new FileCheckpointStore(root, clock);
            await store.SaveAsync(Checkpoint("run-1", new Counter(), expires: T0.AddMinutes(5)));

            (await store.ExistsAsync("run-1")).ShouldBeTrue();

            clock.Advance(TimeSpan.FromMinutes(6));

            (await store.LoadAsync<Counter>("run-1")).ShouldBeNull();
            (await store.ExistsAsync("run-1")).ShouldBeFalse();
        });
    }

    [Test]
    public async Task CleanupExpired_RemovesWhatHasExpiredAndLeavesWhatHasNot()
    {
        await InAFolder(async root =>
        {
            var clock = new FakeTimeProvider();
            clock.SetUtcNow(T0);

            var store = new FileCheckpointStore(root, clock);
            await store.SaveAsync(Checkpoint("short", new Counter(), expires: T0.AddMinutes(5)));
            await store.SaveAsync(Checkpoint("long", new Counter(), expires: T0.AddDays(7)));

            clock.Advance(TimeSpan.FromMinutes(6));
            await store.CleanupExpiredAsync();

            // Read back through a new store, so the answer comes from the folder rather than from
            // anything this one happened to remember.
            var after = new FileCheckpointStore(root, clock);

            (await after.LoadAsync<Counter>("short")).ShouldBeNull();
            (await after.LoadAsync<Counter>("long")).ShouldNotBeNull();
        });
    }

    [Test]
    public async Task TwoExecutionIdsDifferingOnlyInPunctuation_DoNotShareAFile()
    {
        await InAFolder(async root =>
        {
            var clock = new FakeTimeProvider();
            clock.SetUtcNow(T0);
            var store = new FileCheckpointStore(root, clock);

            await store.SaveAsync(Checkpoint("tenant/run", new Counter { Value = 1 }));
            await store.SaveAsync(Checkpoint("tenant-run", new Counter { Value = 2 }));

            (await store.LoadAsync<Counter>("tenant/run")).ShouldNotBeNull().State.Value.ShouldBe(1);
            (await store.LoadAsync<Counter>("tenant-run")).ShouldNotBeNull().State.Value.ShouldBe(2);
        });
    }

    [Test]
    public async Task TwoPlanIdsDifferingOnlyInPunctuation_DoNotShareAFile()
    {
        // The same defect, in the store that had it first: every character a file name may not carry
        // mapped to '-', so 'team/plan' and 'team-plan' resolved to one file and the second write
        // silently replaced the first. Traversal was prevented; collision was not.
        await InAFolder(async root =>
        {
            var store = new FilePlanTreeStore(root);

            await store.SaveAsync(PlanTree.Create("team/plan", Contract("Ship the one with a slash"), NoChildren));
            await store.SaveAsync(PlanTree.Create("team-plan", Contract("Ship the one with a dash"), NoChildren));

            (await store.LoadAsync("team/plan")).ShouldNotBeNull()
                .Root.Contract.Goal.ShouldBe("Ship the one with a slash");

            (await store.LoadAsync("team-plan")).ShouldNotBeNull()
                .Root.Contract.Goal.ShouldBe("Ship the one with a dash");
        });
    }

    [Test]
    public async Task APausedRun_ResumesFromAFileStore()
    {
        // The claim the tier makes, end to end: the run stops for a person, everything about where it
        // stood is on disk, and a workflow built fresh over the same folder — nothing shared with the
        // first but the path — picks the question up and answers it.
        await InAFolder(async root =>
        {
            var halted = await Importer(root).RunAsync(new Delivery());

            halted.Status.ShouldBe(ExecutionStatus.Interrupted);
            halted.State.Coordination.ShouldNotBeNull().NodeId.ShouldBe("plan-parse");
            halted.State.Coordination!.Decision.ShouldBeNull();

            var settled = await Importer(root).ResumeAsync(halted.Id, paused => paused with
            {
                Coordination = paused.Coordination! with
                {
                    Decision = PlanDecision.Replan(
                        Contract("Parse both dialects", "the parser round-trips"),
                        new PlanRationale { By = "the on-call engineer", Text = "two dialects." },
                        nodeId: "plan-parse")
                }
            });

            settled.Status.ShouldBe(ExecutionStatus.Completed);

            var coordination = settled.State.Coordination.ShouldNotBeNull();
            coordination.Result.HaltedAt.ShouldBeNull();
            coordination.Result.Tree.Lineage.Count.ShouldBe(2);
            coordination.Result.Tree.Lineage[^1].Rationale!.By.ShouldBe("the on-call engineer");
        });
    }

    // ── Fixtures ──

    /// <summary>The same workflow, built twice over one folder — the stand-in for a restart.</summary>
    private static Workflow<Delivery> Importer(string root)
    {
        var store = new FilePlanTreeStore(root);
        var executor = new PlanExecutor(store);

        return AgenticPattern.SupervisedPlan<Delivery>("importer")
            .WithPlan(PlanTree.Create(
                "plan",
                Contract("Ship the importer", "the importer round-trips every fixture"),
                [("plan-parse", Contract("Parse the input", "the parser round-trips"))]))
            .Supervised(new SupervisionOptions
            {
                Store = store,
                Runner = (context, ct) =>
                    context.Contract.Goal == "Parse the input"
                        ? Disputes(
                            executor, context, "the parser round-trips",
                            "the input has two dialects and the contract names one", ct)
                        : Task.FromResult(Meets(context))
            })
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithCoordinator((coordination, _) =>
                Task.FromResult(coordination.Decision ?? PlanDecision.Ask([])))
            .EscalateToAPerson()
            .Build()
            .UseCheckpointing(new FileCheckpointStore(root));
    }

    /// <summary>
    /// Disputes a node from outside it — a reviewer's call, never the node's own report (R26) — and
    /// returns the narration-only outcome now left for the runner to hand back.
    /// </summary>
    private static async Task<NodeOutcome> Disputes(
        PlanExecutor executor, PlanNodeContext context, string criterion, string reason, CancellationToken ct)
    {
        await executor.DisputeAsync(context.PlanId, context.Node.Id, criterion, reason, ct)
            .ConfigureAwait(false);
        return NodeOutcome.Nothing;
    }

    private static Checkpoint<TState> Checkpoint<TState>(
        string executionId, TState state, DateTimeOffset? expires = null) => new()
        {
            ExecutionId = executionId,
            WorkflowName = "importer",
            CurrentJob = "choose",
            State = state,
            Status = ExecutionStatus.Interrupted,
            History = [],
            CreatedAt = T0,
            ExpiresAt = expires ?? T0.AddDays(7)
        };

    private static async Task InAFolder(Func<string, Task> test)
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            await test(root).ConfigureAwait(false);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static (string Id, AgentContract Contract)[] NoChildren => [];

    private static AgentContract Contract(string goal, params string[] criteria) =>
        new() { Goal = goal, AcceptanceCriteria = criteria };

    private static NodeOutcome Meets(PlanNodeContext context) => new()
    {
        Verdicts =
        [
            .. context.Contract.AcceptanceCriteria.Select(criterion => new CriterionVerdict
            {
                Criterion = criterion, Passed = true, Oracle = "dotnet test", At = T0
            })
        ]
    };
}
