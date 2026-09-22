using Ananke.Design;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Design.Tests;

/// <summary>
/// The switch every consumer would otherwise write, shipped once.
/// </summary>
/// <remarks>
/// <para>
/// Plan events are typed because each carries something the others do not, and the cost of that is a
/// consumer writing forty lines to print them. What is under test is the part of the narration that
/// is genuinely stateful — a node's start and its report read as one line, steps and passes are
/// numbered — plus the judgement about what is <em>not</em> worth a line, because a run that
/// narrated every gate it cleared would bury the few events that matter.
/// </para>
/// </remarks>
[TestFixture]
public class PlanNarratorTests
{
    [Test]
    public void Describe_ANodeThatStarted_SaysNothingUntilSomethingElseArrives() =>
        new PlanNarrator().Describe(Started("survey")).ShouldBeNull();

    [Test]
    public void Describe_ANodeThatStartedAndReported_IsOneLine()
    {
        var narrator = new PlanNarrator();
        narrator.Describe(Started("survey"));

        var line = narrator.Describe(Reported("survey", "Four render paths found."));

        line!.ShouldBe("   1. survey                   Four render paths found.");
    }

    [Test]
    public void Describe_ANodeThatNeverReported_StillGetsItsLine()
    {
        // A node that ran, read its surroundings and had nothing to add still happened, and a
        // narration that dropped it would make the run look shorter than it was.
        var narrator = new PlanNarrator();
        narrator.Describe(Started("survey"));

        narrator.Describe(Started("choose")).ShouldBe("   1. survey                   ");
    }

    [Test]
    public void Describe_Steps_AreNumberedInTheOrderTheyRan()
    {
        var narrator = new PlanNarrator();
        narrator.Describe(Started("survey"));
        narrator.Describe(Reported("survey", "one"));
        narrator.Describe(Started("choose"));

        narrator.Describe(Reported("choose", "two")).ShouldStartWith("   2. choose");
    }

    [Test]
    public void Describe_ANodeThatDidNotSeeEverything_SaysWhatItWasNotShown()
    {
        var narrator = new PlanNarrator();
        narrator.Describe(Started("survey") with { ProjectedTokens = 389, OmittedRecords = 2 });

        var line = narrator.Describe(Reported("survey", "done"));

        line!.ShouldContain("read 389 tokens of the plan; 2 record(s) not shown");
    }

    [Test]
    public void Describe_ADispute_NamesTheCriterionTheNodeWillNotRouteAround()
    {
        var line = new PlanNarrator().Describe(new PlanNodeDisputed
        {
            WorkflowName = "w",
            ExecutionId = "e",
            PlanId = "p",
            PlanVersion = 1,
            NodeId = "design",
            Criterion = "It handles the largest tenant.",
            Reason = "4 GB"
        });

        line!.ShouldContain("⚠ disputes its contract: \"It handles the largest tenant.\"");
    }

    [Test]
    public void Describe_ARulingWithAnAbstention_NamesWhatNothingCouldDecide()
    {
        var line = new PlanNarrator().Describe(Ruled(VerificationOutcome.Abstained, "It is documented."));

        line!.ShouldBe("      → nothing could decide \"It is documented.\"");
    }

    [Test]
    public void Describe_ARulingThatSimplyPassed_SaysNothing() =>
        new PlanNarrator().Describe(Ruled(VerificationOutcome.Passed)).ShouldBeNull();

    [Test]
    public void Describe_ARulingThatPassed_IsALineWhenPassedChecksAreAskedFor() =>
        new PlanNarrator(lines: PlanNarratorLines.PassedChecks).Describe(Ruled(VerificationOutcome.Passed))
            .ShouldBe("      ✓ n: the checks agree it is done");

    [Test]
    public void Describe_AToolCall_SaysNothingUnlessAskedFor() =>
        new PlanNarrator().Describe(ToolCalled()).ShouldBeNull();

    [Test]
    public void Describe_AToolCall_IsALineWhenToolCallsAreAskedFor() =>
        new PlanNarrator(lines: PlanNarratorLines.ToolCalls).Describe(ToolCalled())
            .ShouldBe("""      tool plan-node:design: search {"place":"hakone"} → one stay""");

