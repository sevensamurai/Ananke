using Ananke.Design;
using Shouldly;

namespace Ananke.Design.Tests;

/// <summary>
/// A plan written back out as data.
/// </summary>
/// <remarks>
/// <para>
/// <b>The round trip is by value.</b> What is asserted here is that a manifest written out and read
/// back is the same plan, never that it is the same text: comments and blank lines are stripped
/// before parsing and no writer can restore them. The strongest form of that available is a fixed
/// point — writing the re-parsed manifest produces the same text again — because it catches a
/// writer that drifts one key at a time and a hand-checked field list would not.
/// </para>
/// <para>
/// The rest of the file is the edges, each of which was found by reading the parser rather than by
/// a failure: what survives a round trip, what cannot, and what is refused at the point of writing
/// instead of producing a file that fails to parse later.
/// </para>
/// </remarks>
[TestFixture]
public class PlanManifestExporterTests
{
    /// <summary>
    /// Everything the format can say, and the values that are awkward to write: a goal holding a
    /// colon, one long enough to fold, and a literal block.
    /// </summary>
    private static readonly string[] Manifest =
        """
        # Stripped on the way in, and no writer can put it back.
        plan: offline-export

        root:
          goal: Ship offline export of reports.
          criteria:
            - An exported file is byte-identical to the online report.
          quality:
            - The export path reuses the existing report renderer.
          constraints:
            - No new external service dependencies.
          children:
            - id: discovery
              goal: Understand what exists: every render path, and which of them stream.
              criteria:
                - Every existing render path is accounted for.
              children:
                - id: survey-pipeline
                  goal: >
                    Map the current report pipeline end to end, naming every stage that holds a
                    whole report in memory and every one that does not.
            - id: delivery
              goal: |
                Build offline export on top of what discovery found.
                Stream, never buffer.

        revisions:
          - node: delivery
            reason: >
              The largest tenant's report is 4 GB. Assembling it in one buffer cannot meet the
              root's criterion at any buffer size, so buffering is not the work any more.
            goal: Build offline export by streaming.
            criteria:
              - A report larger than available memory exports successfully.
            children:
              - id: build-streaming-writer
                goal: Stream rendered pages straight to storage.
        """.Split('\n');

    private static PlanManifest Reparse(string yaml) => PlanManifest.Parse(yaml.Split('\n'));

    // ── The round trip ──

    [Test]
    public void ToYaml_WhatItWrites_IsAPlanTheParserReadsBack()
    {
        var written = PlanManifest.Parse(Manifest).ToYaml();

        // Reaching the assertions below at all is the first claim: an unknown key, a bad indent or
        // a value the format cannot hold would throw out of here instead.
        var read = Reparse(written);

        read.Plan.ShouldBe("offline-export");
        read.Root.Goal.ShouldBe("Ship offline export of reports.");
        read.Root.Children.Select(c => c.Id).ShouldBe(["discovery", "delivery"]);
        read.Root.Children[0].Children.Select(c => c.Id).ShouldBe(["survey-pipeline"]);
    }

    [Test]
    public void ToYaml_WrittenTwice_IsAFixedPoint()
    {
        var once = PlanManifest.Parse(Manifest).ToYaml();
        var twice = Reparse(once).ToYaml();

        // Not that the text matches the input — it cannot, the comment alone is gone — but that the
        // writer has stopped moving. A key dropped or re-shaped on the way out shows up here.
        twice.ShouldBe(once);
    }

    [Test]
    public void ToYaml_KeepsGateAndRankCriteriaApart()
    {
        var root = Reparse(PlanManifest.Parse(Manifest).ToYaml()).Root;

        root.Criteria.ShouldBe(["An exported file is byte-identical to the online report."]);
        root.Quality.ShouldBe(["The export path reuses the existing report renderer."]);
        root.Constraints.ShouldBe(["No new external service dependencies."]);
    }

    [Test]
    public void ToYaml_KeepsARevision_WithTheReasonItWasMade()
    {
        var revision = Reparse(PlanManifest.Parse(Manifest).ToYaml()).Revisions.ShouldHaveSingleItem();

        revision.Node.ShouldBe("delivery");
        revision.Goal.ShouldBe("Build offline export by streaming.");
        revision.Reason.ShouldStartWith("The largest tenant's report is 4 GB.");
        revision.Children.Select(c => c.Id).ShouldBe(["build-streaming-writer"]);
    }

    // ── What survives, and what cannot ──

    [Test]
    public void ToYaml_AGoalHoldingAColon_Survives()
    {
        // The parser splits on the FIRST colon and keeps the rest of the line, so this needs no
        // quoting — which is just as well, because the format has none.
        Reparse(PlanManifest.Parse(Manifest).ToYaml()).Root.Children[0].Goal
            .ShouldBe("Understand what exists: every render path, and which of them stream.");
    }

    [Test]
    public void ToYaml_ALiteralBlock_KeepsItsLineBreaks()
    {
        Reparse(PlanManifest.Parse(Manifest).ToYaml()).Root.Children[1].Goal
            .ShouldBe("Build offline export on top of what discovery found.\nStream, never buffer.");
    }

