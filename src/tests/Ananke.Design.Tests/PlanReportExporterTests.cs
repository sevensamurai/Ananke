using Ananke.Design;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Design.Tests;

/// <summary>
/// A plan and its lineage, rendered for a person.
/// </summary>
/// <remarks>
/// <para>
/// The reader this serves is not the model. <c>PlanTreeProjection</c> renders the same tree under a
/// token budget and omits what will not fit; a person asking "what happened to this plan" needs the
/// opposite — nothing elided, and provenance for everything that was decided. Most of what is
/// asserted here is that the awkward parts survive the render: the criterion nothing checked, the
/// verdict that only holds after earlier ones did not, the work a change of plan dropped.
/// </para>
/// <para>
/// The lineage is where a status flag would have lost the argument. "Cancelled" says something
/// stopped; this says which version stopped it, why, and where the dropped work is still readable.
/// </para>
/// </remarks>
[TestFixture]
public class PlanReportExporterTests
{
    private const string Gate = "Export completes for the largest tenant.";
    private const string Rank = "The export path reuses the existing report renderer.";
    private const string Unchecked = "The endpoint appears in the API reference.";

    // ── The tree as it stands ──

    [Test]
    public void ToOutline_APlan_NamesTheVersionInForceAndTheRootOutcome()
    {
        var outline = Plan().ToOutline();

        outline.ShouldStartWith("Plan offline-export — version 1 of 1, root Unmet");
    }

    [Test]
    public void ToOutline_ADecomposition_IndentsChildrenUnderTheirParent()
    {
        var outline = Plan().ToOutline();

        NodeLine(outline, "offline-export").ShouldStartWith("offline-export");
        NodeLine(outline, "delivery").ShouldStartWith("  delivery");
        NodeLine(outline, "build-endpoint").ShouldStartWith("    build-endpoint");
    }

    [Test]
    public void ToOutline_ACriterionNothingDecided_SaysUnchecked()
    {
        // The gap between what was verified and what merely looks fine is the thing abstention
        // exists to keep visible. A report that left the line out would undo it.
        var outline = Plan().ToOutline();

        Line(outline, Unchecked).ShouldContain("unchecked");
    }

    [Test]
    public void ToOutline_AVerdict_NamesTheOracleThatDecidedIt()
    {
        var outline = Plan().WithVerdict("build-endpoint", Verdict(Unchecked, passed: true)).ToOutline();

        Line(outline, Unchecked).ShouldContain("met");
        Line(outline, Unchecked).ShouldContain("(dotnet test)");
    }

    [Test]
    public void ToOutline_ACriterionThatFailedAndThenPassed_SaysHowManyAttemptsDidNot()
    {
        // A criterion that failed, was fixed and now passes reads very differently from one that
        // simply passed, and the difference is the evidence that the fix worked.
        var tree = Plan()
            .WithVerdict("build-endpoint", Verdict(Unchecked, passed: false))
            .WithVerdict("build-endpoint", Verdict(Unchecked, passed: true));

        Line(tree.ToOutline(), Unchecked).ShouldContain("after 1 attempt(s) that did not");
    }

    [Test]
    public void ToOutline_AQualityCriterion_IsMarkedAsRankingRatherThanGating()
    {
        // Gates and ranks are kept apart everywhere else precisely so they are never averaged; a
        // render that showed them alike would put that back.
        var outline = Plan().WithVerdict("offline-export", Verdict(Rank, passed: true)).ToOutline();

        Line(outline, Rank).ShouldContain("rank");
        Line(outline, Gate).ShouldNotContain("rank");
    }

    [Test]
    public void ToOutline_ADisputedNode_ShowsTheCriterionAndWhatContradictedIt()
    {
        var tree = Plan().WithViolation("build-endpoint", new PlanViolation
        {
            Criterion = Unchecked,
            Reason = "There is no API reference to add it to.",
            At = DateTimeOffset.UnixEpoch
        });

        var outline = tree.ToOutline();

        Line(outline, "disputed").ShouldContain(Unchecked);
        Line(outline, "disputed").ShouldContain("There is no API reference to add it to.");
        NodeLine(outline, "build-endpoint").ShouldContain("Disputed");
    }

    [Test]
    public void ToOutline_ANodeThatLastRanUnderAnEarlierVersion_IsMarkedStale()
    {
        var tree = Reruled(Plan().WithReading("delivery", Read(version: 1)));

        NodeLine(tree.ToOutline(), "delivery").ShouldContain("stale: last ran against version 1");
    }

    [Test]
    public void ToOutline_ANodeThatNeverRan_SaysSoRatherThanLookingSettled()
    {
        // In the lifecycle rather than in a flag beside it — one fact, written once.
        NodeLine(Plan().ToOutline(), "build-endpoint").ShouldContain("Planned");
    }

    [Test]
    public void ToOutline_AStepDoneWithAResult_ShowsItsStateAndTheResult()
    {
        var outline = Plan().WithState("build-endpoint", StepState.Done, "onsen-ryokan").ToOutline();

        NodeLine(outline, "build-endpoint").ShouldContain("— Done: onsen-ryokan");
    }

    [Test]
    public void ToOutline_APendingStep_ShowsNoStateText()
    {
        NodeLine(Plan().ToOutline(), "build-endpoint").ShouldNotContain("Pending");
    }

    // ── The lineage ──

    [Test]
    public void ToLineage_TheFirstVersion_IsThePlanAsWritten()
    {
        var lineage = Plan().ToLineage();

        lineage.ShouldStartWith("Lineage of offline-export — 1 version(s)");
        lineage.ShouldContain("the plan as first written — 4 node(s)");
    }

