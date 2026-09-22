using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// A criterion that resolves, and a plan that has to be one before it runs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written against a measured failure.</b> An escalated supervisor with tools diagnosed a halt
/// correctly, proposed a change of plan that works, had it chosen by a person — and every criterion
/// of the plan it wrote then abstained, because the checks recognise text and it had used its own
/// words. The run ended <em>Partial</em> having decided nothing. While a criterion is a sentence, a
/// re-planned step is an unchecked step.
/// </para>
/// </remarks>
[TestFixture]
public class ExecutableCriteriaTests
{
    // ── Reading a criterion ──

    [Test]
    public void Parse_AnInvocation_ReadsTheNameAndItsArguments()
    {
        var criterion = Criterion.Parse("covers(day-3, island-tour)");

        criterion.ShouldNotBeNull();
        criterion.Name.ShouldBe("covers");
        criterion.Arguments.ShouldBe(["day-3", "island-tour"]);
        criterion.ToString().ShouldBe("covers(day-3, island-tour)");
    }

    [Test]
    public void Parse_WithNoArguments_IsStillAnInvocation()
    {
        Criterion.Parse("builds()")!.Arguments.ShouldBeEmpty();
    }

    [TestCase("every day fits the party's daily limit")]
    [TestCase("fits day-3")]
    [TestCase("(day-3)")]
    [TestCase("fits(day-3")]
    [TestCase("fits(nested(day-3))")]
    [TestCase("")]
    public void Parse_WhatIsNotAnInvocation_IsNotOne(string text)
    {
        // Prose is an answer, not an error: a plan may hold criteria nothing can run, and they are
        // marked and counted rather than refused.
        Criterion.Parse(text).ShouldBeNull();
    }

    // ── Resolving it ──

    [Test]
    public async Task ACriterionNamingAStepNobodyRegistered_IsStillDecided()
    {
        // The whole point. `day-3a` was invented by a supervisor at a halt; nothing was told about
        // it in advance, and the check decides it anyway because the step is an argument.
        var check = Checks();

        check.CanRule("fits(day-3a)").ShouldBeTrue();
        (await check.RunAsync("fits(day-3a)")).Holds.ShouldBeTrue();

        check.CanRule("fits(day-4b)").ShouldBeTrue();
    }

    [Test]
    public void AnUnknownName_StillAbstains()
    {
        // Resolution removes the accidental gap, not the deliberate one. A check that answered for
        // anything would make abstention impossible, and abstention is what keeps the unverified
        // visible.
        Checks().CanRule("smells_right(day-3)").ShouldBeFalse();
    }

    [Test]
    public void AKnownNameWithTheWrongNumberOfArguments_Abstains()
    {
        // `covers(day)` and `covers(day, stop)` are not the same question, and answering the wrong
        // one is worse than answering neither.
        Checks().CanRule("covers(day-3)").ShouldBeFalse();
        Checks().CanRule("covers(day-3, island-tour)").ShouldBeTrue();
    }

    [Test]
    public void RunningWhatItCannotRule_Throws_RatherThanGuessing()
    {
        Should.Throw<InvalidOperationException>(() => Checks().RunAsync("smells_right(day-3)"))
            .Message.ShouldContain("CanRule");
    }

    [Test]
    public void TheLegend_NamesEveryCheckAndWhatItDecides()
    {
        // A model authoring criteria has to be told what exists. Left to guess it writes true,
        // decidable-sounding statements nothing is prepared to decide.
        var legend = Checks().Legend();

        legend.ShouldContain("fits(arg1)");
        legend.ShouldContain("covers(arg1, arg2)");
        legend.ShouldContain("the day fits the daily limit");
    }

    // ── Admitting a plan ──

    [Test]
    public void APlanWhoseCriteriaAllResolve_IsAdmitted()
    {
        PlanAdmission.Faults(Plan("fits(day-1)"), [Checks()]).ShouldBeEmpty();
    }

