using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// The seat that decides which steps there are, reached when nobody could propose a change to them.
/// </summary>
/// <remarks>
/// <b>Empty is the signal, and no new field carries it.</b> The Supervisor is the role that has seen
/// the halt, the tools and the tree; when it can propose no contract swap, what is wrong is the shape
/// of the plan, and shape is the Planner's. What must stay distinguishable is a supervisor that ran
/// out of tool rounds — that is a budget, not a plan, and re-planning on it would spend an expensive
/// seat on an accident.
/// </remarks>
[TestFixture]
public class PlannerInTheLoopTests
{
    [Test]
    public async Task NothingToPropose_ReachesThePlannerWithWhatWasRefused()
    {
        var seen = new List<string>();

        var after = await Run(
            author: (_, refused, _) =>
            {
                seen.AddRange(refused);
                return Task.FromResult<AuthoredPlan?>(Plan("kyoto"));
            },
            refused: ["'hakone' may not be asked \"nights(kyoto, 7)\": put it on the step for 'kyoto'"]);

        seen.ShouldHaveSingleItem().ShouldContain("put it on the step for 'kyoto'");
        after.Coordination!.Result.Tree.Current.Nodes.Keys.ShouldContain("kyoto");
    }

    [Test]
    public async Task AReAuthoredPlan_MintsAVersionAndTheRunCarriesOn()
    {
        var after = await Run((_, _, _) => Task.FromResult<AuthoredPlan?>(Plan("kyoto", "osaka")));

        var tree = after.Coordination!.Result.Tree;

        tree.Lineage.Count.ShouldBe(2);
        tree.Root.ChildIds.ShouldBe(["kyoto", "osaka"]);
        // R30: an answer that changes nothing further for the coordinator to decide is the absence
        // of a verb rather than one more.
        after.Coordination.Decision.ShouldBeNull();
        after.Coordination.Result.HaltedAt.ShouldBeNull("the halt is answered: the plan is different now");
    }

    [Test]
    public async Task APlannerThatWritesNothing_LeavesTheHaltStanding()
    {
        // A real answer: the plan is what it should be, and nothing here can help.
        var after = await Run((_, _, _) => Task.FromResult<AuthoredPlan?>(null));

        after.Coordination!.Result.Tree.Lineage.Count.ShouldBe(1);
        after.Coordination.Decision.ShouldBeNull();
    }

    [Test]
    public async Task APlanThatWouldNotRun_IsRefusedRatherThanExecutedToFindOut()
    {
        // Admitted before it runs, exactly as an authored plan is.
        var after = await Run(
            (_, _, _) => Task.FromResult<AuthoredPlan?>(Plan("kyoto")),
            admitCriterion: (_, _) => "a step is a place, and there is nowhere of that name");

        after.Coordination!.Result.Tree.Lineage.Count.ShouldBe(1);
    }

    [Test]
    public async Task AQuestionSomebodyIsAnswering_IsNotThePlannersToTakeOver()
    {
        var asked = false;

        var after = await Run(
            (_, _, _) => { asked = true; return Task.FromResult<AuthoredPlan?>(Plan("kyoto")); },
            question: new PlanQuestion { Options = [] });

        asked.ShouldBeFalse();
        after.Coordination!.Result.Tree.Lineage.Count.ShouldBe(1);
    }

    [Test]
    public void ARunningOutOfRounds_IsNotHavingNothingToOffer()
    {
        // The two empties mean opposite things: one is about the plan, the other about a budget.
        new PlanProposal { Options = [], Exhausted = true }.Empty.ShouldBeTrue();
        new PlanProposal { Options = [], Exhausted = true }.Exhausted.ShouldBeTrue();
        new PlanProposal { Options = [] }.Exhausted.ShouldBeFalse();
    }

