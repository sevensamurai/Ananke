using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// The common human-in-the-loop shape: something proposes, a person picks.
/// </summary>
/// <remarks>
/// <para>
/// Not a person authoring a replacement contract — the rarer, harder case — but the one a consumer
/// reaches for first: <em>option A · option B · declare it not feasible</em>. That needs a pause
/// <b>between proposing and choosing</b>, and the supervised-plan builder has only one, before the
/// coordinator runs, which is a step too early: nothing has been proposed yet.
/// </para>
/// <para>
/// So this is hand-wired from jobs that already ship — <c>Supervise</c>, <c>Job</c>, <c>Then</c>,
/// <c>Loop</c> and <c>AwaitInput</c>, the pause that says <em>a question is outstanding</em> rather
/// than <em>the run is parked</em>. Nothing here is new machinery; the question is whether the
/// machinery composes into the shape.
/// </para>
/// </remarks>
[TestFixture]
public class ChooserPlanTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 31, 9, 0, 0, TimeSpan.Zero);

    /// <summary>One thing a person is asked, and what they may answer.</summary>
    private sealed record Question(string Text, IReadOnlyList<string> Options)
    {
        public string? Answer { get; init; }
    }

    private sealed record DeliveryState
    {
        public PlanCoordination? Coordination { get; init; }

        /// <summary>What is outstanding. Written by `propose`, answered from outside, read by `choose`.</summary>
        public IReadOnlyList<Question> Asked { get; init; } = [];

        /// <summary>How many times the plan itself ran — the cost the inner question loop avoids.</summary>
        public int Passes { get; init; }
    }

    // ── The shape ──

    [Test]
    public async Task AStepThatCannotBeSatisfiedWithoutAsking_PausesWithItsQuestions()
    {
        var workflow = Delivery();

        var asked = await workflow.RunAsync(new DeliveryState());

        asked.Status.ShouldBe(ExecutionStatus.Interrupted);

        // One pause, two questions. Options are data in state, not topology: asking about four
        // options does not mean four jobs, and asking twice does not mean two `choose` jobs.
        asked.State.Asked.Count.ShouldBe(2);
        asked.State.Asked[0].Options.ShouldBe(["ftp", "streaming", "not-feasible"]);
        asked.State.Asked[1].Options.ShouldBe(["overwrite", "keep-both"]);

        // And it paused as a *question*, which a host can tell apart from an approval gate.
        workflow.Build().InputJobs.ShouldContain("choose");

        asked.State.Coordination!.Dispute.ShouldNotBeNull();
        asked.State.Passes.ShouldBe(1);
    }

    [Test]
    public async Task TwoAnswersInOnePause_ReachTheNodeAsOneReRuling()
    {
        // Both answers reach the node the only way anything does — as its contract — so one
        // re-ruling carries both. Answered one at a time they would mint two versions of the plan,
        // for a plan that was wrong exactly once.
        var workflow = Delivery();

        var asked = await workflow.RunAsync(new DeliveryState());
        var done = await workflow.ResumeAsync(asked.Id, Answered("ftp", "keep-both"));

        var coordination = done.State.Coordination.ShouldNotBeNull();
        var tree = coordination.Result.Tree;

        tree.Lineage.Count.ShouldBe(2);

        // And nothing is counted against the person for having been asked.
        coordination.Changes.ShouldBe(0);

        var contract = tree.Node("deliver").Contract;
        contract.AcceptanceCriteria.ShouldContain("the release pack is on the customer's ftp drop");
        contract.Constraints.ShouldContain("the previous release is kept, not overwritten");

        // The person's choice is recorded as their argument, beside the halt's own words.
        tree.Lineage[^1].Rationale!.By.ShouldBe("the release manager");
        tree.Lineage[^1].Reason.ShouldBe("delivering needs a decision that is not the model's to make");
    }

    [Test]
    public async Task DeclaringItNotFeasible_EndsTheRun()
    {
        // A real outcome, not an escape hatch: "we cannot deliver this way" is a legitimate answer
        // to a delivery step, and it is the answer that ends the run.
        var workflow = Delivery();

        var asked = await workflow.RunAsync(new DeliveryState());
        var stopped = await workflow.ResumeAsync(asked.Id, Answered("not-feasible", "keep-both"));

        var coordination = stopped.State.Coordination.ShouldNotBeNull();

        coordination.Decision.ShouldBeOfType<PlanDecision.AskPlan>();
        coordination.Changes.ShouldBe(0);
        coordination.Result.Tree.Lineage.Count.ShouldBe(1);
        coordination.Result.Tree.OutcomeOf("deliver").ShouldNotBe(ContractOutcome.Met);
    }

    [Test]
    public async Task AFollowUpQuestion_DoesNotRunThePlanAgain()
    {
        // The trap the obvious wiring falls into. Getting back to `choose` decides what is re-run:
        // looping to the plan would re-execute the step that hit the wall just to ask a follow-up.
        // The question loop is its own loop, inside the plan loop.
        var workflow = Delivery();

        var asked = await workflow.RunAsync(new DeliveryState());
        asked.State.Passes.ShouldBe(1);

        // "discard — none of these, ask me again" leaves the questions unanswered and comes back.
        var again = await workflow.ResumeAsync(asked.Id, Answered("discard", "discard"));

        again.Status.ShouldBe(ExecutionStatus.Interrupted);
        again.State.Asked.Count.ShouldBe(2);

        // The plan did not run a second time, and no version was minted: a rejected proposal is a
        // decision about the *proposal*, not about the plan.
        again.State.Passes.ShouldBe(1);
        again.State.Coordination!.Changes.ShouldBe(0);
        again.State.Coordination.Result.Tree.Lineage.Count.ShouldBe(1);
    }

    // ── What the record says afterwards, when a node had to ask ──

    [Test]
    public async Task AfterTheAnswer_TheNodeThatAskedIsNotLeftContradicted()
    {
        // A node has no way to *ask* — it can only satisfy, dispute or throw — so escalating means
        // disputing a contract it does not actually believe is wrong. On the answering path that
        // self-heals: answering is a contract change, and a re-ruling clears the violation.
        var workflow = Delivery();

        var asked = await workflow.RunAsync(new DeliveryState());
        var done = await workflow.ResumeAsync(asked.Id, Answered("ftp", "keep-both"));

        var tree = done.State.Coordination!.Result.Tree;

        tree.Node("deliver").Violation.ShouldBeNull();
        tree.OutcomeOf("deliver").ShouldBe(ContractOutcome.Met);
    }

    [Test]
    public async Task WhenTheAnswerIsNo_TheRecordPermanentlySaysTheNodeDisputedItsContract()
    {
        // The cost of asking through the dispute channel, and the one path that does not self-heal.
        // What happened: `deliver` asked which transport to use, and a person answered "none of
        // them". What the record says: `deliver` reported that its contract cannot be met.
        //
        // Those are different claims. The first is a question answered; the second is a standing
        // accusation against the plan, kept forever, about a node that was working correctly.
        var workflow = Delivery();

        var asked = await workflow.RunAsync(new DeliveryState());
        var stopped = await workflow.ResumeAsync(asked.Id, Answered("not-feasible", "keep-both"));

        var deliver = stopped.State.Coordination!.Result.Tree.Node("deliver");

        deliver.Violation.ShouldNotBeNull();
        deliver.Violation.Reason.ShouldBe("delivering needs a decision that is not the model's to make");

        // Pinned as it stands, not as it should be: closing this needs a way for a node to ask
        // without claiming its contract is wrong, which is a decision above this test.
    }

    // ── Fixtures ──

    /// <summary>
    /// `plan → propose → choose`, with the pause between the last two.
    /// </summary>
    private static Workflow<DeliveryState> Delivery()
    {
        var store = new InMemoryPlanTreeStore();
        var executor = new PlanExecutor(store);

        var supervision = new SupervisionOptions
        {
            Store = store,
            // The step whose correct execution includes asking. It cannot choose a transport on the
            // customer's behalf, so it stops rather than choosing. The dispute reaches the tree
            // through DisputeAsync now — an external call, never the node's own report (R26).
            Runner = (context, ct) => context.Node.Id switch
            {
                "deliver" when context.Contract.AcceptanceCriteria.Contains("the release pack reaches the customer") =>
                    Disputes(executor, context, ct),
                _ => Task.FromResult(Met(context))
            }
        };

        return new Workflow<DeliveryState>("release")
            .Supervise(
                "plan",
                PlanTree.Create(
                    "release",
                    Contract("Ship release 1.4 to the customer", "the customer has release 1.4"),
                    [
                        ("pack", Contract("Assemble the pack", "changes.csv and notes.md are current")),
                        ("deliver", Contract("Deliver it", "the release pack reaches the customer"))
                    ]),
                supervision,
                (state, result) => state with
                {
                    Passes = state.Passes + 1,
                    Coordination = state.Coordination is null
                        ? new PlanCoordination { Result = result }
                        : state.Coordination with { Result = result, Decision = null }
                })
            .Job("propose", (state, _) => Task.FromResult(state.Coordination?.NodeId is null
                ? state
                : state with
                {
                    Asked =
                    [
                        new Question(
                            "How should the release pack be delivered?",
                            ["ftp", "streaming", "not-feasible"]),
                        new Question(
                            "The previous release is still on the target. What happens to it?",
                            ["overwrite", "keep-both"])
                    ]
                }))
            // Wrapped, so the change-of-plan budget is spent and the decision reported by the
            // framework rather than by the consumer — the same contract a builder-wired coordinator
            // job gets, which a hand-wired workflow has to ask for explicitly.
            .Job("choose", new AccountedSupervisorJob<DeliveryState>(
                new Choose(supervision),
                state => state.Coordination,
                (state, coordination) => state with { Coordination = coordination }))
            .Then("plan", Workflow.Decide<DeliveryState>(state =>
                state.Coordination?.NodeId is null ? Workflow.End : "propose"))
            .Then("propose", "choose")
            // Three destinations, so a router rather than a loop. The inner question loop is the
            // middle one: unanswered questions come back to `propose`, never to `plan`.
            //
            // Nothing bounds it but the pause itself, and that is the point — `AwaitInput` stops the
            // run before `choose` every time it is reached, so a question loop cannot run away: it
            // can only go round as often as a person answers it.
            .Then("choose", Workflow.Decide<DeliveryState>(state =>
                state.Coordination?.Decision is PlanDecision.AskPlan ? Workflow.End
                : state.Asked.Any(q => q.Answer is null or "discard") ? "propose"
                : "plan"))
            .AwaitInput("choose")
            .UseCheckpointing(new InMemoryCheckpointStore());
    }

    /// <summary>Reads the answers and turns them into the one decision they add up to.</summary>
    private sealed class Choose(SupervisionOptions supervision) : IJob<DeliveryState>
    {
        public string Name => "choose";

        public async Task<DeliveryState> ExecuteAsync(
            DeliveryState state, CancellationToken ct = default)
        {
            if (state.Coordination is not { } coordination || coordination.NodeId is not { } haltedAt)
                return state;

            var answers = state.Asked.Select(q => q.Answer).ToList();

            // Nothing chosen, or "none of these": come back with the questions again. No decision is
            // written, so nothing is reported and no budget is spent.
            if (answers.Any(a => a is null or "discard"))
                return state;

            if (answers[0] == "not-feasible")
                return state with { Coordination = coordination with { Decision = PlanDecision.Ask([]) } };

            var replacement = new AgentContract
            {
                Goal = "Deliver it",
                AcceptanceCriteria = [$"the release pack is on the customer's {answers[0]} drop"],
                Constraints = answers[1] == "keep-both"
                    ? ["the previous release is kept, not overwritten"]
                    : ["the previous release is overwritten"]
            };

            await supervision.ReruleAsync(
                coordination.Result.Tree.PlanId,
                haltedAt,
                replacement,
                // Taken from the halt, never composed here: the person did not witness it.
                coordination.HaltReason ?? "it stopped",
                rationale: new PlanRationale
                {
                    By = "the release manager",
                    Text = $"Deliver over {answers[0]}; {answers[1]} for the existing release."
                },
                ct: ct);

            return state with
            {
                Asked = [],
                Coordination = coordination with { Decision = PlanDecision.Replan(replacement) }
            };
        }
    }

    private static Func<DeliveryState, DeliveryState> Answered(params string[] answers) =>
        state => state with
        {
            Asked = [.. state.Asked.Select((q, i) => q with { Answer = answers[i] })]
        };

    private static AgentContract Contract(string goal, params string[] criteria) =>
        new() { Goal = goal, AcceptanceCriteria = criteria };

    private static NodeOutcome Met(PlanNodeContext context) => new()
    {
        Verdicts =
        [
            .. context.Contract.AcceptanceCriteria.Select(criterion => new CriterionVerdict
            {
                Criterion = criterion, Passed = true, Oracle = "dotnet test", At = T0
            })
        ]
    };

    /// <summary>
    /// Disputes a node from outside it — a reviewer's call, never the node's own report (R26) — and
    /// returns the narration-only outcome now left for the runner to hand back.
    /// </summary>
    private static async Task<NodeOutcome> Disputes(
        PlanExecutor executor, PlanNodeContext context, CancellationToken ct)
    {
        await executor.DisputeAsync(
            context.PlanId, context.Node.Id, "the release pack reaches the customer",
            "delivering needs a decision that is not the model's to make", ct).ConfigureAwait(false);
        return NodeOutcome.Nothing;
    }
}