    [Test]
    public void APlanNamingACheckThatDoesNotExist_IsRefusedBeforeItRuns()
    {
        var faults = PlanAdmission.Faults(Plan("smells_right(day-1)"), [Checks()]);

        faults.ShouldHaveSingleItem().NodeId.ShouldBe("day-1");
        faults[0].Problem.ShouldContain("smells_right(day-1)");
        faults[0].Problem.ShouldContain("could never be shown to have been done");
    }

    [Test]
    public void APlanWithProseCriteria_IsRefusedUnlessTheProseWasDeclaredJudged()
    {
        var plan = Plan("somebody has to look at it");

        PlanAdmission.Faults(plan, [Checks()]).ShouldNotBeEmpty();

        // Declared, and therefore allowed — and countable, which is the point of declaring it.
        PlanAdmission.Faults(plan, [Checks()], judged: ["somebody has to look at it"]).ShouldBeEmpty();
        PlanAdmission.Judged(plan, [Checks()])["day-1"].ShouldBe(1);
    }

    [Test]
    public void WithNoChecksAtAll_AnyCriterionIsAdmitted()
    {
        // What a plan with no deterministic checks has always done. Admission does not invent a
        // requirement that nothing was going to enforce.
        PlanAdmission.Faults(Plan("anything at all")).ShouldBeEmpty();
    }

    [Test]
    public void AStepWithNoGoal_IsRefused()
    {
        var tree = PlanTree.Create(
            "trip",
            new AgentContract { Goal = "Plan a trip", AcceptanceCriteria = ["fits(root)"] },
            [("day-1", new AgentContract { Goal = "  ", AcceptanceCriteria = ["fits(day-1)"] })]);

        PlanAdmission.Faults(tree, [Checks()])
            .ShouldContain(fault => fault.NodeId == "day-1" && fault.Problem == "it has no goal");
    }

    [Test]
    public void ACriterionThatResolvesAndCanNeverHold_IsRefusedToo()
    {
        // Measured: a supervisor wrote covers(day-3b, return) — a real check, the right arity, and a
        // stop that does not exist. It resolves, it runs, and it answers no for ever, so the step
        // disputes a contract that was impossible when it was written.
        var checks = new InvocationCheck("itinerary-check",
        [
            CheckDefinition.Of("fits", 1, "the day fits the daily limit", _ => true),
            CheckDefinition.Of("covers", 2, "the day includes that stop", _ => true,
                validate: c => c.Argument(1) == "return" ? "there is no stop called 'return'" : null)
        ]);

        checks.CanRule("covers(day-3, return)").ShouldBeTrue("it resolves, which is the trap");
        checks.Nonsense("covers(day-3, return)").ShouldNotBeNull();

        var faults = PlanAdmission.Faults(Plan("covers(day-1, return)"), [checks]);

        faults.ShouldHaveSingleItem().Problem.ShouldContain("could never hold");
    }

    [Test]
    public void ArgumentsThatAreMerelyNotSatisfiedYet_AreNotNonsense()
    {
        // The line that keeps this from refusing every plan before it runs: a day nobody has planned
        // is not an invalid argument, it is an unfinished one.
        var checks = new InvocationCheck("itinerary-check",
        [
            CheckDefinition.Of("fits", 1, "the day fits", _ => false)
        ]);


        checks.Nonsense("fits(day-never-planned)").ShouldBeNull();
        PlanAdmission.Faults(Plan("fits(day-never-planned)"), [checks]).ShouldBeEmpty();
    }

    // ── Fixtures ──

    private static InvocationCheck Checks() => new(
        "itinerary-check",
        [
            CheckDefinition.Of("fits", 1, "the day fits the daily limit", _ => true),
            CheckDefinition.Of("covers", 2, "the day includes that stop", _ => true)
        ]);

    private static PlanTree Plan(string criterion) => PlanTree.Create(
        "trip",
        new AgentContract { Goal = "Plan a trip", AcceptanceCriteria = ["fits(root)"] },
        [("day-1", new AgentContract { Goal = "Plan day 1", AcceptanceCriteria = [criterion] })]);
}
