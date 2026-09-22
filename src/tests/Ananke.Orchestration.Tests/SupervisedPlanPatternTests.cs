using Ananke.Design;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Simulation;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Patterns;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Routing;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// The loop the plan tier was built for, composed from jobs the framework already had.
/// </summary>
/// <remarks>
/// <para>
/// A plan runs, a node reports its contract is wrong, a coordinator decides what the plan becomes,
/// and the work resumes under the new version. The claim under test is that closing that loop needed
/// <b>no new execution concept</b> — it is <c>Supervise</c>, <c>Job</c>, <c>Then</c> and <c>Loop</c>,
/// which is why the tests that matter most here are the ones an in-job loop could not pass:
/// <see cref="Build_InterruptBefore_StopsBetweenAssessmentAndResumption"/> and
/// <see cref="Build_AChangeOfPlan_IsAJobInTheRunsHistoryAndTopology"/>.
/// </para>
/// </remarks>
[TestFixture]
public class SupervisedPlanPatternTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);

    private sealed record DeliveryState
    {
        public PlanCoordination? Coordination { get; init; }
    }

    // ── The loop closes ──

    [Test]
    public async Task Build_ADisputeAndACoordinator_ReRulesAndResumes_WithNoConsumerControlFlow()
    {
        // A node reports its contract is wrong, the coordinator replaces it, and the plan carries on
        // to a satisfied root. Nothing in this test loops, and nothing here re-enters the plan.
        var result = await Delivery(
            Disputes("plan-parse", untilVersion: 2),
            (_, _) => Task.FromResult(PlanDecision.Replan(
                Contract("Parse the new format", "the parser reads the new format"))))
            .RunAsync(new DeliveryState());

        var outcome = result.State.Coordination!.Result;
        outcome.HaltedAt.ShouldBeNull();
        outcome.RootOutcome.ShouldBe(ContractOutcome.Met);
        outcome.Tree.Lineage.Count.ShouldBe(2); // the change of plan is a version

        // The version's reason is the node's own words, and the coordinator never supplied one.
        outcome.Tree.Lineage[1].Reason.ShouldBe(Dispute().Reason);
        outcome.Tree.Lineage[1].Rationale.ShouldBeNull();
    }

    [Test]
    public async Task Build_ACoordinatorWithAnArgumentForItsChange_RecordsItBesideTheReasonAndAttributed()
    {
        // Two things, never one field. The reason was observed at the halt in the node's words; the
        // rationale is what the coordinator concluded from it, signed by whoever concluded it.
        var result = await Delivery(
            Disputes("plan-parse", untilVersion: 2),
            (_, _) => Task.FromResult(PlanDecision.Replan(
                Contract("Parse the new format", "the parser reads the new format"),
                new PlanRationale { By = "planner", Text = "A different reader handles the format." })))
            .RunAsync(new DeliveryState());

        var version = result.State.Coordination!.Result.Tree.Lineage[1];

        version.Reason.ShouldBe(Dispute().Reason); // the witness's words, not the coordinator's
        version.Rationale!.By.ShouldBe("planner");
        version.Rationale.Text.ShouldBe("A different reader handles the format.");
    }

    [Test]
    public async Task Build_ACoordinatorThatStops_LeavesThePlanHaltedExactlyAsSuperviseAloneWould()
    {
        var stopped = await Delivery(Disputes("plan-parse"), (_, _) => Task.FromResult(PlanDecision.Ask([])))
            .RunAsync(new DeliveryState());

        var alone = await Unsupervised(Disputes("plan-parse")).RunAsync(new DeliveryState());

        stopped.State.Coordination!.Result.HaltedAt.ShouldBe("plan-parse");
        stopped.State.Coordination.Result.Tree.Lineage.Count
            .ShouldBe(alone.State.Coordination!.Result.Tree.Lineage.Count);

        // Stopping spends nothing from the budget: it ends the run rather than buying a pass.
        stopped.State.Coordination.Changes.ShouldBe(0);
    }

    [Test]
    public async Task Build_ASettledPlan_IsNeverPutToTheCoordinator()
    {
        var asked = 0;

        var result = await Delivery(Meets, (_, _) =>
        {
            asked++;
            return Task.FromResult(PlanDecision.Ask([]));
        }).RunAsync(new DeliveryState());

        asked.ShouldBe(0);
        result.State.Coordination!.Result.HaltedAt.ShouldBeNull();
    }

    [Test]
    public async Task Build_ACoordinatorThatKeepsReRuling_IsBoundedByTheChangeOfPlanCap()
    {
        // The observed live failure: a planner that answers a contradiction by authoring the same
        // contradiction again. Nothing about the tree stops this — the plan changes every time, so
        // the pass loop always sees progress — which is why changes of plan carry a bound of their own.
        var asked = 0;

        var result = await Delivery(
            Disputes("plan-parse"),
            (_, _) =>
            {
                asked++;
                return Task.FromResult(PlanDecision.Replan(
                    Contract("Parse", "the parser round-trips")));
            },
            maxChanges: 2)
            .RunAsync(new DeliveryState());

        asked.ShouldBe(2);
        result.State.Coordination!.Changes.ShouldBe(2);
        result.State.Coordination.Result.HaltedAt.ShouldBe("plan-parse"); // ends halted, not looping
    }

    [Test]
    public async Task Build_WithNoCeilingAsked_TheFrameworkInventsNone()
    {
        // What a run may spend is bounded by BudgetConfig, in the currency that runs out; what a node
        // may attempt is bounded by its contract. A ceiling on re-planning is a third judgement and
        // it is the consumer's — so an unset one is unset, not three.
        var asked = 0;

        var result = await Delivery(
            Disputes("plan-parse", untilVersion: 6),
            (_, _) =>
            {
                asked++;
                return Task.FromResult(PlanDecision.Replan(
                    Contract($"Parse, take {asked}", "the parser round-trips")));
            },
            maxChanges: null)
            .RunAsync(new DeliveryState());

        // Five changes of plan, against a default that used to stop it at three.
        asked.ShouldBe(5);
        result.State.Coordination!.Changes.ShouldBe(5);
        result.State.Coordination.MaxChanges.ShouldBeNull();
        result.State.Coordination.Result.HaltedAt.ShouldBeNull();
    }

    [Test]
    public async Task Build_ACoordinatorThatDecidesNothing_EndsTheRunWhereItStandsInsteadOfSpinning()
    {
        // "Stuck" is a condition, not a count. A coordinator that was asked and said nothing leaves
        // the plan unchanged and unsettled, so the next round would put the identical question to the
        // identical coordinator — detected where it happens rather than counted up to, which for a
        // loop whose every round is a whole plan pass is the difference between one wasted pass and
        // hundreds. Only a job coordinator can reach this; the delegate form returns a decision by
        // signature.
        var asked = 0;

        var workflow = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(Disputing("plan-parse"))
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithCoordinator(new Silent(() => asked++))
            .Build();

        var result = await workflow.RunAsync(new DeliveryState());

        result.Status.ShouldBe(ExecutionStatus.Completed);
        asked.ShouldBe(1);                                              // asked once, not a thousand times

        var coordination = result.State.Coordination.ShouldNotBeNull();

        // And it ends honestly: the halt still names the node, and nothing was minted or spent.
        coordination.Result.HaltedAt.ShouldBe("plan-parse");
        coordination.Result.Tree.Lineage.Count.ShouldBe(1);
        coordination.Changes.ShouldBe(0);
    }

    [Test]
    public async Task Build_WhatReachesTheCoordinator_CarriesTheWitnessStatementAndTheBudget()
    {
        PlanCoordination? seen = null;

        await Delivery(Disputes("plan-parse"), (coordination, _) =>
        {
            seen = coordination;
            return Task.FromResult(PlanDecision.Ask([]));
        }).RunAsync(new DeliveryState());

        seen!.NodeId.ShouldBe("plan-parse");
        seen.Dispute!.Reason.ShouldBe(Dispute().Reason);
        seen.Node!.Id.ShouldBe("plan-parse");
        seen.Changes.ShouldBe(0);
        seen.MaxChanges.ShouldBe(3);
    }

    [Test]
    public async Task Build_EveryDecision_IsReported_IncludingTheOnesThatChangeNothing()
    {
        // Two of the three decisions used to leave no trace at all: a retry changes nothing about
        // the plan and a stop ends the run, so the tier's central judgement was the one thing a
        // reader had to infer from what stopped happening.
        var events = new List<WorkflowEvent>();

        var workflow = Delivery(Disputes("plan-parse"), (_, _) => Task.FromResult(PlanDecision.Ask([])));

        await foreach (var evt in workflow.StreamAsync(new DeliveryState()))
            events.Add(evt);

        var decided = events.OfType<PlanDecisionTaken>().ShouldHaveSingleItem();

        decided.NodeId.ShouldBe("plan-parse");
        decided.Decision.ShouldBeOfType<PlanDecision.AskPlan>();

        // The budget is on the decision because that is when it is spent, and a reader deciding
        // whether a run gave up too early needs to know how much room it had left.
        decided.Changes.ShouldBe(0);
        decided.MaxChanges.ShouldBe(3);
    }

    [Test]
    public async Task Build_ADecision_IsReportedBeforeWhateverItDoes()
    {
        // The account has to read in the order it happened: what halted, what was decided, then the
        // version that decision minted — not a version appearing with nothing having asked for it.
        var events = new List<WorkflowEvent>();

        var workflow = Delivery(
            Disputes("plan-parse", untilVersion: 2),
            (_, _) => Task.FromResult(PlanDecision.Replan(
                Contract("Parse, differently", "the parser round-trips"))));

        await foreach (var evt in workflow.StreamAsync(new DeliveryState()))
            events.Add(evt);

        var decided = events.FindIndex(e => e is PlanDecisionTaken);
        var minted = events.FindIndex(e => e is PlanVersionMinted);

        decided.ShouldBeGreaterThanOrEqualTo(0);
        minted.ShouldBeGreaterThan(decided);
    }

    [Test]
    public async Task Build_ASettledPlan_ReportsNoDecision()
    {
        // Nothing halted, so nothing was asked. An event saying "stop" here would be a decision
        // nobody made.
        var events = new List<WorkflowEvent>();

        var workflow = Delivery(Meets, (_, _) => Task.FromResult(PlanDecision.Ask([])));

        await foreach (var evt in workflow.StreamAsync(new DeliveryState()))
            events.Add(evt);

        events.OfType<PlanDecisionTaken>().ShouldBeEmpty();
    }

    [Test]
    public async Task Build_ACoordinatorReRulingAnAncestor_RePlansTheRemainder()
    {
        // A node's input is the previous node's output, so a contradiction may invalidate the shape
        // of what follows rather than just the step that hit it. The coordinator finds the ancestor
        // from the tree the last pass returned; a coordinator that could see nothing but its own
        // node could not name the node it wants to re-plan.
        var result = await Delivery(
            Disputes("plan-parse", untilVersion: 2),
            (coordination, _) =>
            {
                var parent = coordination.Result.Tree.Current.Nodes.Values
                    .Single(n => n.ChildIds.Contains(coordination.NodeId!, StringComparer.Ordinal));

                return Task.FromResult(PlanDecision.Replan(
                    Contract("Ship it differently", "the build is green"),
                    children: [new AuthoredStep { Id = "plan-import", Contract = Contract("Import", "the importer runs") }],
                    nodeId: parent.Id));
            })
            .RunAsync(new DeliveryState());

        var tree = result.State.Coordination!.Result.Tree;
        tree.Current.Nodes.Keys.ShouldContain("plan-import");
        tree.Current.Nodes.Keys.ShouldNotContain("plan-render"); // the remainder was re-planned
        tree.Lineage[0].Nodes.Keys.ShouldContain("plan-render"); // dropped, not cancelled
    }

    // ── A node that fails ──

    [Test]
    public async Task Build_ANodeThatThrows_ReachesTheCoordinatorInsteadOfFaultingTheWorkflow()
    {
        // The whole of I18. A provider outage inside one node used to propagate out of the plan,
        // through the supervised job, and fault the run — so the most consequential judgement in the
        // tier was never asked about two of the three ways work can go wrong.
        PlanCoordination? seen = null;

        var result = await Delivery(
            Fails("plan-parse", "429 Too Many Requests (quota exhausted)", untilVersion: 2),
            (coordination, _) =>
            {
                seen = coordination;
                return Task.FromResult(PlanDecision.Replan(
                    Contract("Parse the new format", "the parser reads the new format")));
            })
            .RunAsync(new DeliveryState());

        result.IsFailure.ShouldBeFalse(); // the run reports; it does not throw
        seen!.Node!.Failure.ShouldNotBeNull();
        seen.NodeId.ShouldBe("plan-parse");
    }

    [Test]
    public async Task Build_AFailedNode_GivesTheCoordinatorTheFailuresOwnWords()
    {
        // R4's third row. A failure carries somebody else's error message, and it is the only
        // honest answer to "why did this stop?" for a cause that decides nothing.
        PlanCoordination? seen = null;

        await Delivery(
            Fails("plan-parse", "the tool loop exceeded 3 rounds", untilVersion: 2),
            (coordination, _) =>
            {
                seen = coordination;
                return Task.FromResult(PlanDecision.Replan(Contract("Parse", "it parses")));
            })
            .RunAsync(new DeliveryState());

        seen!.HaltReason.ShouldBe(
            "'plan-parse' failed: could not be run: 3 attempts, the tool loop exceeded 3 rounds");
    }

    // ── What an in-job loop could not do ──

    [Test]
    public async Task Build_AChangeOfPlan_IsAJobInTheRunsHistoryAndTopology()
    {
        // The whole argument for moving the loop out of the supervised job. A decision made inside
        // that job is invisible: it appears in no history, counts as no job, and draws no edge.
        var result = await Delivery(
            Disputes("plan-parse", untilVersion: 2),
            (_, _) => Task.FromResult(PlanDecision.Replan(
                Contract("Parse the new format", "the parser reads the new format"))))
            .RunAsync(new DeliveryState());

        var ran = result.Result!.History.Select(h => h.JobName).ToList();

        ran.ShouldContain(SupervisedPlanBuilder<DeliveryState>.PlanJob);
        ran.ShouldContain(SupervisedPlanBuilder<DeliveryState>.CoordinatorJob);
        result.Result.JobsExecuted.ShouldBeGreaterThan(2); // the plan ran twice, either side of a decision

        var dsl = Delivery(Meets, (_, _) => Task.FromResult(PlanDecision.Ask([]))).ToDsl();
        dsl.ShouldContain(l => l.Contains(SupervisedPlanBuilder<DeliveryState>.CoordinatorJob));
    }

    [Test]
    public async Task Build_InterruptBefore_StopsBetweenAssessmentAndResumption()
    {
        // A person between the halt and the change of plan. This is the case the in-job loop made
        // unreachable: there was no point in the run to stop at.
        var asked = 0;
        var store = new InMemoryCheckpointStore();

        var workflow = Delivery(
                Disputes("plan-parse", untilVersion: 2),
                (_, _) =>
                {
                    asked++;
                    return Task.FromResult(PlanDecision.Replan(
                        Contract("Parse the new format", "the parser reads the new format")));
                })
            .InterruptBefore(SupervisedPlanBuilder<DeliveryState>.CoordinatorJob)
            .UseCheckpointing(store);

        var first = await workflow.RunAsync(new DeliveryState());

        first.Status.ShouldBe(ExecutionStatus.Interrupted);
        asked.ShouldBe(0); // the plan was assessed; nothing has decided yet
        first.State.Coordination!.Result.HaltedAt.ShouldBe("plan-parse");
        first.State.Coordination.Decision.ShouldBeNull();

        var second = await workflow.ResumeAsync(first.Id);

        asked.ShouldBe(1); // the decision was taken on resume, not before the pause

        // The gate re-arms on every pass, which is what a human reviewing changes of plan wants:
        // the run comes back before each assessment, not only the first.
        second.Status.ShouldBe(ExecutionStatus.Interrupted);
        second.State.Coordination!.Result.Tree.Lineage.Count.ShouldBe(2);
        second.State.Coordination.Result.HaltedAt.ShouldBeNull(); // the new plan settled

        var third = await workflow.ResumeAsync(second.Id);

        third.Status.ShouldBe(ExecutionStatus.Completed);
        asked.ShouldBe(1); // a settled plan is never put to the coordinator
    }

    [Test]
    public async Task Build_ACoordinatorThatIsAJobRatherThanAModel_GoesInTheSameSlot()
    {
        // A queue, a person, a rules table. The slot takes a job, so a decider that is not a
        // function of the halt is not a special case.
        var supervision = Disputing("plan-parse", untilVersion: 2);

        var workflow = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(supervision)
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithCoordinator(new FromTheDrawer(supervision))
            .Build();

        var result = await workflow.RunAsync(new DeliveryState());

        result.State.Coordination!.Result.HaltedAt.ShouldBeNull();
        result.State.Coordination.Result.Tree.Lineage.Count.ShouldBe(2);
    }

    /// <summary>A coordinator that is not a model: the replacement contract was written in advance.</summary>
    private sealed class FromTheDrawer(SupervisionOptions supervision) : IJob<DeliveryState>
    {
        public string Name => SupervisedPlanBuilder<DeliveryState>.CoordinatorJob;

        public async Task<DeliveryState> ExecuteAsync(DeliveryState state, CancellationToken ct = default)
        {
            if (state.Coordination is not { } coordination || coordination.NodeId is not { } haltedAt)
                return state;

            var replacement = Contract("Parse the new format", "the parser reads the new format");

            await supervision.ReruleAsync(
                coordination.Result.Tree.PlanId,
                haltedAt,
                replacement,
                "a decision taken before the run started",
                ct: ct);

            return state with
            {
                Coordination = coordination with { Decision = PlanDecision.Replan(replacement), Changes = 1 }
            };
        }
    }

    // ── What the job form owes, and what it is owed ──

    [Test]
    public async Task Build_AJobCoordinatorThatOnlyReRules_StillSpendsTheChangeOfPlanBudget()
    {
        // The failure this closes: budget accounting used to be the job's to remember, so a job that
        // forgot left the cap unenforced and the loop's iteration backstop quietly standing in for
        // the number the consumer actually configured — while every coordinator reading
        // `coordination.Changes` to decide "is this my last one?" read a lie.
        var supervision = Disputing("plan-parse");

        var workflow = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(supervision)
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithCoordinator(new Forgetful(supervision))
            .MaxChangesOfPlan(2)
            .Build();

        var result = await workflow.RunAsync(new DeliveryState());

        // Two changes of plan, then the cap — not the loop's backstop, and not forever.
        result.State.Coordination!.Changes.ShouldBe(2);
        result.State.Coordination.Result.HaltedAt.ShouldBe("plan-parse"); // ends halted, not looping
    }

    [Test]
    public async Task Build_AJobCoordinatorsDecision_IsReportedLikeAnyOther()
    {
        // A decision that nothing reports is the tier's central act happening invisibly — to the
        // narrator, to a trace, and to every event consumer.
        var events = new List<WorkflowEvent>();
        var supervision = Disputing("plan-parse", untilVersion: 2);

        var workflow = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(supervision)
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithCoordinator(new Forgetful(supervision))
            .MaxChangesOfPlan(3)          // asked for, so the report has one to carry
            .Build();

        await foreach (var evt in workflow.StreamAsync(new DeliveryState()))
            events.Add(evt);

        var decided = events.OfType<PlanDecisionTaken>().ShouldHaveSingleItem();

        decided.NodeId.ShouldBe("plan-parse");
        decided.Decision.ShouldBeOfType<PlanDecision.ReplanPlan>();

        // The budget as it stood when the job was asked, so an audit reads the same for both forms.
        decided.Changes.ShouldBe(0);
        decided.MaxChanges.ShouldBe(3);
    }

    [Test]
    public async Task Build_AJobCoordinatorsDecision_IsReportedAfterItsEffects_UnlikeTheDelegateForm()
    {
        // The one guarantee the job form cannot have, pinned so the difference is visible rather
        // than assumed. A job applies its own decision, so by the time anything outside it can know
        // what was decided, the version is already minted. Reporting earlier would mean announcing a
        // decision nobody had taken yet.
        var events = new List<WorkflowEvent>();
        var supervision = Disputing("plan-parse", untilVersion: 2);

        var workflow = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(supervision)
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithCoordinator(new Forgetful(supervision))
            .Build();

        await foreach (var evt in workflow.StreamAsync(new DeliveryState()))
            events.Add(evt);

        events.FindIndex(e => e is PlanDecisionTaken)
            .ShouldBeGreaterThan(events.FindIndex(e => e is PlanVersionMinted));
    }

    [Test]
    public async Task Build_AJobCoordinatorThatSpentTheBudgetItself_IsNotChargedTwice()
    {
        // `FromTheDrawer` writes `Changes = 1` by hand, as every job coordinator had to before this.
        // The framework recomputes from what the budget *was*, so the careful job and the forgetful
        // one now agree.
        var supervision = Disputing("plan-parse", untilVersion: 2);

        var workflow = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(supervision)
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithCoordinator(new FromTheDrawer(supervision))
            .Build();

        var result = await workflow.RunAsync(new DeliveryState());

        result.State.Coordination!.Changes.ShouldBe(1);
    }

    [Test]
    public async Task Build_AJobCoordinatorThatDecidesNothing_IsNotMadeToLookDecisive()
    {
        // Silence is a legitimate answer — a queue with nothing in it, a person who has not replied.
        // Reporting a decision here would invent one, and spending budget would charge for it.
        var events = new List<WorkflowEvent>();

        var workflow = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(Disputing("plan-parse"))
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithCoordinator(new Speechless())
            .Build();

        await foreach (var evt in workflow.StreamAsync(new DeliveryState()))
            events.Add(evt);

        events.OfType<PlanDecisionTaken>().ShouldBeEmpty();
        events.OfType<PlanVersionMinted>().ShouldBeEmpty();
    }

    [Test]
    public async Task Build_AJobCoordinatorThatStops_SpendsNothing()
    {
        // Stopping buys no further pass, so there is nothing to charge for. Charging anyway would
        // make "give up" cost the same as "try something else", which is the wrong incentive to put
        // in front of a coordinator deciding whether it is worth continuing.
        var workflow = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(Disputing("plan-parse"))
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithCoordinator(new Defeated())
            .Build();

        var result = await workflow.RunAsync(new DeliveryState());

        result.State.Coordination!.Changes.ShouldBe(0);
        result.State.Coordination.Decision.ShouldBeOfType<PlanDecision.AskPlan>();
    }

    [Test]
    public async Task Build_AJobCoordinatorThatWroteItsOwnBudget_IsReportedWithTheBudgetAsItStood()
    {
        // `FromTheDrawer` writes `Changes = 1` itself. The reported budget is what the coordinator
        // was *given* when it was asked — a reader deciding whether a run gave up too early needs
        // the room it had, not a number the decider wrote about itself afterwards.
        var events = new List<WorkflowEvent>();
        var supervision = Disputing("plan-parse", untilVersion: 2);

        var workflow = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(supervision)
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithCoordinator(new FromTheDrawer(supervision))
            .Build();

        await foreach (var evt in workflow.StreamAsync(new DeliveryState()))
            events.Add(evt);

        events.OfType<PlanDecisionTaken>().ShouldHaveSingleItem().Changes.ShouldBe(0);
    }

    /// <summary>A job coordinator that judges the halt not worth another pass.</summary>
    private sealed class Defeated : IJob<DeliveryState>
    {
        public string Name => SupervisedPlanBuilder<DeliveryState>.CoordinatorJob;

        public Task<DeliveryState> ExecuteAsync(DeliveryState state, CancellationToken ct = default) =>
            Task.FromResult(state.Coordination is not { } coordination
                ? state
                : state with { Coordination = coordination with { Decision = PlanDecision.Ask([]) } });
    }

    /// <summary>A job coordinator that re-rules and does none of the bookkeeping.</summary>
    private sealed class Forgetful(SupervisionOptions supervision) : IJob<DeliveryState>
    {
        public string Name => SupervisedPlanBuilder<DeliveryState>.CoordinatorJob;

        public async Task<DeliveryState> ExecuteAsync(DeliveryState state, CancellationToken ct = default)
        {
            if (state.Coordination is not { } coordination || coordination.NodeId is not { } haltedAt)
                return state;

            var replacement = Contract("Parse the new format", "the parser reads the new format");

            await supervision.ReruleAsync(
                coordination.Result.Tree.PlanId, haltedAt, replacement,
                coordination.HaltReason ?? "it stopped", ct: ct);

            return state with
            {
                Coordination = coordination with { Decision = PlanDecision.Replan(replacement) }
            };
        }
    }

    /// <summary>A job coordinator with nothing to say: nobody has answered yet.</summary>
    private sealed class Speechless : IJob<DeliveryState>
    {
        public string Name => SupervisedPlanBuilder<DeliveryState>.CoordinatorJob;

        public Task<DeliveryState> ExecuteAsync(DeliveryState state, CancellationToken ct = default) =>
            Task.FromResult(state);
    }

    // ── The five-job loop (G4): plan -> propose -> author -> choose -> [review] ──

    [Test]
    public void ABuilderWithNoAdvisor_ProducesTheSameTopologyItAlwaysHas()
    {
        // The regression guard: neither WithAdvisor nor WithReviewer was called, so this must be
        // bit-identical to the topology every consumer of this builder already depends on.
        var definition = Delivery(Meets, (_, _) => Task.FromResult(PlanDecision.Ask([]))).Build();

        definition.Jobs.Keys.OrderBy(k => k, StringComparer.Ordinal).ShouldBe(["coordinate", "plan"]);

        definition.Connections.OfType<DirectConnection>()
            .ShouldContain(c => c.From == "plan" && c.To == "coordinate");
        definition.Connections.OfType<LoopConnection<DeliveryState>>()
            .ShouldContain(c => c.From == "coordinate" && c.LoopTarget == "plan" && c.ExitTarget == Workflow.End);
        definition.Connections.OfType<RouterConnection<DeliveryState>>().ShouldBeEmpty();

        definition.InputJobs.ShouldBeEmpty();
    }

    [Test]
    public void AnAdvisorInTheSupervisionOptions_IsRead()
    {
        // F5: the slot existed and nothing read it. Setting it on the options, with no WithAdvisor
        // call at all, must be enough to wire the job.
        PlanAdvisor advisor = (_, _) => Task.FromResult(new PlanProposal { Options = [] });

        var definition = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(new SupervisionOptions { Runner = Meets, Advisor = advisor })
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .Build()
            .Build();

        definition.Jobs.Keys.ShouldContain(SupervisedPlanBuilder<DeliveryState>.AdvisorJob);
    }

    [Test]
    public void AnAuthorInTheSupervisionOptions_IsRead()
    {
        // F7: the Planner had a name and no seat. Setting Author on the options, with no
        // WithPlanner call at all, must be enough to wire the job.
        PlanAuthor author = (_, _, _) => Task.FromResult<AuthoredPlan?>(null);

        var definition = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(new SupervisionOptions { Runner = Meets, Author = author })
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithAdvisor((_, _) => Task.FromResult(new PlanProposal { Options = [] }))
            .Build()
            .Build();

        definition.Jobs.Keys.ShouldContain(SupervisedPlanBuilder<DeliveryState>.PlannerJob);
    }

    [Test]
    public async Task WithAdvisor_WiresTheChooser_WhenNoCoordinatorWasGiven()
    {
        // An advisor implies the chooser: with no WithCoordinator at all, the offer still has to be
        // applied by something, or an unattended run would recommend forever and never decide.
        var rerule = Contract("Parse the new format", "the parser reads the new format");

        var workflow = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(Disputing("plan-parse", untilVersion: 2))
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithAdvisor(
                (_, _) => Task.FromResult(new PlanProposal
                {
                    Options = [new PlanOption { Summary = "reparse", Replan = true, Recommended = true }]
                }),
                unattended: true)
            .WithPlanner((_, _, _) => Task.FromResult<AuthoredPlan?>(new AuthoredPlan
            {
                Contract = Contract("Ship it", "the build is green"),
                Steps =
                [
                    new AuthoredStep { Id = "plan-parse", Contract = rerule },
                    new AuthoredStep
                    {
                        Id = "plan-render",
                        Contract = Contract("Render", "output matches the golden file")
                    }
                ]
            }))
            .Build();

        var result = await workflow.RunAsync(new DeliveryState());

        result.State.Coordination!.Result.HaltedAt.ShouldBeNull();
        result.State.Coordination.Result.Tree.Lineage.Count.ShouldBe(2); // the offer was applied
    }

    [Test]
    public async Task AHaltNobodyCouldProposeAgainst_ReachesThePlanner()
    {
        // R25, through the builder rather than a hand-wired workflow: an advisor that kept nothing
        // is a statement about the plan's shape, and shape is the Planner's to rewrite.
        var authored = new AuthoredPlan
        {
            Contract = Contract("Ship it, restructured", "the build is green"),
            Steps = [new AuthoredStep { Id = "plan-import", Contract = Contract("Import", "the importer runs") }]
        };

        var workflow = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(Disputing("plan-parse"))
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithAdvisor((_, _) => Task.FromResult(new PlanProposal { Options = [] }))
            .WithPlanner((_, _, _) => Task.FromResult<AuthoredPlan?>(authored))
            .Build();

        var result = await workflow.RunAsync(new DeliveryState());

        var tree = result.State.Coordination!.Result.Tree;
        result.State.Coordination.Result.HaltedAt.ShouldBeNull();
        tree.Current.Nodes.Keys.ShouldContain("plan-import");
        tree.Current.Nodes.Keys.ShouldNotContain("plan-parse");
    }

    [Test]
    public async Task Build_AReplanTheSupervisorOffers_ReachesThePlannerWithItsReading()
    {
        // A chosen replan carries no contract of its own: it is the Planner's signal to re-author,
        // and what the supervisor concluded travels with it rather than being applied directly.
        PlanCoordination? given = null;

        var workflow = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(Disputing("plan-parse"))
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithAdvisor(
                (_, _) => Task.FromResult(new PlanProposal
                {
                    Options =
                    [
                        new PlanOption
                        {
                            Summary = "restructure",
                            Replan = true,
                            Recommended = true,
                            Rationale = new PlanRationale { By = "supervisor", Text = "parsing is the wrong step" }
                        }
                    ]
                }),
                unattended: true)
            .WithPlanner((coordination, _, _) =>
            {
                given = coordination;
                return Task.FromResult<AuthoredPlan?>(new AuthoredPlan
                {
                    Contract = Contract("Ship it", "the build is green"),
                    Steps = [new AuthoredStep { Id = "plan-import", Contract = Contract("Import", "the importer runs") }]
                });
            })
            .Build();

        var result = await workflow.RunAsync(new DeliveryState()).WaitAsync(TimeSpan.FromSeconds(10));

        var replan = given!.Decision.ShouldBeOfType<PlanDecision.ReplanPlan>();
        replan.Contract.ShouldBeNull();
        replan.Rationale!.Text.ShouldBe("parsing is the wrong step");

        var coordination = result.State.Coordination!;
        coordination.Result.HaltedAt.ShouldBeNull();
        coordination.Result.Tree.Current.Nodes.Keys.ShouldContain("plan-import");
        coordination.Changes.ShouldBe(1);
    }

    [Test]
    public async Task Build_AnUnattendedRunThatKeepsReplanning_StopsAtMaxChangesOfPlan()
    {
        // The same bound a contract replan is held to: the Planner runs on the last allowed change
        // before the ceiling ends the run, not instead of it.
        var calls = 0;

        var workflow = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(Disputing("plan-parse"))
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithAdvisor(
                (_, _) => Task.FromResult(new PlanProposal
                {
                    Options =
                    [
                        new PlanOption
                        {
                            Summary = "restructure",
                            Replan = true,
                            Recommended = true,
                            Rationale = new PlanRationale { By = "supervisor", Text = "parsing is the wrong step" }
                        }
                    ]
                }),
                unattended: true)
            .WithPlanner((_, _, _) =>
            {
                calls++;
                return Task.FromResult<AuthoredPlan?>(new AuthoredPlan
                {
                    Contract = Contract("Ship it", "the build is green"),
                    Steps = [new AuthoredStep { Id = "plan-parse", Contract = Contract($"Parse, attempt {calls}", "the parser round-trips") }]
                });
            })
            .MaxChangesOfPlan(2)
            .Build();

        var result = await workflow.RunAsync(new DeliveryState()).WaitAsync(TimeSpan.FromSeconds(10));

        calls.ShouldBe(2);
        result.State.Coordination!.Changes.ShouldBe(2);
        result.Status.ShouldBe(ExecutionStatus.Completed);
    }

    [Test]
    public async Task Build_AReplanAPersonChose_IsNotCharged()
    {
        // A pause is bounded by somebody being there to answer, so a replan a person picked spends
        // nothing from the budget — the same rule a contract replan is already held to.
        var workflow = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(Disputing("plan-parse"))
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithAdvisor((_, _) => Task.FromResult(new PlanProposal
            {
                Options =
                [
                    new PlanOption
                    {
                        Summary = "restructure",
                        Replan = true,
                        Recommended = true,
                        Rationale = new PlanRationale { By = "supervisor", Text = "parsing is the wrong step" }
                    }
                ]
            }))
            .WithPlanner((_, _, _) => Task.FromResult<AuthoredPlan?>(new AuthoredPlan
            {
                Contract = Contract("Ship it", "the build is green"),
                Steps = [new AuthoredStep { Id = "plan-import", Contract = Contract("Import", "the importer runs") }]
            }))
            .Build()
            .UseCheckpointing(new InMemoryCheckpointStore());

        var first = await workflow.RunAsync(new DeliveryState()).WaitAsync(TimeSpan.FromSeconds(10));

        first.Status.ShouldBe(ExecutionStatus.Interrupted);

        var second = await workflow.ResumeAsync(first.Id, state => state with
        {
            Coordination = state.Coordination! with
            {
                Question = state.Coordination.Question! with { Picked = 1 }
            }
        }).WaitAsync(TimeSpan.FromSeconds(10));

        second.Status.ShouldBe(ExecutionStatus.Completed);
        second.State.Coordination!.Changes.ShouldBe(0);
        second.State.Coordination.Result.Tree.Current.Nodes.Keys.ShouldContain("plan-import");
    }

    [Test]
    public async Task Build_AReplan_ReportsWhatWasOfferedAndEachStepThePlannerWrote()
    {
        var events = new List<WorkflowEvent>();

        var workflow = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(Disputing("plan-parse"))
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithAdvisor(
                (_, _) => Task.FromResult(new PlanProposal
                {
                    Options = [new PlanOption { Summary = "restructure", Replan = true, Recommended = true }]
                }),
                unattended: true)
            .WithPlanner((_, _, _) => Task.FromResult<AuthoredPlan?>(new AuthoredPlan
            {
                Contract = Contract("Ship it", "the build is green"),
                Steps = [new AuthoredStep { Id = "plan-import", Contract = Contract("Import", "the importer runs") }]
            }))
            .Build();

        await foreach (var evt in workflow.StreamAsync(new DeliveryState()))
            events.Add(evt);

        events.OfType<PlanProposalOffered>().ShouldHaveSingleItem().NodeId.ShouldBe("plan-parse");

        var written = events.OfType<PlanReauthored>().ShouldHaveSingleItem().Authored.ShouldHaveSingleItem();
        written.Id.ShouldBe("plan-import");
        written.Contract.AcceptanceCriteria.ShouldBe(["the importer runs"]);
    }

    [Test]
    public async Task Build_AStepThatCouldNotDoItsTask_UnattendedTakesTheRecommendedOptionToThePlanner()
    {
        PlanCoordination? asked = null;

        var supervision = new SupervisionOptions
        {
            Store = new InMemoryPlanTreeStore(),
            Verifier = new DeterministicVerifier(
                [new PredicateCheck("the parser", ["the parser round-trips"], _ => false)]),
            Runner = (context, _) => Task.FromResult(context.Node.Id == "plan-parse"
                ? new NodeOutcome
                {
                    Summary = "only the documented dialect parses",
                    Done = false,
                    Options = ["parse the documented dialect only", "add a second parser"]
                }
                : Answer(context, passed: true)),
            Supervisor = SimulatedAgentModel.Json(new
            {
                option = 1,
                why = "the plan names one dialect"
            })
        };

        var workflow = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(supervision)
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithAdvisor(new AgentPlanAdvisor(supervision).AsAdvisor(), unattended: true)
            .WithPlanner((coordination, _, _) =>
            {
                asked = coordination;
                return Task.FromResult<AuthoredPlan?>(new AuthoredPlan
                {
                    Contract = Contract("Ship it", "the build is green"),
                    Steps = [new AuthoredStep { Id = "plan-import", Contract = Contract("Import", "the importer runs") }]
                });
            })
            .Build();

        var result = await workflow.RunAsync(new DeliveryState()).WaitAsync(TimeSpan.FromSeconds(10));

        asked.ShouldNotBeNull().Decision.ShouldBeOfType<PlanDecision.ReplanPlan>()
            .Rationale.ShouldNotBeNull().Text.ShouldContain("parse the documented dialect only");
        result.State.Coordination!.Changes.ShouldBe(1);
    }

    [Test]
    public async Task ASettledPlan_ReachesTheReviewer_AndARejectionBecomesAHalt()
    {
        // R11, through the builder: the goal is what the review reads, and a rejection travels as a
        // dispute against the root exactly the way any other halt does.
        var workflow = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(new SupervisionOptions { Runner = Meets })
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithCoordinator((_, _) => Task.FromResult(PlanDecision.Ask([])))
            .WithReviewer((_, _) => Task.FromResult(PlanReview.Reject("nothing brings the family home")))
            .Build();

        var result = await workflow.RunAsync(new DeliveryState());

        var coordination = result.State.Coordination!;
        coordination.Result.HaltedAt.ShouldBe("plan");
        coordination.Dispute!.Reason.ShouldBe("nothing brings the family home");
    }

    [Test]
    public async Task AnOutstandingQuestion_SendsTheRunBackToPropose_NotPastIt()
    {
        // The router's own claim, tested directly rather than through the pause that normally makes
        // it moot: an arrival at the coordinator carrying an unanswered question goes back to the
        // advisor, never past it to the plan or to End.
        var workflow = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(Disputing("plan-parse"))
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithAdvisor((_, _) => Task.FromResult(new PlanProposal
            {
                Options = [new PlanOption { Summary = "reparse", Replan = true }]
            }))
            .Build();

        var router = workflow.Build().Connections.OfType<RouterConnection<DeliveryState>>()
            .Single(c => c.From == SupervisedPlanBuilder<DeliveryState>.CoordinatorJob);

        var outstanding = new DeliveryState
        {
            Coordination = new PlanCoordination
            {
                Result = new PlanRunResult
                {
                    Tree = Tree(),
                    Executed = [],
                    Skipped = [],
                    Rulings = new Dictionary<string, Verification>(),
                    HaltedAt = "plan-parse"
                },
                Question = new PlanQuestion
                {
                    Options = [new PlanOption { Summary = "reparse", Replan = true }]
                }
            }
        };

        var target = await router.Router.RouteAsync(outstanding);

        target.ShouldBe(SupervisedPlanBuilder<DeliveryState>.AdvisorJob);
    }

    [Test]
    public async Task AnAnswerOffTheList_AlsoGoesBackToPropose_RatherThanReadingAsStuck()
    {
        // R32's free text, and the row it would otherwise fall into. Once an off-list answer has been
        // taken off the question, what is left — no question, no decision, still halted — is exactly
        // the shape this router reads as *stuck* and ends the run on. It is the opposite: somebody
        // has just said the most useful thing anybody has said all round, and the seat that can read
        // it has not seen it yet. It must not go to the plan either — the step is unchanged, and
        // re-attempting it would spend an executor on a halt nobody has answered.
        var workflow = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(Disputing("plan-parse"))
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithAdvisor((_, _) => Task.FromResult(new PlanProposal
            {
                Options = [new PlanOption { Summary = "reparse", Replan = true }]
            }))
            .Build();

        var router = workflow.Build().Connections.OfType<RouterConnection<DeliveryState>>()
            .Single(c => c.From == SupervisedPlanBuilder<DeliveryState>.CoordinatorJob);

        var said = new DeliveryState
        {
            Coordination = new PlanCoordination
            {
                Result = new PlanRunResult
                {
                    Tree = Tree(),
                    Executed = [],
                    Skipped = [],
                    Rulings = new Dictionary<string, Verification>(),
                    HaltedAt = "plan-parse"
                },
                Said = "neither of those — parse it as CSV and keep the header"
            }
        };

        (await router.Router.RouteAsync(said))
            .ShouldBe(SupervisedPlanBuilder<DeliveryState>.AdvisorJob);
    }

    // ── What the builder refuses ──

    [Test]
    public void Build_WithNoCoordinator_SaysWhatIsMissing_AndNamesTheAlternative()
    {
        var incomplete = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(new SupervisionOptions { Runner = Meets })
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c });

        var thrown = Should.Throw<InvalidOperationException>(() => incomplete.Build());

        thrown.Message.ShouldContain("coordinator is required");
        thrown.Message.ShouldContain("Supervise"); // a plan that decides nothing is the plain job
    }

    [Test]
    public void Build_WithNoStateSlot_SaysWhatIsMissing()
    {
        var incomplete = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(new SupervisionOptions { Runner = Meets })
            .WithCoordinator((_, _) => Task.FromResult(PlanDecision.Ask([])));

        Should.Throw<InvalidOperationException>(() => incomplete.Build())
            .Message.ShouldContain("Tracking");
    }

    // ── Fixtures ──

    private static Workflow<DeliveryState> Delivery(
        PlanNodeRunner runner, PlanSupervisor coordinator, int? maxChanges = 3)
    {
        var builder = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(new SupervisionOptions { Runner = runner })
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithCoordinator(coordinator);

        return (maxChanges is { } ceiling ? builder.MaxChangesOfPlan(ceiling) : builder).Build();
    }

    /// <summary>
    /// The same, for a runner built from a store the run's own executor shares — what
    /// <see cref="Disputes"/> needs, since a dispute reaches the tree through
    /// <see cref="PlanExecutor.DisputeAsync"/> now rather than through what the runner returns (R26).
    /// </summary>
    private static Workflow<DeliveryState> Delivery(
        Func<PlanExecutor, PlanNodeRunner> runner, PlanSupervisor coordinator, int? maxChanges = 3)
    {
        var store = new InMemoryPlanTreeStore();

        var builder = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(Tree())
            .Supervised(new SupervisionOptions { Store = store, Runner = runner(new PlanExecutor(store)) })
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithCoordinator(coordinator);

        return (maxChanges is { } ceiling ? builder.MaxChangesOfPlan(ceiling) : builder).Build();
    }

    /// <summary>The same plan with no coordinator at all, for comparing against.</summary>
    private static Workflow<DeliveryState> Unsupervised(PlanNodeRunner runner) =>
        new Workflow<DeliveryState>("delivery")
            .Supervise(
                "plan",
                Tree(),
                new SupervisionOptions { Runner = runner },
                (s, r) => s with { Coordination = new PlanCoordination { Result = r } })
            .Then("plan", Workflow.End);

    /// <summary>The same, for a <see cref="Disputes"/>-built runner. See the matching <c>Delivery</c> overload.</summary>
    private static Workflow<DeliveryState> Unsupervised(Func<PlanExecutor, PlanNodeRunner> runner)
    {
        var store = new InMemoryPlanTreeStore();

        return new Workflow<DeliveryState>("delivery")
            .Supervise(
                "plan",
                Tree(),
                new SupervisionOptions { Store = store, Runner = runner(new PlanExecutor(store)) },
                (s, r) => s with { Coordination = new PlanCoordination { Result = r } })
            .Then("plan", Workflow.End);
    }

    /// <summary>A <see cref="SupervisionOptions"/> whose one node disputes until the plan is re-ruled.</summary>
    private static SupervisionOptions Disputing(string nodeId, int untilVersion = int.MaxValue)
    {
        var store = new InMemoryPlanTreeStore();
        return new SupervisionOptions { Store = store, Runner = Disputes(nodeId, untilVersion)(new PlanExecutor(store)) };
    }

    /// <summary>A runner where one node throws until the plan is re-ruled.</summary>
    private static PlanNodeRunner Fails(string nodeId, string message, int untilVersion = int.MaxValue) =>
        (context, ct) => context.Node.Id == nodeId && context.PlanVersion < untilVersion
            ? throw new InvalidOperationException(message)
            : Meets(context, ct);

    /// <summary>
    /// A runner where one node disputes its contract until the plan is re-ruled — from outside the
    /// node, through the executor its run shares a store with, never through what it returns (R26).
    /// </summary>
    private static Func<PlanExecutor, PlanNodeRunner> Disputes(string nodeId, int untilVersion = int.MaxValue) =>
        executor => (context, ct) => context.Node.Id == nodeId && context.PlanVersion < untilVersion
            ? DisputeOutcome(executor, context, ct)
            : Meets(context, ct);

    private static async Task<NodeOutcome> DisputeOutcome(
        PlanExecutor executor, PlanNodeContext context, CancellationToken ct)
    {
        var dispute = Dispute();
        await executor.DisputeAsync(context.PlanId, context.Node.Id, dispute.Criterion, dispute.Reason, ct)
            .ConfigureAwait(false);
        return NodeOutcome.Nothing;
    }

    /// <summary>A coordinator job that runs and writes no decision — the wiring mistake, in a type.</summary>
    private sealed class Silent(Action onAsked) : IJob<DeliveryState>
    {
        public string Name => SupervisedPlanBuilder<DeliveryState>.CoordinatorJob;

        public Task<DeliveryState> ExecuteAsync(DeliveryState state, CancellationToken ct = default)
        {
            if (state.Coordination?.Result.HaltedAt is not null)
                onAsked();

            return Task.FromResult(state);
        }
    }

    private static PlanTree Tree(string planId = "plan") => PlanTree.Create(
        planId,
        Contract("Ship it", "the build is green"),
        [
            ($"{planId}-parse", Contract("Parse", "the parser round-trips")),
            ($"{planId}-render", Contract("Render", "output matches the golden file"))
        ]);

    private static AgentContract Contract(string goal, params string[] criteria) =>
        new() { Goal = goal, AcceptanceCriteria = criteria };

    private static Task<NodeOutcome> Meets(PlanNodeContext context, CancellationToken ct) =>
        Task.FromResult(Answer(context, passed: true));

    private static NodeOutcome Answer(PlanNodeContext context, bool passed) => new()
    {
        Verdicts = [.. context.Contract.AcceptanceCriteria.Select(c => Verdict(c, passed))]
    };

    private static CriterionVerdict Verdict(string criterion, bool passed) =>
        new() { Criterion = criterion, Passed = passed, Oracle = "dotnet test", At = T0 };

    private static PlanViolation Dispute() => new()
    {
        Criterion = "the parser round-trips",
        Reason = "round-tripping is impossible for the input format as specified",
        At = T0
    };
}