    [Test]
    public void Describe_AToolCallThatWasCut_SaysHowLongTheWholeResultWas() =>
        new PlanNarrator(lines: PlanNarratorLines.ToolCalls)
            .Describe(ToolCalled() with { Result = "one stay", ResultLength = 900 })!
            .ShouldContain("one stay … (900 characters)");

    [Test]
    public void Describe_AToolCallThatErrored_SaysSo() =>
        new PlanNarrator(lines: PlanNarratorLines.ToolCalls)
            .Describe(ToolCalled() with { Result = "nowhere called 'x'", ResultLength = 18, IsError = true })!
            .ShouldContain("→ error: nowhere called 'x'");

    private static AgentToolCalled ToolCalled() => new()
    {
        WorkflowName = "w",
        ExecutionId = "e",
        AgentName = "plan-node:design",
        ToolName = "search",
        Arguments = """{"place":"hakone"}""",
        Result = "one stay",
        ResultLength = 8
    };

    [Test]
    public void Describe_ARulingThatFailedItsGate_SaysSo() =>
        new PlanNarrator().Describe(Ruled(VerificationOutcome.GateFailed))
            .ShouldBe("      → a check says no");

    [Test]
    public void Describe_APassCompleted_CountsThePassAndWhatRan()
    {
        var narrator = new PlanNarrator();
        narrator.Describe(Pass(["a", "b"], []));

        var second = narrator.Describe(Pass(["a"], ["b"], haltedAt: "a"));

        second!.ShouldStartWith("     ── pass 2: 1 step(s) ran, 1 skipped as already done, halted at a");
    }

    [Test]
    public void Describe_AVersionMinted_NamesTheReasonAndTheWorkItDropped()
    {
        var line = new PlanNarrator().Describe(new PlanVersionMinted
        {
            WorkflowName = "w",
            ExecutionId = "e",
            PlanId = "p",
            PlanVersion = 2,
            ReRuledNodeId = "delivery",
            Reason = "The report is 4 GB.",
            DroppedNodeIds = ["build-buffer"]
        });

        line!.ShouldContain("the plan changed — version 2, new instructions for 'delivery'");
        line!.ShouldContain("reason: The report is 4 GB.");
        line!.ShouldContain("no longer part of the plan: build-buffer");
    }

    [Test]
    public void Describe_AVersionMintedWithARationale_KeepsItOnItsOwnLineAndNamesWhoSaidIt()
    {
        // What was observed and what somebody concluded from it read identically once they share a
        // label. The lineage is a record, and a reader has to be able to see where it stops being one.
        var line = new PlanNarrator().Describe(new PlanVersionMinted
        {
            WorkflowName = "w",
            ExecutionId = "e",
            PlanId = "p",
            PlanVersion = 2,
            ReRuledNodeId = "delivery",
            Reason = "No buffer size makes this work.",
            Rationale = new PlanRationale { By = "planner", Text = "Streaming keeps memory flat." }
        });

        line!.ShouldContain("reason: No buffer size makes this work.");
        line!.ShouldContain("planner says: Streaming keeps memory flat.");
    }

    [Test]
    public void Describe_AVersionMintedWithNoRationale_AddsNoLineForOne()
    {
        var line = new PlanNarrator().Describe(new PlanVersionMinted
        {
            WorkflowName = "w",
            ExecutionId = "e",
            PlanId = "p",
            PlanVersion = 2,
            ReRuledNodeId = "delivery",
            Reason = "No buffer size makes this work."
        });

        line!.ShouldNotContain("says:");
    }

    [Test]
    public void Describe_AVersionMintedThatNothingCanDecide_CountsThemAndNamesThem()
    {
        // The count is the finding — a change of plan can leave the plan gated by less than it went
        // in with — and the words are what makes it actionable: a criterion nothing can decide is
        // usually one nobody has written a check for yet.
        var line = new PlanNarrator().Describe(new PlanVersionMinted
        {
            WorkflowName = "w",
            ExecutionId = "e",
            PlanId = "p",
            PlanVersion = 2,
            ReRuledNodeId = "delivery",
            Reason = "No buffer size makes this work.",
            CriteriaNothingCanDecide = ["The endpoint appears in the API reference.", "It reads well."]
        });

        line!.ShouldContain("criteria nothing can decide: 2");
        line!.ShouldContain("\"The endpoint appears in the API reference.\"");
        line!.ShouldContain("\"It reads well.\"");
    }