    [Test]
    public void ToLineage_AReruling_RecordsWhichNodeAndWhy()
    {
        var lineage = Reruled(Plan()).ToLineage();

        Line(lineage, "version 2").ShouldContain("re-ruling delivery");
        lineage.ShouldContain("reason: The largest tenant's report is 4 GB.");
    }

    [Test]
    public void ToLineage_ARerulingWithARationale_KeepsItApartFromTheReasonAndAttributesIt()
    {
        // A document that reads like a record while being partly a guess is worse than one that
        // says less. The observed reason and somebody's conclusion from it get separate lines, and
        // the conclusion is signed.
        var lineage = Reruled(Plan()).ToLineage();

        lineage.ShouldContain("reason: The largest tenant's report is 4 GB.");
        lineage.ShouldContain("planner says: Streaming keeps peak memory to one page.");
    }

    [Test]
    public void ToLineage_ARerulingThatChangedAGoal_ShowsItBeforeAndAfter()
    {
        var lineage = Reruled(Plan()).ToLineage();

        lineage.ShouldContain("contract changed: delivery");
        lineage.ShouldContain("goal was: Build offline export on top of what discovery found.");
        lineage.ShouldContain("goal now: Build offline export by streaming.");
    }

    [Test]
    public void ToLineage_ARerulingThatChangedCriteria_NamesWhatWentAndWhatArrived()
    {
        var lineage = Reruled(Plan()).ToLineage();

        lineage.ShouldContain("criterion dropped: A report exports end to end from the API.");
        lineage.ShouldContain("criterion added:   A report exports end to end at any tenant size.");
    }

    [Test]
    public void ToLineage_WorkTheRerulingDropped_IsNamedAndStillReadableWhereItWas()
    {
        // The whole argument for a lineage over a "cancelled" flag: what it was, what it was for,
        // and which change of plan made it unnecessary.
        var lineage = Reruled(Plan()).ToLineage();

        lineage.ShouldContain("dropped: build-buffer");
        lineage.ShouldContain("Assemble the whole report in memory before writing it.");
        lineage.ShouldContain("not cancelled: still readable in version 1");
    }

    [Test]
    public void ToLineage_WorkTheRerulingAdded_IsNamedWithItsGoal()
    {
        var lineage = Reruled(Plan()).ToLineage();

        lineage.ShouldContain("added:   build-streaming-writer — Stream rendered pages to storage.");
    }

    [Test]
    public void ToLineage_NodesOutsideTheReruledSubtree_AreReportedAsCarriedOver()
    {
        // Unchanged subtrees are shared rather than reissued, and the count is how a reader sees
        // that a re-ruling was local rather than a rewrite.
        Reruled(Plan()).ToLineage().ShouldContain("carried over unchanged: 2 node(s)");
    }

    [Test]
    public void ToReport_APlan_IsTheOutlineFollowedByTheLineage()
    {
        var tree = Reruled(Plan());

        tree.ToReport().ShouldBe($"{tree.ToOutline()}\n\n{tree.ToLineage()}");
    }

    // ── Helpers ──

    private static PlanTree Plan() => PlanTree.Create(
        "offline-export",
        new AgentContract
        {
            Goal = "Ship offline export of reports.",
            AcceptanceCriteria = [Gate],
            QualityCriteria = [Rank],
            Constraints = ["No new external service dependencies."]
        },
        [
            new PlanTree.PlanNodeSpec(
                "delivery",
                new AgentContract
                {
                    Goal = "Build offline export on top of what discovery found.",
                    AcceptanceCriteria = ["A report exports end to end from the API."]
                },
                [
                    new PlanTree.PlanNodeSpec("build-buffer", new AgentContract
                    {
                        Goal = "Assemble the whole report in memory before writing it."
                    }),
                    new PlanTree.PlanNodeSpec("build-endpoint", new AgentContract
                    {
                        Goal = "Expose the export endpoint.",
                        AcceptanceCriteria = [Unchecked]
                    })
                ])
        ]);

    private static PlanTree Reruled(PlanTree tree) => tree.Rerule(
        "delivery",
        new AgentContract
        {
            Goal = "Build offline export by streaming.",
            AcceptanceCriteria = ["A report exports end to end at any tenant size."]
        },
        "The largest tenant's report is 4 GB. No buffer size makes buffering work.",
        [
            ("build-streaming-writer", new AgentContract { Goal = "Stream rendered pages to storage." }),
            ("build-endpoint", new AgentContract
            {
                Goal = "Expose the export endpoint.",
                AcceptanceCriteria = [Unchecked]
            })
        ],
        rationale: new PlanRationale
        {
            By = "planner",
            Text = "Streaming keeps peak memory to one page."
        });

    private static CriterionVerdict Verdict(string criterion, bool passed) => new()
    {
        Criterion = criterion,
        Passed = passed,
        Oracle = "dotnet test",
        At = DateTimeOffset.UnixEpoch
    };

    private static NodeReading Read(int version) => new()
    {
        At = DateTimeOffset.UnixEpoch,
        PlanVersion = version,
        ProjectedTokens = 100
    };

    /// <summary>The line a node is rendered on, found by its own indented heading.</summary>
    private static string NodeLine(string report, string nodeId) =>
        report.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith($"{nodeId} — ", StringComparison.Ordinal))
        ?? throw new AssertionException($"No line for node '{nodeId}' in:\n{report}");

    private static string Line(string report, string containing) =>
        report.Split('\n').FirstOrDefault(l => l.Contains(containing, StringComparison.Ordinal))
        ?? throw new AssertionException($"No line containing '{containing}' in:\n{report}");
}
