using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Patterns;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// A person in the coordinator's seat: the plan hits a wall, stops, and waits to be told what to do.
/// </summary>
/// <remarks>
/// <para>
/// The case the tier's shape is meant to buy, and the one an in-job coordination loop could not have:
/// a change of plan <b>nobody in the process is qualified to make</b>. A node reports its contract
/// cannot be met, the replacement is not a rewording but a different decomposition, and the only
/// sensible author is a person who is not there when the run reaches the halt.
/// </para>
/// <para>
/// So the run stops at <c>InterruptBefore(coordinate)</c>, the answer arrives later through
/// <c>ResumeAsync(id, transform)</c> — the same door every other kind of human input uses — and the
/// coordinator is a two-line delegate that reads it. <b>No new seam:</b> the decision travels in
/// workflow state because that is where everything else travels.
/// </para>
/// </remarks>
[TestFixture]
public class HumanInTheLoopPlanTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 31, 9, 0, 0, TimeSpan.Zero);

    private sealed record DeliveryState
    {
        public PlanCoordination? Coordination { get; init; }
    }

    /// <summary>
    /// Whatever a person left in state, or the halt standing if nobody answered.
    /// </summary>
    /// <remarks>
    /// The whole of a human-in-the-loop coordinator. Stopping on an empty inbox is the honest
    /// default: a resume carrying nothing is not an instruction to keep going.
    /// </remarks>
    private static readonly PlanSupervisor Inbox =
        (coordination, _) => Task.FromResult(coordination.Decision ?? PlanDecision.Ask([]));

    [Test]
    public async Task APlanThatHitTheWall_WaitsForAPerson_AndTheirDecisionArrivesThroughState()
    {
        var events = new List<WorkflowEvent>();
        using var sink = WorkflowEventReporting.BeginScope(new Collecting(events));

        var workflow = Importer().UseCheckpointing(new InMemoryCheckpointStore());

        // ── The wall ────────────────────────────────────────────────────────────────────────────
        var halted = await workflow.RunAsync(new DeliveryState());

        halted.Status.ShouldBe(ExecutionStatus.Interrupted);

        var first = halted.State.Coordination.ShouldNotBeNull();
        first.NodeId.ShouldBe("plan-parse");
        first.Dispute.ShouldNotBeNull();
        first.HaltReason.ShouldBe("the input has two dialects and one parser cannot round-trip both");

        // Nothing has decided. That is what makes the pause worth having: a person is looking at an
        // open question, not reviewing an answer already given on their behalf.
        first.Decision.ShouldBeNull();

        // Nothing is being counted at them, and no ceiling was invented to count against: this plan
        // asked for none, so what bounds the run is the person in front of it.
        first.Changes.ShouldBe(0);
        first.MaxChanges.ShouldBeNull();

        // ── The answer that needed a person ─────────────────────────────────────────────────────
        // Not a rewording. The step was impossible as decomposed, so the parent is re-ruled and the
        // work is split in two — the shape of change a coordinator reading only the halted node
        // could not express, and the reason a decision names the node it re-rules.
        var decided = await workflow.ResumeAsync(halted.Id, Answer(PlanDecision.Replan(
            Contract("Ship the importer", "the importer round-trips every fixture"),
            new PlanRationale
            {
                By = "the on-call engineer",
                Text = "The format has two dialects. One parser cannot round-trip both; split it."
            },
            children:
            [
                new AuthoredStep
                {
                    Id = "plan-parse-strict",
                    Contract = Contract("Parse the documented dialect", "the strict parser round-trips")
                },
                new AuthoredStep
                {
                    Id = "plan-parse-legacy",
                    Contract = Contract("Parse the legacy dialect", "the legacy parser round-trips")
                }
            ],
            nodeId: "plan")));

        var third = decided.State.Coordination.ShouldNotBeNull();
        var tree = third.Result.Tree;

        tree.Lineage.Count.ShouldBe(2);
        tree.Node("plan").ChildIds.ShouldBe(["plan-parse-strict", "plan-parse-legacy"]);
        third.Result.HaltedAt.ShouldBeNull();                       // the new plan settled
        tree.OutcomeOf("plan").ShouldBe(ContractOutcome.Met);

        // The version records what was *observed* — the halt's own words — and what the person
        // *concluded* beside it, attributed to them. A person is no better a source for why a plan
        // stopped being right than the node that stopped.
        var minted = tree.Lineage[^1];
        minted.ReRuledNodeId.ShouldBe("plan");
        minted.Reason.ShouldBe("the input has two dialects and one parser cannot round-trip both");
        minted.Rationale!.By.ShouldBe("the on-call engineer");

        // ── And a settled plan is never put to them ─────────────────────────────────────────────
        // Not "paused and then let through": the escalation is a condition over the halt, and a pass
        // that settled has none — so the answering resume is the last thing that happens.
        decided.Status.ShouldBe(ExecutionStatus.Completed);

        // Both human decisions are in the record like any other — reporting is not accounting, and
        // a person's judgement belongs in the record more than a model's does. What the events say
        // is *who* decided, so a reader never has to infer whether the counts apply.
        var taken = events.OfType<PlanDecisionTaken>().ToList();

        taken.ShouldHaveSingleItem().Decision.ShouldBeOfType<PlanDecision.ReplanPlan>();
        taken.ShouldAllBe(t => t.Escalated);
        taken.ShouldAllBe(t => t.Changes == 0 && t.MaxChanges == null);
    }

    [Test]
    public async Task APersonWhoResumesWithoutAnswering_LeavesThePlanExactlyAsTheyFoundIt()
    {
        // A resume that carries nothing is not consent to continue, which is what makes walking away
        // from the pause safe.
        var workflow = Importer().UseCheckpointing(new InMemoryCheckpointStore());

        var halted = await workflow.RunAsync(new DeliveryState());
        var resumed = await workflow.ResumeAsync(halted.Id);

        var coordination = resumed.State.Coordination.ShouldNotBeNull();
        coordination.Result.HaltedAt.ShouldBe("plan-parse");
        coordination.Result.Tree.Lineage.Count.ShouldBe(1);
        coordination.Decision.ShouldBeOfType<PlanDecision.AskPlan>();

        // Stopping buys no further pass, so it costs nothing.
        coordination.Changes.ShouldBe(0);
    }

    [Test]
    public async Task APersonAnsweringMoreOftenThanTheOldBudget_StillSettlesThePlan()
    {
        // The behaviour the accounting ruling exists for. Four questions is not four failures of
        // planning: nothing about the plan was wrong four times, somebody was simply consulted four
        // times. Charged to the change-of-plan cap — three, by default — the fourth answer would
        // never be asked for and the run would end unsettled, having done nothing wrong.
        var workflow = Stubborn().UseCheckpointing(new InMemoryCheckpointStore());

        var run = await workflow.RunAsync(new DeliveryState());

        for (var take = 2; take <= 5; take++)
        {
            run.Status.ShouldBe(ExecutionStatus.Interrupted);
            run.State.Coordination!.Changes.ShouldBe(0);   // none of it charged to the budget

            run = await workflow.ResumeAsync(run.Id, Answer(PlanDecision.Replan(
                Contract($"Parse the input, take {take}", "the parser round-trips"),
                new PlanRationale { By = "the on-call engineer", Text = $"try dialect {take}." },
                nodeId: "plan-parse")));
        }

        // The fifth pass is the one that settles, and the loop was still there to run it: nothing
        // counts an answer, and nothing caps how often somebody may be asked.
        run.Status.ShouldBe(ExecutionStatus.Completed);

        var coordination = run.State.Coordination.ShouldNotBeNull();

        coordination.Result.HaltedAt.ShouldBeNull();
        coordination.Result.Tree.Lineage.Count.ShouldBe(5);   // the original plus four answers
        coordination.Changes.ShouldBe(0);                     // none of which was counted
    }

    [Test]
    public async Task APlanEscalatingOnlyAtTheWall_ChargesWhatItDecidedItselfAndNotWhatItWasTold()
    {
        // The case a run-level declaration could not express, through the builder: one run, two
        // halts, and only the second is a person's. The dispute is re-ruled with nobody watching and
        // spends the budget; the step that broke stops the run, and the answer that comes back spends
        // nothing — because the loop was not the thing that produced it.
        var events = new List<WorkflowEvent>();
        using var sink = WorkflowEventReporting.BeginScope(new Collecting(events));

        var workflow = TwoWalls().UseCheckpointing(new InMemoryCheckpointStore());

        // The pause says what kind of stop it is: a turn that wants an answer, not a gate that wants
        // approval. A host reading the definition can tell without knowing anything about plans.
        workflow.Build().InputJobs.ShouldContain(SupervisedPlanBuilder<DeliveryState>.CoordinatorJob);

        var run = await workflow.RunAsync(new DeliveryState());

        run.Status.ShouldBe(ExecutionStatus.Interrupted);

        var halted = run.State.Coordination.ShouldNotBeNull();

        halted.Node!.Failure.ShouldNotBeNull();
        halted.NodeId.ShouldBe("plan-render");
        halted.Result.Tree.Lineage.Count.ShouldBe(2);   // the dispute was re-ruled without asking
        halted.Changes.ShouldBe(1);                     // and that one was charged

        var done = await workflow.ResumeAsync(run.Id, Answer(PlanDecision.Replan(
            Contract("Render, without the golden file", "output is well-formed"),
            new PlanRationale { By = "the on-call engineer", Text = "the golden file is stale." },
            nodeId: "plan-render")));

        done.Status.ShouldBe(ExecutionStatus.Completed);

        var settled = done.State.Coordination.ShouldNotBeNull();

        settled.Result.HaltedAt.ShouldBeNull();
        settled.Result.Tree.Lineage.Count.ShouldBe(3);
        settled.Changes.ShouldBe(1);                    // still one: the answer was not charged

        // And the record says which was which, from how each decision was reached.
        var taken = events.OfType<PlanDecisionTaken>().ToList();

        taken.Count.ShouldBe(2);
        taken[0].Escalated.ShouldBeFalse();
        taken[1].Escalated.ShouldBeTrue();
    }

    [Test]
    public async Task AnAnswerFromOutsideTheRun_IsTheDecision_AndTheCoordinatorIsNotAsked()
    {
        // The point of escalating at all. A coordinator that would have decided otherwise — this one
        // gives up — must not get to overwrite what somebody was stopped and asked for, and the
        // record must not then report its answer as theirs. Every shipped coordinator decides from
        // the halt alone, so a framework that asked anyway would discard the person's answer in the
        // ordinary case rather than the exotic one.
        var events = new List<WorkflowEvent>();
        using var sink = WorkflowEventReporting.BeginScope(new Collecting(events));

        var asked = 0;
        var store = new InMemoryPlanTreeStore();
        var executor = new PlanExecutor(store);

        var workflow = AgenticPattern.SupervisedPlan<DeliveryState>("importer")
            .WithPlan(PlanTree.Create(
                "plan",
                Contract("Ship the importer", "the importer round-trips every fixture"),
                [
                    ("plan-parse", Contract("Parse the input", "the parser round-trips")),
                    ("plan-render", Contract("Render the report", "output matches the golden file"))
                ]))
            .Supervised(new SupervisionOptions
            {
                Store = store,
                // Only the contract a person is going to write can be satisfied.
                Runner = (context, ct) =>
                    context.Node.Id == "plan-parse" && context.Contract.Goal != "Parse both dialects"
                        ? Disputes(executor, context, ImpossibleCriterion, ImpossibleReason, ct)
                        : Task.FromResult(Answer(context, passed: true))
            })
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithCoordinator((_, _) =>
            {
                asked++;
                return Task.FromResult(PlanDecision.Ask([]));
            })
            .EscalateToAPerson()
            .Build()
            .UseCheckpointing(new InMemoryCheckpointStore());

        var halted = await workflow.RunAsync(new DeliveryState());

        halted.Status.ShouldBe(ExecutionStatus.Interrupted);
        asked.ShouldBe(0);   // nothing decided on the way to the pause

        var done = await workflow.ResumeAsync(halted.Id, Answer(PlanDecision.Replan(
            Contract("Parse both dialects", "the parser round-trips"),
            new PlanRationale { By = "the on-call engineer", Text = "the format has two dialects." },
            nodeId: "plan-parse")));

        // Had the coordinator been asked, its Stop would have settled the coordination and ended the
        // run with the plan unsettled and nothing minted.
        asked.ShouldBe(0);
        done.Status.ShouldBe(ExecutionStatus.Completed);

        var settled = done.State.Coordination.ShouldNotBeNull();

        settled.Result.HaltedAt.ShouldBeNull();
        settled.Result.Tree.Lineage.Count.ShouldBe(2);
        settled.Changes.ShouldBe(0);                      // an answer is not a change of plan spent

        // Cleared by the settling pass, like any other decision: it answered a halt that no longer
        // stands. What it did survives in the tree and in the record, which is where it belongs.
        settled.Decision.ShouldBeNull();

        // The version records the person's contract, not a coordinator's — and the decision the
        // record attributes to the pause is the one that was actually acted on.
        settled.Result.Tree.Node("plan-parse").Contract.Goal.ShouldBe("Parse both dialects");

        var taken = events.OfType<PlanDecisionTaken>().ShouldHaveSingleItem();

        taken.Escalated.ShouldBeTrue();
        taken.Decision.ShouldBeOfType<PlanDecision.ReplanPlan>();
    }

    [Test]
    public async Task AnEscalatingPlan_WiresThePauseToTheCoordinatorItWasGiven_WhateverItIsCalled()
    {
        // A supplied coordinator job keeps its own name everywhere in the generated topology, so the
        // escalation has to be wired to that name too. Wiring it to the pattern's own constant left
        // any other name unreachable — and the failure arrived on the first run, not at Build(),
        // naming a job the consumer never wrote.
        var store = new InMemoryPlanTreeStore();
        var executor = new PlanExecutor(store);

        var supervision = new SupervisionOptions
        {
            Store = store,
            Runner = (context, ct) =>
                context.Node.Id == "plan-parse" && context.Contract.Goal != "Parse both dialects"
                    ? Disputes(executor, context, ImpossibleCriterion, ImpossibleReason, ct)
                    : Task.FromResult(Answer(context, passed: true))
        };

        var workflow = AgenticPattern.SupervisedPlan<DeliveryState>("importer")
            .WithPlan(PlanTree.Create(
                "plan",
                Contract("Ship the importer", "the importer round-trips every fixture"),
                [("plan-parse", Contract("Parse the input", "the parser round-trips"))]))
            .Supervised(supervision)
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithCoordinator(new Triage(supervision))
            .EscalateToAPerson()
            .Build()
            .UseCheckpointing(new InMemoryCheckpointStore());

        var definition = workflow.Build();

        definition.Jobs.ShouldContainKey(Triage.JobName);
        definition.Jobs.ShouldNotContainKey(SupervisedPlanBuilder<DeliveryState>.CoordinatorJob);
        definition.InputJobs.ShouldContain(Triage.JobName);

        var halted = await workflow.RunAsync(new DeliveryState());

        halted.Status.ShouldBe(ExecutionStatus.Interrupted);
        halted.State.Coordination!.NodeId.ShouldBe("plan-parse");

        var done = await workflow.ResumeAsync(halted.Id, Answer(PlanDecision.Replan(
            Contract("Parse both dialects", "the parser round-trips"),
            nodeId: "plan-parse")));

        done.Status.ShouldBe(ExecutionStatus.Completed);
        done.State.Coordination!.Result.HaltedAt.ShouldBeNull();
    }

    // ── Fixtures ──

    /// <summary>A coordinator job under a name of the consumer's choosing, applying what it is told.</summary>
    /// <remarks>
    /// The job form decides <em>and applies</em> — which is why the wrapper cannot short-circuit it
    /// the way the delegate form is short-circuited: nothing outside the job can mint the version it
    /// has not decided on yet. Here what it applies is the re-ruling a person left in state, which is
    /// the whole of what an inbox does.
    /// </remarks>
    private sealed class Triage(SupervisionOptions supervision) : IJob<DeliveryState>
    {
        public const string JobName = "triage-inbox";

        public string Name => JobName;

        public async Task<DeliveryState> ExecuteAsync(DeliveryState state, CancellationToken ct = default)
        {
            if (state.Coordination is not { } coordination || coordination.NodeId is not { } haltedAt)
                return state;

            if (coordination.Decision is not PlanDecision.ReplanPlan replan)
                return state with { Coordination = coordination with { Decision = PlanDecision.Ask([]) } };

            await supervision.ReruleAsync(
                coordination.Result.Tree.PlanId,
                replan.NodeId ?? haltedAt,
                replan.Contract!,
                coordination.HaltReason ?? "nothing recorded why.",
                replan.Children,
                replan.Rationale,
                ct);

            return state;
        }
    }

    /// <summary>The plan: one step that cannot be done as written, and says so.</summary>
    private static Workflow<DeliveryState> Importer()
    {
        var store = new InMemoryPlanTreeStore();
        var executor = new PlanExecutor(store);

        return AgenticPattern.SupervisedPlan<DeliveryState>("importer")
            .WithPlan(PlanTree.Create(
                "plan",
                Contract("Ship the importer", "the importer round-trips every fixture"),
                [
                    ("plan-parse", Contract("Parse the input", "the parser round-trips")),
                    ("plan-render", Contract("Render the report", "output matches the golden file"))
                ]))
            .Supervised(new SupervisionOptions
            {
                Store = store,
                // The one step nothing can satisfy, and it reports that rather than failing quietly.
                // Every node the person's re-ruling introduces can be satisfied.
                Runner = (context, ct) =>
                    context.Node.Id == "plan-parse"
                        ? Disputes(executor, context, ImpossibleCriterion, ImpossibleReason, ct)
                        : Task.FromResult(Answer(context, passed: true))
            })
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithCoordinator(Inbox)
            .EscalateToAPerson()
            .Build();
    }

    /// <summary>A plan whose one hard step yields only to the fifth contract it is given.</summary>
    /// <remarks>
    /// Every contract but the last is one the step reports it cannot meet — the halt a person is
    /// there to answer, four times over.
    /// </remarks>
    private static Workflow<DeliveryState> Stubborn()
    {
        var store = new InMemoryPlanTreeStore();
        var executor = new PlanExecutor(store);

        return AgenticPattern.SupervisedPlan<DeliveryState>("importer")
            .WithPlan(PlanTree.Create(
                "plan",
                Contract("Ship the importer", "the importer round-trips every fixture"),
                [
                    ("plan-parse", Contract("Parse the input", "the parser round-trips")),
                    ("plan-render", Contract("Render the report", "output matches the golden file"))
                ]))
            .Supervised(new SupervisionOptions
            {
                Store = store,
                Runner = (context, ct) =>
                    context.Node.Id == "plan-parse"
                    && context.Contract.Goal != "Parse the input, take 5"
                        ? Disputes(executor, context, ImpossibleCriterion, ImpossibleReason, ct)
                        : Task.FromResult(Answer(context, passed: true))
            })
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithCoordinator(Inbox)
            .EscalateToAPerson()
            .Build();
    }

    /// <summary>Two walls: one the coordinator can re-rule itself, one only a person can answer.</summary>
    private static Workflow<DeliveryState> TwoWalls()
    {
        var store = new InMemoryPlanTreeStore();
        var executor = new PlanExecutor(store);

        return AgenticPattern.SupervisedPlan<DeliveryState>("importer")
            .WithPlan(PlanTree.Create(
                "plan",
                Contract("Ship the importer", "the importer round-trips every fixture"),
                [
                    ("plan-parse", Contract("Parse the input", "the parser round-trips")),
                    ("plan-render", Contract("Render the report", "output matches the golden file"))
                ]))
            .Supervised(new SupervisionOptions
            {
                Store = store,
                Runner = (context, ct) => (context.Node.Id, context.Contract.Goal) switch
                {
                    ("plan-parse", "Parse the input") => Disputes(
                        executor, context, "the parser round-trips",
                        "the input has two dialects and the contract names one", ct),

                    // The second wall, and a different kind: the golden file is gone, so the attempt
                    // dies rather than disagreeing with its contract. The coordinator's rule answers
                    // disputes and has nothing to say about this one.
                    ("plan-render", "Render the report") =>
                        throw new InvalidOperationException("the golden file is missing"),

                    _ => Task.FromResult(Answer(context, passed: true))
                }
            })
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithCoordinator((coordination, _) => Task.FromResult(
                coordination.Decision
                ?? (coordination.Dispute is not null
                    ? PlanDecision.Replan(
                        Contract("Parse the documented dialect", "the parser round-trips"),
                        nodeId: "plan-parse")
                    : PlanDecision.Ask([]))))
            // Only the wall. A dispute is the coordinator's to answer, and it does.
            .EscalateToAPerson(coordination => coordination.Node?.Failure is not null)
            .Build();
    }

    /// <summary>A person's decision, put where the coordinator will look for it.</summary>
    private static Func<DeliveryState, DeliveryState> Answer(PlanDecision decision) =>
        state => state with { Coordination = state.Coordination! with { Decision = decision } };

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

    private const string ImpossibleCriterion = "the parser round-trips";
    private const string ImpossibleReason =
        "the input has two dialects and one parser cannot round-trip both";

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

    private sealed class Collecting(List<WorkflowEvent> events) : IWorkflowEventSink
    {
        public ValueTask ReportAsync(WorkflowEvent evt, CancellationToken ct = default)
        {
            events.Add(evt);
            return ValueTask.CompletedTask;
        }
    }
}
