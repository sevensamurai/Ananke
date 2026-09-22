using Ananke.Design;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Design.Tests;

/// <summary>
/// A plan written down as data.
/// </summary>
/// <remarks>
/// <para>
/// Two things are being tested, and only one of them is parsing. The first is that the format can
/// say what a plan actually needs to say — a decomposition of any depth, contracts with the gate and
/// rank criteria kept apart, and <b>a re-ruling</b>, without which the format describes only the
/// plan's first version.
/// </para>
/// <para>
/// The second is that it says nothing else. A plan format that acquires conditions or expressions
/// has become a program that computes a plan, and the reason for writing one down was to have
/// something a person can read. The test for that is unglamorous: an unknown key is an error.
/// </para>
/// </remarks>
[TestFixture]
public class PlanManifestTests
{
    [Test]
    public async Task ApplyToAsync_ADeclaredRevision_ReRulesThePlanBeingSupervised()
    {
        // The declared change of plan, applied to the store a supervised job reads — without the
        // job that decides it having to assemble an executor of its own.
        var manifest = PlanManifest.Parse(Manifest);
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(manifest.ToTree());

        var supervision = new SupervisionOptions
        {
            Runner = (_, _) => Task.FromResult(NodeOutcome.Nothing),
            Store = store
        };

        var reruled = await manifest.Revisions[0].ApplyToAsync(supervision, manifest.Plan);

        reruled.Lineage.Count.ShouldBe(2);
        reruled.Current.ReRuledNodeId.ShouldBe("delivery");
        (await store.LoadAsync(manifest.Plan))!.Current.Number.ShouldBe(2);
    }

