using Ananke.Design;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Design.Tests;

/// <summary>
/// How a run ended, in one sentence a reader can act on.
/// </summary>
/// <remarks>
/// The tier's own words — Blocked, Unmet, halted — are precise and mean nothing to somebody who has
/// not read the design, so every consumer writes this switch. Three endings are the domain's to word
/// (done, given up, nothing started) and the rest are facts about the run: which version was written
/// and never ran, which step stopped it, and whether anything was offered.
/// </remarks>
[TestFixture]
public class PlanRunEndingTests
{
    private static readonly PlanEndingWords Words = new()
    {
        Done = "Done: every night of the trip has a recommended stay.",
        GivenUp = "Given up: the trip was called off rather than left unfinished.",
        NothingStarted = "Stopped before the trip was planned, with no step halted."
    };

    [Test]
    public void Describe_ARunThatMetItsRoot_IsTheDomainsDoneSentence() =>
        PlanRunEnding.Describe(Coordination(Met()), Words).ShouldBe(Words.Done);

    [Test]
    public void Describe_ARunGivenUp_IsTheDomainsGivenUpSentence() =>
        PlanRunEnding.Describe(Coordination(GivenUp()), Words).ShouldBe(Words.GivenUp);

    [Test]
    public void Describe_AStepThatBroke_NamesIt() =>
        PlanRunEnding.Describe(Coordination(Faulted(), haltedAt: "parse"), Words)
            .ShouldBe("Something broke while planning 'parse'.");

    [Test]
    public void Describe_TheChangeOfPlanCeiling_NamesTheVersionThatNeverRan()
    {
        var ending = PlanRunEnding.Describe(
            Coordination(Unmet()) with { Changes = 3, MaxChanges = 3 }, Words);

        ending.ShouldBe(
            "Stopped: the plan changed 3 time(s), the most this run allows. "
            + "Version 1 was written and never ran.");
    }

    [Test]
    public void Describe_ARunThatStoppedWithNoStepHalted_IsTheDomainsSentence() =>
        PlanRunEnding.Describe(Coordination(Unmet()), Words).ShouldBe(Words.NothingStarted);

    [Test]
    public void Describe_AHaltNothingWasOfferedFor_SaysSo() =>
        PlanRunEnding.Describe(
                Coordination(Unmet(), haltedAt: "parse") with { Decision = PlanDecision.Ask([]) }, Words)
            .ShouldBe("Stopped at 'parse' — nothing was offered that the run could take.");

    [Test]
    public void Describe_AHaltThePlannerWroteNothingFor_SaysSo() =>
        PlanRunEnding.Describe(Coordination(Unmet(), haltedAt: "parse"), Words)
            .ShouldBe("Stopped at 'parse' — the Planner was asked and wrote nothing the run could use.");

    [Test]
    public void Describe_WithNoWords_StillEndsEveryRun() =>
        PlanRunEnding.Describe(Coordination(Met())).ShouldNotBeNullOrWhiteSpace();

    // ── Fixtures ──

    private static PlanCoordination Coordination(PlanTree tree, string? haltedAt = null) => new()
    {
        Result = new PlanRunResult
        {
            Tree = tree,
            Executed = ["parse"],
            Skipped = [],
            Rulings = new Dictionary<string, Verification>(),
            HaltedAt = haltedAt
        }
    };

    /// <summary>A root with nothing to meet is met, so the run completed.</summary>
    private static PlanTree Met() =>
        PlanTree.Create("plan", new AgentContract { Goal = "Ship it" }, [("parse", Contract())]);

    private static PlanTree Unmet() => PlanTree.Create(
        "plan",
        new AgentContract { Goal = "Ship it", AcceptanceCriteria = ["the build is green"] },
        [("parse", Contract())]);

    private static PlanTree GivenUp() => Unmet().Abandon(
        "plan", new PlanAbandonment { Reason = "called off", By = "a person", At = DateTimeOffset.UnixEpoch });

    private static PlanTree Faulted() => Unmet().WithFailure(
        "parse", new NodeFailure { Message = "the tool was not there", At = DateTimeOffset.UnixEpoch });

    private static AgentContract Contract() => new() { Goal = "Parse the input" };
}