    [Test]
    public void ToYaml_ALongGoal_IsFoldedAndReadsBackAsOneLine()
    {
        var written = PlanManifest.Parse(Manifest).ToYaml();

        written.ShouldContain("goal: >");

        Reparse(written).Root.Children[0].Children[0].Goal.ShouldBe(
            "Map the current report pipeline end to end, naming every stage that holds a whole "
            + "report in memory and every one that does not.");
    }

    [Test]
    public void ToYaml_AGoalHoldingABlankLine_IsRefusedRatherThanWrittenWrong()
    {
        // Read() drops blank lines before parsing, so this value would come back a different
        // string. Refused where the node is still nameable, not discovered on the way back in.
        var manifest = new PlanManifest
        {
            Plan = "p",
            Root = new PlanNodeManifest { Goal = "One.\n\nTwo." }
        };

        Should.Throw<InvalidOperationException>(() => manifest.ToYaml())
            .Message.ShouldContain("blank line");
    }

    [Test]
    public void ToYaml_AChildWithNoId_IsRefused()
    {
        var manifest = new PlanManifest
        {
            Plan = "p",
            Root = new PlanNodeManifest
            {
                Goal = "Root.",
                Children = [new PlanNodeManifest { Goal = "A step nothing can refer to." }]
            }
        };

        Should.Throw<InvalidOperationException>(() => manifest.ToYaml())
            .Message.ShouldContain("no id");
    }

    [Test]
    public void ToYaml_ANodeWithNoGoal_IsRefused()
    {
        var manifest = new PlanManifest { Plan = "p", Root = new PlanNodeManifest { Goal = "  " } };

        Should.Throw<InvalidOperationException>(() => manifest.ToYaml())
            .Message.ShouldContain("no goal");
    }

    [Test]
    public void ToYaml_ACriterionSpanningLines_IsRefused()
    {
        // A sequence item has no block form: ParseScalars reads to the end of the line and stops.
        var manifest = new PlanManifest
        {
            Plan = "p",
            Root = new PlanNodeManifest { Goal = "Root.", Criteria = ["One.\nTwo."] }
        };

        Should.Throw<InvalidOperationException>(() => manifest.ToYaml());
    }

    [Test]
    public void ToYaml_APlanWithNothingButARoot_OmitsTheKeysItHasNothingFor()
    {
        var written = new PlanManifest
        {
            Plan = "bare",
            Root = new PlanNodeManifest { Goal = "Do the thing." }
        }.ToYaml();

        // An empty sequence parses, but no plan written by hand carries one and a reader should not
        // have to wonder whether it meant something.
        written.ShouldNotContain("criteria:");
        written.ShouldNotContain("children:");
        Reparse(written).Root.Goal.ShouldBe("Do the thing.");
    }

    // ── What the parser drops without saying so ──

    [Test]
    public void ToYaml_AWrappedLineThatWouldStartWithAHash_IsNotReadBackAsAComment()
    {
        // The parser drops any line whose first character is '#' before it reads a thing, so a folded
        // goal whose wrap lands on a word like "#tag" loses that whole line with no error at all.
        // Seventeen four-letter words fill a folded line exactly, which puts "#tag" at the start of
        // the next.
        var goal = string.Join(' ', Enumerable.Repeat("word", 17)) + " #tag and more words after it";

        var manifest = new PlanManifest { Plan = "p", Root = new PlanNodeManifest { Goal = goal } };

        Reparse(manifest.ToYaml()).Root.Goal.ShouldBe(goal);
    }

    [Test]
    public void ToYaml_ALongGoalThatStartsWithAHash_ReadsBackWhole()
    {
        // Folded, its first line would begin with '#' and be dropped like any other comment.
        var goal = "#1 priority: " + string.Join(' ', Enumerable.Repeat("word", 25));

        var manifest = new PlanManifest { Plan = "p", Root = new PlanNodeManifest { Goal = goal } };

        Reparse(manifest.ToYaml()).Root.Goal.ShouldBe(goal);
    }

    [Test]
    public void ToYaml_ALiteralBlockWithALineStartingWithAHash_IsRefusedRatherThanWrittenWrong()
    {
        // A literal block keeps its line breaks, so there is no wrapping to move the '#' away from
        // the start of its line. Refused where the node is still nameable.
        var manifest = new PlanManifest
        {
            Plan = "p",
            Root = new PlanNodeManifest { Goal = "First line.\n#Second line." }
        };

        Should.Throw<InvalidOperationException>(() => manifest.ToYaml())
            .Message.ShouldContain("'#'");
    }

    [Test]
    public void ToYaml_ABlankCriterion_IsRefused()
    {
        // Written as "- ", which the parser trims to "-" and no longer reads as a sequence item.
        var manifest = new PlanManifest
        {
            Plan = "p",
            Root = new PlanNodeManifest { Goal = "Root.", Criteria = ["Holds.", "   "] }
        };

        Should.Throw<InvalidOperationException>(() => manifest.ToYaml())
            .Message.ShouldContain("blank");
    }

    [Test]
    public void ToYaml_APlanNameSpanningLines_IsRefused()
    {
        var manifest = new PlanManifest
        {
            Plan = "one\ntwo",
            Root = new PlanNodeManifest { Goal = "Root." }
        };

        Should.Throw<InvalidOperationException>(() => manifest.ToYaml())
            .Message.ShouldContain("plan");
    }
}
