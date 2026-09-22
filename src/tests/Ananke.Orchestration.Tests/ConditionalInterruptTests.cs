using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Patterns;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// A pause with a reason to happen: <c>InterruptWhen</c>, and what a round the run parked on costs.
/// </summary>
/// <remarks>
/// <para>
/// An unconditional interrupt is an approval gate — it stops every time, which is right when somebody
/// must see every one of these. An escalation is the other shape: the run steers itself until it
/// cannot, and only then asks. Without a condition on the pause the second is written as the first,
/// so a plan that wanted a person <em>at the wall</em> asks about every decision it was able to make.
/// </para>
/// <para>
/// The other half of the spike asked whether a round the run parked on should advance the loop's
/// counter, and the answer is <b>yes</b>: for a conversation a turn <em>is</em> a parked round, and
/// <c>InterviewBuilder</c> bounds itself with exactly that count. What was wrong was never the
/// counter — it was a plan builder computing the cap from <c>MaxChangesOfPlan</c>, a statement about
/// re-planning rather than about rounds.
/// </para>
/// </remarks>
[TestFixture]
public class ConditionalInterruptTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 2, 9, 0, 0, TimeSpan.Zero);

    private sealed record Counting
    {
        public int Rounds { get; init; }
        public bool Escalate { get; init; }
        public PlanCoordination? Coordination { get; init; }
    }

    [Test]
    public async Task APauseWithACondition_HappensOnTheArrivalsThatMeetItAndNoOthers()
    {
        var workflow = new Workflow<Counting>("count")
            .Job("tick", (state, _) => Task.FromResult(state with { Rounds = state.Rounds + 1 }))
            .Job("check", (state, _) => Task.FromResult(state))
            .Then("tick", "check")
            .Loop("check", loopTarget: "tick", exitTarget: Workflow.End,
                until: s => s.Rounds >= 4, maxIterations: 10)
            // Ask only about the third round. Every other arrival is the run steering itself.
            .InterruptWhen("check", s => s.Rounds == 3)
            .UseCheckpointing(new InMemoryCheckpointStore());

        var paused = await workflow.RunAsync(new Counting());

        paused.Status.ShouldBe(ExecutionStatus.Interrupted);
        paused.State.Rounds.ShouldBe(3);          // ran rounds 1 and 2 without asking

        var done = await workflow.ResumeAsync(paused.Id);

        done.Status.ShouldBe(ExecutionStatus.Completed);
        done.State.Rounds.ShouldBe(4);            // and round 4 needed nobody
    }

    [Test]
    public async Task ALoopWhoseEveryRoundIsParked_IsStillBoundedByItsOwnCap()
    {
        // Found by the spike, and it is why a loop counter must keep counting rounds somebody
        // answered. `InterviewBuilder` passes `maxIterations: _maxTurns` — for a conversation, a
        // turn *is* a parked round, so a cap that ignored them would never fire and
        // `Interview_MaxTurnsCap_ExitsWhenUntilNeverTrue` would hang instead of ending.
        var workflow = new Workflow<Counting>("count")
            .Job("tick", (state, _) => Task.FromResult(state with { Rounds = state.Rounds + 1 }))
            .Job("check", (state, _) => Task.FromResult(state))
            .Then("tick", "check")
            .Loop("check", loopTarget: "tick", exitTarget: Workflow.End,
                until: s => s.Rounds >= 4, maxIterations: 2)
            .InterruptWhen("check", _ => true)     // every round asks
            .UseCheckpointing(new InMemoryCheckpointStore());

        var run = await workflow.RunAsync(new Counting());

        run.Status.ShouldBe(ExecutionStatus.Interrupted);
        run = await workflow.ResumeAsync(run.Id);

        run.Status.ShouldBe(ExecutionStatus.Interrupted);
        run = await workflow.ResumeAsync(run.Id);

        // Two rounds, and the cap ended it with the condition still unmet.
        run.Status.ShouldBe(ExecutionStatus.Completed);
        run.State.Rounds.ShouldBe(2);
    }

    [Test]
    public async Task APlanEscalatesAtTheWallAndSteersItselfEverywhereElse()
    {
        // The case a run-level flag cannot express: one run, two halts, and only the second one is a
        // person's to answer. The dispute is re-ruled by the coordinator with nobody watching; the
        // step that broke is where the run stops and asks.
        var store = new InMemoryPlanTreeStore();
        var executor = new PlanExecutor(store);

        var workflow = AgenticPattern.SupervisedPlan<Counting>("delivery")
            .WithPlan(PlanTree.Create(
                "plan",
                Contract("Ship it", "the build is green"),
                [
                    ("plan-parse", Contract("Parse", "the parser round-trips")),
                    ("plan-render", Contract("Render", "output matches the golden file"))
                ]))
            .Supervised(new SupervisionOptions { Store = store, Runner = Stubborn(executor) })
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithCoordinator((coordination, _) => Task.FromResult(
                coordination.Decision                                  // what a person left, if any
                ?? (coordination.Dispute is not null                   // otherwise steer, if it can
                    ? PlanDecision.Replan(
                        Contract("Parse, the documented dialect", "the parser round-trips"),
                        nodeId: "plan-parse")
                    : PlanDecision.Ask([]))))
            .Build()
            // The whole point: pause at the wall, not at every decision.
            .InterruptWhen(
                SupervisedPlanBuilder<Counting>.CoordinatorJob,
                s => s.Coordination?.Node?.Failure is not null)
            .UseCheckpointing(new InMemoryCheckpointStore());

        var run = await workflow.RunAsync(new Counting());

        // It re-ruled the dispute on its own and only stopped at the step that broke.
        run.Status.ShouldBe(ExecutionStatus.Interrupted);

        var halted = run.State.Coordination.ShouldNotBeNull();

        halted.Node!.Failure.ShouldNotBeNull();
        halted.NodeId.ShouldBe("plan-render");
        halted.Result.Tree.Lineage.Count.ShouldBe(2);   // the autonomous re-ruling is already minted

        var answered = await workflow.ResumeAsync(run.Id, state => state with
        {
            Coordination = state.Coordination! with
            {
                Decision = PlanDecision.Replan(
                    Contract("Render, without the golden file", "output is well-formed"),
                    new PlanRationale { By = "the on-call engineer", Text = "the golden file is stale." },
                    nodeId: "plan-render")
            }
        });

        answered.Status.ShouldBe(ExecutionStatus.Completed);

        var settled = answered.State.Coordination.ShouldNotBeNull();

        settled.Result.HaltedAt.ShouldBeNull();
        settled.Result.Tree.Lineage.Count.ShouldBe(3);
    }

    [Test]
    public async Task AConditionalInputTurn_IsStillRecognisableAsOne()
    {
        // A pause a host cannot classify is a pause it cannot render. `AwaitInput` earns its keep by
        // saying *what kind* of stop this is, and a conditional one has to keep saying it.
        var workflow = new Workflow<Counting>("count")
            .Job("tick", (state, _) => Task.FromResult(state with { Rounds = state.Rounds + 1 }))
            .Job("check", (state, _) => Task.FromResult(state))
            .Then("tick", "check")
            .Loop("check", loopTarget: "tick", exitTarget: Workflow.End,
                until: s => s.Rounds >= 3, maxIterations: 10)
            .AwaitInputWhen("check", s => s.Rounds == 2)
            .UseCheckpointing(new InMemoryCheckpointStore());

        workflow.Build().InputJobs.ShouldContain("check");

        var run = await workflow.RunAsync(new Counting());

        run.Status.ShouldBe(ExecutionStatus.Interrupted);
        run.State.Rounds.ShouldBe(2);

        var done = await workflow.ResumeAsync(run.Id, state => state with { Escalate = true });

        done.Status.ShouldBe(ExecutionStatus.Completed);
        done.State.Escalate.ShouldBeTrue();          // the reply arrived the way every reply does
    }

    [Test]
    public void AConditionalPauseOnTheEntryJob_IsRefusedLikeAnyOther()
    {
        // The condition does not buy an exception to the rule: no work has happened yet, so there is
        // nothing to approve and nothing for a predicate to read.
        var workflow = new Workflow<Counting>("count")
            .Job("tick", (state, _) => Task.FromResult(state))
            .Then("tick", Workflow.End)
            .InterruptWhen("tick", _ => true)
            .UseCheckpointing(new InMemoryCheckpointStore());

        var build = () => workflow.Build();

        build.ShouldThrow<InvalidOperationException>().Message.ShouldContain("entry job");
    }

    [Test]
    public async Task APauseAfterAJob_TakesACondition_AndReadsWhatTheJobProduced()
    {
        // The after-side is where a condition about the *result* belongs: before the job there is
        // nothing yet to test. It was reachable only by writing two calls in one particular order
        // until `InterruptAfterWhen` said it outright.
        static Workflow<Counting> Build() => new Workflow<Counting>("count")
            .Job("tick", (state, _) => Task.FromResult(state with { Rounds = state.Rounds + 1 }))
            .Job("done", (state, _) => Task.FromResult(state with { Escalate = true }))
            .Then("tick", "done")
            .Then("done", Workflow.End)
            .InterruptAfterWhen("tick", s => s.Rounds == 1)
            .UseCheckpointing(new InMemoryCheckpointStore());

        var workflow = Build();
        var paused = await workflow.RunAsync(new Counting());

        paused.Status.ShouldBe(ExecutionStatus.Interrupted);
        paused.State.Rounds.ShouldBe(1);            // the job ran — the pause is after it
        paused.State.Escalate.ShouldBeFalse();      // and the next job has not

        var resumed = await workflow.ResumeAsync(paused.Id);

        resumed.Status.ShouldBe(ExecutionStatus.Completed);
        resumed.State.Escalate.ShouldBeTrue();

        // The arrival the condition does not name runs straight through, exactly as before it.
        var unstopped = await Build().RunAsync(new Counting { Rounds = 5 });

        unstopped.Status.ShouldBe(ExecutionStatus.Completed);
        unstopped.State.Escalate.ShouldBeTrue();
    }

    [Test]
    public void AConditionOnAnAfterPause_DoesNotMoveItToTheFrontOfTheJob()
    {
        // A predicate says *whether this arrival pauses*, never *where the pause is*. Forcing Before
        // here made the two orderings of the same two calls mean different things, and silently
        // demoted the after-pause in one of them.
        static Workflow<Counting> Wired(Action<Workflow<Counting>> wire)
        {
            var workflow = new Workflow<Counting>("count")
                .Job("tick", (state, _) => Task.FromResult(state))
                .Job("done", (state, _) => Task.FromResult(state))
                .Then("tick", "done")
                .Then("done", Workflow.End)
                .UseCheckpointing(new InMemoryCheckpointStore());

            wire(workflow);
            return workflow;
        }

        foreach (var wired in new[]
        {
            Wired(w => { w.InterruptAfter("tick"); w.InterruptWhen("tick", _ => true); }),
            Wired(w => { w.InterruptWhen("tick", _ => true); w.InterruptAfter("tick"); })
        })
        {
            var descriptor = wired.Build().Jobs["tick"];

            descriptor.Interrupt.ShouldBe(InterruptMode.After);
            descriptor.InterruptWhen.ShouldNotBeNull();
        }
    }

    // ── Fixtures ──

    /// <summary>Disputes the parse step until it is re-ruled; never renders against a golden file.</summary>
    /// <remarks>
    /// The dispute reaches the tree through <see cref="PlanExecutor.DisputeAsync"/> now — an external
    /// call, never the node's own report (R26) — so the runner needs the executor its run shares a
    /// store with.
    /// </remarks>
    private static PlanNodeRunner Stubborn(PlanExecutor executor) => (context, ct) =>
        (context.Node.Id, context.Contract.Goal) switch
        {
            ("plan-parse", "Parse") => Disputes(executor, context, ct),

            // The second wall, and a different kind: this one the coordinator has no rule for, so
            // it is the one a person is asked about.
            ("plan-render", "Render") => throw new InvalidOperationException("the golden file is missing"),

            _ => Task.FromResult(Answer(context, passed: true))
        };

    private static async Task<NodeOutcome> Disputes(
        PlanExecutor executor, PlanNodeContext context, CancellationToken ct)
    {
        await executor.DisputeAsync(
            context.PlanId, context.Node.Id, "the parser round-trips",
            "the input has two dialects and the contract names one", ct).ConfigureAwait(false);
        return NodeOutcome.Nothing;
    }

    private static AgentContract Contract(string goal, params string[] criteria) =>
        new() { Goal = goal, AcceptanceCriteria = criteria };

    private static NodeOutcome Answer(PlanNodeContext context, bool passed) => new()
    {
        Verdicts =
        [
            .. context.Contract.AcceptanceCriteria.Select(criterion => new CriterionVerdict
            {
                Criterion = criterion, Passed = passed, Oracle = "dotnet test", At = T0
            })
        ]
    };
}