    [Test]
    public void ReAuthoringAStepIdentically_KeepsWhatItAlreadyProved()
    {
        // AgentContract is a record, so `==` compares each member with the default comparer — and the
        // criteria are an IReadOnlyList<string>, compared by reference. A contract re-authored
        // character for character was therefore unequal to the one it replaced, and re-listing an
        // unchanged step silently threw its verdicts away.
        var tree = PlanTree.Create(
                "trip",
                Contract("A trip", "within_duration()"),
                [("tokyo", Contract("Four nights", "nights(tokyo, 4)"))])
            .WithVerdict("tokyo", new CriterionVerdict
            {
                Criterion = "nights(tokyo, 4)",
                Passed = true,
                Oracle = "the itinerary",
                At = DateTimeOffset.UnixEpoch
            });

        var again = tree.Rerule(
            "trip",
            Contract("A trip", "within_duration()"),
            "re-planned",
            [("tokyo", Contract("Four nights", "nights(tokyo, 4)"))]);

        again.Node("tokyo").Verdicts.ShouldNotBeEmpty();
        again.OutcomeOf("tokyo").ShouldBe(ContractOutcome.Met);
    }

    [Test]
    public void ReAuthoringDoesNotUnAbandonWhatSomebodyGaveUp()
    {
        // R10 in its narrowest form, and it holds by construction: a re-listed node keeps the
        // decision recorded on it.
        var tree = PlanTree.Create(
                "trip",
                Contract("A trip", "within_duration()"),
                [("hakone", Contract("Three nights", "nights(hakone, 3)"))])
            .Abandon("hakone", new PlanAbandonment
            {
                Reason = "the traveller would rather go without",
                By = "a person",
                At = DateTimeOffset.UnixEpoch
            });

        var again = tree.Rerule(
            "trip", Contract("A trip", "within_duration()"), "re-planned",
            [("hakone", Contract("One night instead", "nights(hakone, 1)"))]);

        again.LifecycleOf("hakone").ShouldBe(NodeLifecycle.Abandoned);
    }

    // ── Fixtures ──

    private static async Task<Held> Run(
        PlanAuthor author,
        IReadOnlyList<string>? refused = null,
        PlanQuestion? question = null,
        CriterionAdmission? admitCriterion = null)
    {
        var store = new InMemoryPlanTreeStore();
        var tree = Halted();
        await store.SaveAsync(tree).ConfigureAwait(false);

        var supervision = new SupervisionOptions { Store = store, AdmitCriterion = admitCriterion };

        var state = new Held
        {
            Coordination = new PlanCoordination
            {
                Result = new PlanRunResult
                {
                    Tree = tree,
                    Executed = ["hakone"],
                    Skipped = [],
                    Rulings = new Dictionary<string, Verification>(StringComparer.Ordinal),
                    HaltedAt = "hakone"
                },
                Refused = refused ?? [],
                Question = question
            }
        };

        var job = new PlanAuthorJob<Held>(
            "author", author, supervision,
            s => s.Coordination,
            (s, c) => s with { Coordination = c });

        return await job.ExecuteAsync(state).ConfigureAwait(false);
    }

    private static AuthoredPlan Plan(params string[] steps) => new()
    {
        Contract = Contract("A trip", "within_duration()"),
        Steps =
        [
            .. steps.Select(id => new AuthoredStep
            {
                Id = id, Contract = Contract($"Visit {id}", $"nights({id}, 2)")
            })
        ],
        Rationale = "a different shape"
    };

    private static PlanTree Halted() => PlanTree.Create(
            "trip",
            Contract("A trip", "within_duration()"),
            [("hakone", Contract("Three nights", "nights(hakone, 3)"))])
        .WithViolation("hakone", new PlanViolation
        {
            Criterion = "nights(hakone, 3)",
            Reason = "there is one room and the trip needs three nights",
            At = DateTimeOffset.UnixEpoch
        });

    private static AgentContract Contract(string goal, params string[] criteria) =>
        new() { Goal = goal, AcceptanceCriteria = criteria };

    private sealed record Held
    {
        public PlanCoordination? Coordination { get; init; }
    }
}