    [Test]
    public void Describe_AVersionMintedWithEveryCriterionDecidable_AddsNoLineForIt()
    {
        var line = new PlanNarrator().Describe(new PlanVersionMinted
        {
            WorkflowName = "w",
            ExecutionId = "e",
            PlanId = "p",
            PlanVersion = 2,
            ReRuledNodeId = "delivery",
            Reason = "No buffer size makes this work.",
            CriteriaNothingCanDecide = []
        });

        line!.ShouldNotContain("nothing can decide");
    }

    [Test]
    public void Describe_AVersionMintedWhereNobodyCouldSay_AddsNoLineForIt()
    {
        // A plan with no verifier says nothing about decidability, and a narrator that announced
        // "nothing can decide: 0" would turn "nobody was asked" into a clean bill of health.
        var line = new PlanNarrator().Describe(new PlanVersionMinted
        {
            WorkflowName = "w",
            ExecutionId = "e",
            PlanId = "p",
            PlanVersion = 2,
            ReRuledNodeId = "delivery",
            Reason = "No buffer size makes this work."
        });

        line!.ShouldNotContain("nothing can decide");
    }

    [Test]
    public void Describe_ADecision_SaysWhatItWasLookingAtAndWhatItChose()
    {
        var line = new PlanNarrator().Describe(new PlanDecisionTaken
        {
            WorkflowName = "w",
            ExecutionId = "e",
            PlanId = "p",
            PlanVersion = 1,
            NodeId = "write-index",
            Decision = PlanDecision.Replan(new AgentContract { Goal = "Index it differently" }),
            Changes = 1,
            MaxChanges = 3
        });

        line!.ShouldContain("'write-index' halted →");
        line!.ShouldContain("1 of 3 changes of plan used");
        line!.ShouldContain("→ change what");
    }

    [Test]
    public void Describe_AnEscalatedDecision_NamesWhoAnsweredRatherThanACountNobodyIsKeeping()
    {
        // The budget clause describes a bound only a round the run took by itself is under. An
        // escalated answer is reported and never counted, so a count printed here would narrate a cap
        // that is not in force — and a reader wants to know somebody answered far more than they want
        // two numbers that do not apply.
        var line = new PlanNarrator().Describe(new PlanDecisionTaken
        {
            WorkflowName = "w",
            ExecutionId = "e",
            PlanId = "p",
            PlanVersion = 1,
            NodeId = "deliver",
            Decision = PlanDecision.Replan(new AgentContract { Goal = "Deliver it over ftp" }),
            Escalated = true
        });

        line!.ShouldContain("answered by a person");
        line!.ShouldNotContain("changes of plan used");
        line!.ShouldContain("→ change what");
    }

    [Test]
    public void Describe_ADecisionThatChangesNothing_IsStillReported()
    {
        // The half that used to be invisible: an Ask mints no version, so it left a reader inferring
        // the tier's central judgement from what stopped happening.
        var asked = new PlanNarrator().Describe(new PlanDecisionTaken
        {
            WorkflowName = "w",
            ExecutionId = "e",
            PlanId = "p",
            PlanVersion = 1,
            NodeId = "list-changes",
            Decision = PlanDecision.Ask([new PlanOption { Summary = "keep going", Answer = "yes" }]),
            Changes = 0,
            MaxChanges = 3
        });

        asked!.ShouldContain("→ ask — 1 option(s) offered");

        var nothingToOffer = new PlanNarrator().Describe(new PlanDecisionTaken
        {
            WorkflowName = "w",
            ExecutionId = "e",
            PlanId = "p",
            PlanVersion = 1,
            NodeId = "list-changes",
            Decision = PlanDecision.Ask([]),
            Changes = 3,
            MaxChanges = 3
        });

        nothingToOffer!.ShouldContain("→ ask — nothing to offer");
    }

