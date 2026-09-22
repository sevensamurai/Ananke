using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// The record a Planner reads: the whole plan at a halt, rather than one step about to run.
/// </summary>
/// <remarks>
/// <para>
/// A supervisor is asked what to do about the step that stopped, so it is shown that step. A Planner
/// is asked which steps there should be, so it is shown the plan — every step with where it stands and
/// what it settled on, what binds the plan, why it stopped, and what has already been tried.
/// </para>
/// <para>
/// <b>What binds is what nothing has contradicted.</b> A term's end says why it stopped holding, not
/// whether it did, and the plan binds terms into what it authors on the same test.
/// </para>
/// </remarks>
[TestFixture]
public class PlanHaltRecordForPlannerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 16, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public void ForPlanner_CarriesThePlansOwnContract()
    {
        var record = PlanHaltRecord.ForPlanner(Halted(), []);

        record.ShouldContain("A week away");
        record.ShouldContain("week(1)");
        record.ShouldContain("no new dependencies");
    }

    [Test]
    public void ForPlanner_CarriesEveryStepWithWhereItStandsAndWhatItSettledOn()
    {
        var record = PlanHaltRecord.ForPlanner(Halted(), []);

        record.ShouldContain("tokyo-1");
        record.ShouldContain("hakone-1");
        record.ShouldContain("onsen-ryokan, 13 April");   // what the done step settled on
        record.ShouldContain(nameof(StepState.Done));
    }

    [Test]
    public void ForPlanner_CarriesWhyItStopped()
    {
        var record = PlanHaltRecord.ForPlanner(Halted(), []);

        record.ShouldContain("nothing is free that week");
    }

    [Test]
    public void ForPlanner_CarriesATermThatStillBinds()
    {
        var record = PlanHaltRecord.ForPlanner(Halted(), []);

        record.ShouldContain("the onsen stays in the plan");
        record.ShouldContain("keep the onsen");          // the words it was read from
    }

    [Test]
    public void ForPlanner_LeavesOutATermSomethingHasContradicted()
    {
        var record = PlanHaltRecord.ForPlanner(Halted(), []);

        record.ShouldNotContain("three cities or more");
    }

    [Test]
    public void ForPlanner_CarriesWhatWasRefused()
    {
        var record = PlanHaltRecord.ForPlanner(Halted(), ["a step may add no dependency"]);

        record.ShouldContain("a step may add no dependency");
    }

    [Test]
    public void ForPlanner_CarriesWhatThePlanAlreadyTried()
    {
        var record = PlanHaltRecord.ForPlanner(Replanned(), []);

        record.ShouldContain("What this plan already tried");
    }

    [Test]
    public void ForPlanner_AHaltWithNothingSaidOrRefused_ReadsWithoutThoseSections()
    {
        var record = PlanHaltRecord.ForPlanner(Halted(), []);

        record.ShouldNotContain("none of the options you offered");
        record.ShouldNotContain("already answered this once");
    }

    [Test]
    public void ForPlanner_CarriesWhatSomebodySaid()
    {
        var said = Halted() with { Said = "keep Naoshima, find the slack elsewhere" };

        PlanHaltRecord.ForPlanner(said, []).ShouldContain("keep Naoshima, find the slack elsewhere");
    }

    // ── Fixtures ──

    /// <summary>A week with one step settled and one that stopped, and two terms, one contradicted.</summary>
    private static PlanCoordination Halted()
    {
        var tree = PlanTree.Create(
            "trip",
            new AgentContract
            {
                Goal = "A week away",
                AcceptanceCriteria = ["week(1)"],
                Constraints = ["no new dependencies"]
            },
            [
                ("hakone-1", Step("Find a stay in hakone")),
                ("tokyo-1", Step("Find a stay in tokyo"))
            ]) with
        {
            Terms =
            [
                new PlanTerm
                {
                    Id = "t1",
                    Reading = "the onsen stays in the plan",
                    Said = "keep the onsen, whatever else moves",
                    By = "the traveller",
                    At = T0
                },
                new PlanTerm
                {
                    Id = "t2",
                    Reading = "three cities or more",
                    Said = "we want to see a lot",
                    By = "the traveller",
                    At = T0,
                    Contradicted = "the week is too short for three",
                    ContradictedAt = T0
                }
            ]
        };

        tree = tree
            .WithState("hakone-1", StepState.Done, "onsen-ryokan, 13 April")
            .WithViolation("tokyo-1", new PlanViolation
            {
                Criterion = "stay_on(tokyo, 2027-04-08)",
                Reason = "nothing is free that week",
                At = T0
            });

        return new PlanCoordination
        {
            Result = new PlanRunResult
            {
                Tree = tree,
                Executed = ["hakone-1", "tokyo-1"],
                Skipped = [],
                Rulings = new Dictionary<string, Verification>(),
                HaltedAt = "tokyo-1"
            }
        };
    }

    /// <summary>The same halt, after the plan has been re-ruled once.</summary>
    private static PlanCoordination Replanned()
    {
        var halted = Halted();

        var tree = halted.Result.Tree.Rerule(
            "trip",
            halted.Result.Tree.Root.Contract,
            "nothing was free in tokyo that week",
            [("kyoto-1", Step("Find a stay in kyoto"))]);

        return halted with { Result = halted.Result with { Tree = tree, HaltedAt = "kyoto-1" } };
    }

    private static AgentContract Step(string goal) =>
        new() { Goal = goal, AcceptanceCriteria = ["stay_on(somewhere, 2027-04-08)"] };
}
