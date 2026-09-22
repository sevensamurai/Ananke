using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Patterns;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// A coordinator that is a table rather than a model — the second implementation of the seat.
/// </summary>
/// <remarks>
/// <para>
/// The seam claimed a coordinator need not be a model. That claim rested on a delegate's signature
/// until something else filled the seat, so these tests are as much about the <em>seam</em> as about
/// this type: it is written against `PlanCoordination` and `PlanDecision` exactly as shipped, and it
/// goes into the pattern through the same slot.
/// </para>
/// <para>
/// The behaviour worth pinning is what it refuses: re-applying a replacement that has already been
/// tried. Retrying a transient failure is no longer this seat's — R30 puts that in the loop (E9), so
/// a rule now matches only on which node halted, never on how.
/// </para>
/// </remarks>
[TestFixture]
public class DeterministicPlanCoordinatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 31, 9, 0, 0, TimeSpan.Zero);

    // ── What the table decides ──

    [Test]
    public async Task Decide_AMatchingRule_ReRulesWithWhatTheTableWroteDown()
    {
        var rationale = new PlanRationale { By = "the runbook", Text = "the format needs two parsers" };

        var coordinator = new DeterministicPlanSupervisor(
        [
            new HaltRule
            {
                NodeId = "parse",
                Contract = Contract("Parse, differently"),
                Children = [("parse-strict", Contract("Strict")), ("parse-legacy", Contract("Legacy"))],
                ReruleNodeId = "root",
                Rationale = rationale
            }
        ]);

        var decision = await coordinator.DecideAsync(Halt("parse"));

        var replan = decision.ShouldBeOfType<PlanDecision.ReplanPlan>();
        replan.Contract!.Goal.ShouldBe("Parse, differently");
        replan.Children!.Select(c => c.Id).ShouldBe(["parse-strict", "parse-legacy"]);
        replan.Rationale.ShouldBe(rationale);

        // The node that stopped and the node whose contract has to change are routinely different.
        replan.NodeId.ShouldBe("root");
    }

    [Test]
    public async Task Decide_ARuleThatHasFired_IsNotHandedBackAgain()
    {
        // Re-applying the same replacement at every halt is a change of plan nobody authored, and it
        // spends the run's budget re-trying an answer that has already been tried.
        var coordinator = new DeterministicPlanSupervisor([AnyHalt("Try the other format")]);

        (await coordinator.DecideAsync(Halt("parse")))
            .ShouldBeOfType<PlanDecision.ReplanPlan>();

        (await coordinator.DecideAsync(Halt("parse")))
            .ShouldBeOfType<PlanDecision.AskPlan>();
    }

    [Test]
    public async Task Decide_SeveralRules_AreMatchedInTheOrderTheyWereWritten()
    {
        // Declared order, not "most specific wins". A table whose precedence has to be worked out is
        // a table nobody can review before the run, which is the whole reason to write one.
        var coordinator = new DeterministicPlanSupervisor(
        [
            AnyHalt("first"),
            new HaltRule { NodeId = "parse", Contract = Contract("more specific, but later") }
        ]);

        var decision = await coordinator.DecideAsync(Halt("parse"));

        decision.ShouldBeOfType<PlanDecision.ReplanPlan>().Contract!.Goal.ShouldBe("first");
    }

    [Test]
    public async Task Decide_ARuleForAnotherNode_DoesNotAnswerThisHalt()
    {
        var coordinator = new DeterministicPlanSupervisor(
        [
            new HaltRule { NodeId = "render", Contract = Contract("wrong node") }
        ]);

        (await coordinator.DecideAsync(Halt("parse")))
            .ShouldBeOfType<PlanDecision.AskPlan>();
    }

    [Test]
    public async Task Decide_AnEmptyTable_AsksWithNothingToOffer()
    {
        (await new DeterministicPlanSupervisor([]).DecideAsync(Halt("parse")))
            .ShouldBeOfType<PlanDecision.AskPlan>()
            .Options.ShouldBeEmpty();
    }

    [Test]
    public void Decide_APlanThatDidNotHalt_SaysSoRatherThanDecidingSomething()
    {
        var settled = new PlanCoordination
        {
            Result = new PlanRunResult
            {
                Tree = Tree(),
                Executed = [],
                Skipped = [],
                Rulings = new Dictionary<string, Verification>(),
                HaltedAt = null
            }
        };

        Should.Throw<InvalidOperationException>(() => new DeterministicPlanSupervisor([]).DecideAsync(settled))
            .Message.ShouldContain("did not halt");
    }

    // ── Through the pattern, in the same slot as a model ──

    [Test]
    public async Task AsCoordinator_ClosesTheLoop_WithNoModelAnywhereInTheRun()
    {
        // The claim the seam has been making since it shipped: a coordinator need not be a model.
        var coordinator = new DeterministicPlanSupervisor(
        [
            new HaltRule
            {
                NodeId = "plan-parse",
                Contract = Contract("Parse the new format", "the parser reads the new format")
            }
        ]);

        // A dispute reaches the tree through DisputeAsync now — an external call, never a node's own
        // report (R26) — so it is seeded before the run rather than returned by the runner.
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(PlanTree.Create(
            "plan",
            Contract("Ship it", "the build is green"),
            [("plan-parse", Contract("Parse", "the parser round-trips"))]));
        await new PlanExecutor(store).DisputeAsync(
            "plan", "plan-parse", "the parser round-trips",
            "round-tripping is impossible for the input as specified");

        var workflow = AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(PlanTree.Create(
                "plan",
                Contract("Ship it", "the build is green"),
                [("plan-parse", Contract("Parse", "the parser round-trips"))]))
            .Supervised(new SupervisionOptions
            {
                Store = store,
                Runner = (context, ct) => Task.FromResult(new NodeOutcome
                {
                    Verdicts =
                    [
                        .. context.Contract.AcceptanceCriteria.Select(c => new CriterionVerdict
                        {
                            Criterion = c, Passed = true, Oracle = "dotnet test", At = T0
                        })
                    ]
                })
            })
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithCoordinator(coordinator.AsCoordinator())
            .Build();

        var result = await workflow.RunAsync(new DeliveryState());

        result.State.Coordination!.Result.HaltedAt.ShouldBeNull();
        result.State.Coordination.Result.Tree.Lineage.Count.ShouldBe(2);
        result.State.Coordination.Result.Tree.OutcomeOf("plan-parse").ShouldBe(ContractOutcome.Met);
    }

    // ── Fixtures ──

    private sealed record DeliveryState
    {
        public PlanCoordination? Coordination { get; init; }
    }

    private static HaltRule AnyHalt(string goal) => new() { Contract = Contract(goal) };

    private static AgentContract Contract(string goal, params string[] criteria) =>
        new() { Goal = goal, AcceptanceCriteria = criteria.Length == 0 ? ["it is done"] : criteria };

    private static PlanTree Tree() => PlanTree.Create(
        "plan", Contract("Ship it"), [("parse", Contract("Parse")), ("render", Contract("Render"))]);

    /// <summary>A halt, as the coordinator job would hand it over.</summary>
    private static PlanCoordination Halt(string nodeId)
    {
        var tree = PlanTree.Create(
            "plan", Contract("Ship it"), [(nodeId, Contract("Do the thing"))]);

        return new PlanCoordination
        {
            Result = new PlanRunResult
            {
                Tree = tree,
                Executed = [nodeId],
                Skipped = [],
                Rulings = new Dictionary<string, Verification>(),
                HaltedAt = nodeId
            },
            MaxChanges = 3
        };
    }
}