    private static readonly string[] Manifest =
        """
        # A feature, and the discovery that changed it.
        plan: offline-export

        root:
          goal: Ship offline export of reports.
          criteria:
            - An exported file is byte-identical to the online report.
            - Export completes for the largest tenant.
          quality:
            - The export path reuses the existing report renderer.
          constraints:
            - No new external service dependencies.
          children:
            - id: discovery
              goal: Understand what already exists before designing anything.
              criteria:
                - Every existing render path is accounted for.
              children:
                - id: survey-pipeline
                  goal: Map the current report pipeline end to end.
            - id: delivery
              goal: Build offline export on top of what discovery found.
              children:
                - id: build-buffer
                  goal: Assemble the whole report in memory before writing it.

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

    // ── What the format can say ──

    [Test]
    public void Parse_ReadsADecompositionOfAnyDepth()
    {
        var manifest = PlanManifest.Parse(Manifest);

        manifest.Plan.ShouldBe("offline-export");
        manifest.Root.Goal.ShouldBe("Ship offline export of reports.");
        manifest.Root.Children.Select(c => c.Id).ShouldBe(["discovery", "delivery"]);
        manifest.Root.Children[0].Children.Select(c => c.Id).ShouldBe(["survey-pipeline"]);
    }

    [Test]
    public void Parse_KeepsGateAndRankCriteriaApart()
    {
        var root = PlanManifest.Parse(Manifest).Root;

        // Two lists, never one. A failed acceptance criterion is not purchasable with a strong
        // showing on quality, and a format that put them in one list would have said it was.
        root.Criteria.Count.ShouldBe(2);
        root.Quality.ShouldBe(["The export path reuses the existing report renderer."]);
        root.Constraints.ShouldBe(["No new external service dependencies."]);
    }

    [Test]
    public void Parse_ReadsARevision_WithTheReasonItWasMade()
    {
        var revision = PlanManifest.Parse(Manifest).Revisions.ShouldHaveSingleItem();

        revision.Node.ShouldBe("delivery");
        revision.Goal.ShouldBe("Build offline export by streaming.");
        revision.Reason.ShouldStartWith("The largest tenant's report is 4 GB.");
        revision.Reason.ShouldNotContain("\n", Case.Sensitive);
        revision.Children.Select(c => c.Id).ShouldBe(["build-streaming-writer"]);
    }

    // ── What it builds ──

    [Test]
    public void ToTree_BuildsVersionOne_WhateverTheDepth()
    {
        var tree = PlanManifest.Parse(Manifest).ToTree();

        // Describing a plan is not changing one: a manifest of any depth is one version, not one
        // version per level.
        tree.Lineage.Count.ShouldBe(1);
        tree.Current.Nodes.Keys.OrderBy(k => k, StringComparer.Ordinal).ShouldBe(
            ["build-buffer", "delivery", "discovery", "offline-export", "survey-pipeline"]);
        tree.Node("offline-export").ChildIds.ShouldBe(["discovery", "delivery"]);
    }

    [Test]
    public void ToCurrentTree_AppliesEveryRevision_AndKeepsWhatWasDropped()
    {
        var tree = PlanManifest.Parse(Manifest).ToCurrentTree();

        tree.Lineage.Count.ShouldBe(2);
        tree.Current.Reason.ShouldStartWith("The largest tenant's report is 4 GB.");

        // Dropped, not cancelled: gone from this version, still readable in the one before it.
        tree.Current.Nodes.ShouldNotContainKey("build-buffer");
        tree.Current.DroppedNodeIds.ShouldBe(["build-buffer"]);
        tree.Lineage[0].Nodes.ShouldContainKey("build-buffer");
        tree.Lineage[0].Nodes["build-buffer"].Contract.Goal
            .ShouldBe("Assemble the whole report in memory before writing it.");
    }

    [Test]
    public void ApplyTo_IsTheSameReRulingTheExecutorWouldMake()
    {
        var manifest = PlanManifest.Parse(Manifest);

        var applied = manifest.Revisions[0].ApplyTo(manifest.ToTree());

        applied.Node("delivery").Contract.Goal.ShouldBe("Build offline export by streaming.");
        applied.Current.ReRuledNodeId.ShouldBe("delivery");
    }

    // ── What it refuses to say ──

    [Test]
    public void Parse_AnUnknownKey_IsAnErrorRatherThanASilence()
    {
        // A misspelled 'critera:' that parsed to nothing would produce a node with no acceptance
        // criteria — indistinguishable from one that legitimately has none, and discovered when the
        // plan passed without checking anything.
        var error = Should.Throw<InvalidOperationException>(() => PlanManifest.Parse(
            """
            plan: p
            root:
              goal: Ship it.
              critera:
                - typo
            """.Split('\n')));

        error.Message.ShouldContain("critera");
    }

    [Test]
    public void Parse_ARevisionWithNoReason_IsRefused()
    {
        var error = Should.Throw<InvalidOperationException>(() => PlanManifest.Parse(
            """
            plan: p
            root:
              goal: Ship it.
            revisions:
              - node: p
                goal: Ship it differently.
            """.Split('\n')));

        error.Message.ShouldContain("reason");
    }

    [Test]
    public void Parse_ANodeWithNoGoal_IsRefused() =>
        Should.Throw<InvalidOperationException>(() => PlanManifest.Parse(
            """
            plan: p
            root:
              criteria:
                - something
            """.Split('\n'))).Message.ShouldContain("goal");

    [Test]
    public void Parse_WithNoPlanIdentifier_IsRefused() =>
        Should.Throw<InvalidOperationException>(() => PlanManifest.Parse(
            """
            root:
              goal: Ship it.
            """.Split('\n')));

    [Test]
    public void Parse_ALiteralBlockScalar_KeepsItsLineBreaks()
    {
        var manifest = PlanManifest.Parse(
            """
            plan: p
            root:
              goal: |
                first
                second
            """.Split('\n'));

        manifest.Root.Goal.ShouldBe("first\nsecond");
    }

    [Test]
    public void Load_ReadsAManifestFromDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".yml");
        File.WriteAllLines(path, Manifest);

        try
        {
            PlanManifest.Load(path).Plan.ShouldBe("offline-export");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
