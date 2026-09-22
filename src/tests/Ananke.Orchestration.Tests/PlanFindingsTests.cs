using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// What a plan would leave unmet if it ran, which is a different question from whether it is a plan.
/// </summary>
/// <remarks>
/// <see cref="PlanAdmission"/> answers whether a plan may run at all — a step that is its own ancestor,
/// a criterion nothing can decide. This runs the criteria that can be decided and reports the ones that
/// do not hold, so a plan nobody has executed can be refused before a pass is spent on it.
/// </remarks>
[TestFixture]
public class PlanFindingsTests
{
    [Test]
    public async Task Unmet_ADraftWhoseCriteriaAllHold_FindsNothing()
    {
        var found = await PlanFindings.UnmetAsync(Draft(), [Checks()], TestContext.CurrentContext.CancellationToken);

        found.ShouldBeEmpty();
    }

    [Test]
    public async Task Unmet_ACriterionThatDoesNotHold_ReadsAsTheCriterionAndWhy()
    {
        var checks = Checks(free: "tokyo");

        var found = await PlanFindings.UnmetAsync(Draft(), [checks], TestContext.CurrentContext.CancellationToken);

        found.ShouldHaveSingleItem().ShouldBe("stay_on(hakone, 2027-04-13): hakone is not free");
    }

    [Test]
    public async Task Unmet_EveryStepsCriteriaThenTheRoots_AreAsked()
    {
        var asked = new List<string>();

        await PlanFindings.UnmetAsync(Draft(), [Checks(asked: asked)], TestContext.CurrentContext.CancellationToken);

        // The steps in the order the plan holds them, and the whole plan's own criteria last: a check
        // over the week reads what every step asks for, so it cannot be ruled on before them.
        asked.ShouldBe(["stay_on(tokyo, 2027-04-08)", "stay_on(hakone, 2027-04-13)", "week(1)"]);
    }

    [Test]
    public async Task Unmet_ACriterionNothingCanRule_SaysSoWithoutRunningAnything()
    {
        var asked = new List<string>();
        var tree = Draft(extra: "paint(the-shed)");

        var found = await PlanFindings.UnmetAsync(tree, [Checks(asked: asked)], TestContext.CurrentContext.CancellationToken);

        found.ShouldContain("paint(the-shed): nothing can decide it");
        asked.ShouldNotContain("paint(the-shed)");
    }

    [Test]
    public async Task Unmet_ACriterionThatCouldNeverHold_SaysWhyWithoutRunningIt()
    {
        var asked = new List<string>();
        var tree = Draft(extra: "stay_on(atlantis, 2027-04-13)");

        var found = await PlanFindings.UnmetAsync(tree, [Checks(asked: asked)], TestContext.CurrentContext.CancellationToken);

        found.ShouldContain("stay_on(atlantis, 2027-04-13): there is nowhere called 'atlantis'");
        asked.ShouldNotContain("stay_on(atlantis, 2027-04-13)");
    }

    [Test]
    public async Task Unmet_NoChecksAtAll_LeavesEveryCriterionUndecided()
    {
        var found = await PlanFindings.UnmetAsync(Draft(), [], TestContext.CurrentContext.CancellationToken);

        found.Count.ShouldBe(3);
        found.ShouldAllBe(finding => finding.EndsWith("nothing can decide it", StringComparison.Ordinal));
    }

    // ── Fixtures ──

    /// <summary>A plan of two stays under a week, as a Planner would have drafted it.</summary>
    private static PlanTree Draft(string? extra = null)
    {
        var steps = new List<(string, AgentContract)>
        {
            ("tokyo-1", Step("stay_on(tokyo, 2027-04-08)")),
            ("hakone-1", Step("stay_on(hakone, 2027-04-13)"))
        };

        if (extra is not null)
            steps.Add(("extra-1", Step(extra)));

        return PlanTree.Create(
            "trip",
            new AgentContract { Goal = "A week away", AcceptanceCriteria = ["week(1)"] },
            steps);
    }

    private static AgentContract Step(string criterion) =>
        new() { Goal = "Find a stay", AcceptanceCriteria = [criterion] };

    /// <summary>
    /// What the world says: a stay is free unless <paramref name="free"/> names somewhere else, and
    /// nowhere called 'atlantis' exists at all.
    /// </summary>
    private static InvocationCheck Checks(string? free = null, List<string>? asked = null) => new(
        "the world",
        [
            CheckDefinition.Of(
                "stay_on", 2,
                "a stay in arg1 is free on arg2",
                c =>
                {
                    asked?.Add(c.ToString());
                    return free is null || c.Argument(0) == free
                        ? Finding.Held
                        : Finding.Not($"{c.Argument(0)} is not free");
                },
                c => c.Argument(0) == "atlantis" ? "there is nowhere called 'atlantis'" : null),

            CheckDefinition.Of(
                "week", 1,
                "arg1 week(s) are planned",
                c =>
                {
                    asked?.Add(c.ToString());
                    return Finding.Held;
                })
        ]);
}