    [Test]
    public void Describe_WhatTheSupervisorOffered_NamesTheHaltTheVersionAndEachOption()
    {
        var line = new PlanNarrator().Describe(new PlanProposalOffered
        {
            WorkflowName = "w",
            ExecutionId = "e",
            PlanId = "p",
            PlanVersion = 2,
            NodeId = "hakone",
            HaltReason = "only 2027-04-07 is free",
            Options =
            [
                new PlanOption
                {
                    Summary = "Hakone has one free night",
                    Replan = true,
                    Recommended = true,
                    Rationale = new PlanRationale { By = "supervisor", Text = "only 2027-04-07 is free" }
                }
            ],
            Discarded = ["\"shift it\": only one replan is offered"]
        });

        line!.ShouldContain("supervisor asked about 'hakone' (version 2): only 2027-04-07 is free");
        line!.ShouldContain("refused: \"shift it\"");
        line!.ShouldContain("→ Hakone has one free night (only 2027-04-07 is free)");
    }

    [Test]
    public void Describe_WhatThePlannerWrote_ListsEachStepWithItsCriteria()
    {
        var line = new PlanNarrator().Describe(new PlanReauthored
        {
            WorkflowName = "w",
            ExecutionId = "e",
            PlanId = "p",
            PlanVersion = 2,
            Steps = ["hakone"],
            Authored =
            [
                new AuthoredStep
                {
                    Id = "hakone",
                    Contract = new AgentContract { Goal = "Book Hakone", AcceptanceCriteria = ["booked(hakone, 2027-04-07, 2027-04-08, 1)"] }
                }
            ]
        });

        line!.ShouldBe("   planner wrote: hakone booked(hakone, 2027-04-07, 2027-04-08, 1)");
    }

    [Test]
    public void Describe_ADecision_NamesThePlanVersionItWasTakenUnder()
    {
        var line = new PlanNarrator().Describe(new PlanDecisionTaken
        {
            WorkflowName = "w",
            ExecutionId = "e",
            PlanId = "p",
            PlanVersion = 3,
            NodeId = "hakone",
            Decision = PlanDecision.Replan(new PlanRationale { By = "supervisor", Text = "one night" }),
            Escalated = true
        });

        line!.ShouldEndWith("(version 3)");
    }

    [Test]
    public void Describe_AnEventThatIsNotAPlanEvent_SaysNothing() =>
        new PlanNarrator().Describe(new JobStarted<string> { WorkflowName = "w", ExecutionId = "e", JobName = "j" })
            .ShouldBeNull();

    [Test]
    public async Task Narrate_AStreamOfEvents_YieldsOnlyTheEventsWithSomethingToSay()
    {
        var lines = new List<string>();

        await foreach (var line in Stream().Narrate())
            lines.Add(line);

        lines.Count.ShouldBe(2, "the start is folded into the report, and the passing ruling says nothing");
        lines[0].ShouldContain("survey");
        lines[1].ShouldStartWith("     ── pass 1:");

        static async IAsyncEnumerable<WorkflowEvent> Stream()
        {
            await Task.Yield();
            yield return Started("survey");
            yield return Reported("survey", "done");
            yield return Ruled(VerificationOutcome.Passed);
            yield return Pass(["survey"], []);
        }
    }

    // ── Fixtures ──

    private static PlanNodeStarted Started(string nodeId) => new()
    {
        WorkflowName = "w",
        ExecutionId = "e",
        PlanId = "p",
        PlanVersion = 1,
        NodeId = nodeId,
        Goal = "do it",
        ProjectedTokens = 100
    };

    private static PlanNodeReported Reported(string nodeId, string summary) => new()
    {
        WorkflowName = "w",
        ExecutionId = "e",
        PlanId = "p",
        PlanVersion = 1,
        NodeId = nodeId,
        Summary = summary
    };

    private static PlanNodeVerified Ruled(VerificationOutcome outcome, string? abstained = null) => new()
    {
        WorkflowName = "w",
        ExecutionId = "e",
        PlanId = "p",
        PlanVersion = 1,
        NodeId = "n",
        Outcome = outcome,
        Abstained = abstained is null ? [] : [abstained]
    };

    private static PlanPassCompleted Pass(string[] executed, string[] skipped, string? haltedAt = null) => new()
    {
        WorkflowName = "w",
        ExecutionId = "e",
        PlanId = "p",
        PlanVersion = 1,
        Executed = executed,
        Skipped = skipped,
        HaltedAt = haltedAt,
        RootOutcome = ContractOutcome.Unmet
    };
}
