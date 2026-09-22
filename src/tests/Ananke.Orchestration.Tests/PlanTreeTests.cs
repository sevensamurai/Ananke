using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// The durable tree: what it records, what it derives, and what mints a new version of it.
/// </summary>
/// <remarks>
/// The design turns on two refusals. A node carries <b>no status field</b>, because marking
/// something complete or cancelled records the outcome and destroys the reason. And a verdict
/// <b>does not mint a plan version</b>, because a version marks the plan changing — if every fact
/// about the work minted one, the lineage would be churn instead of a history of decisions.
/// </remarks>
[TestFixture]
public class PlanTreeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 24, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public void Create_LinksChildrenToTheRootInOrder()
    {
        var tree = Tree();

        tree.Root.ChildIds.ShouldBe(["parse", "render"]);
        tree.Node("parse").ParentId.ShouldBe("plan");
        tree.Current.Number.ShouldBe(1);
        tree.Lineage.Count.ShouldBe(1);
    }

    [Test]
    public void Create_DuplicateChildIds_AreRejected()
    {
        Should.Throw<ArgumentException>(() => PlanTree.Create(
            "plan", Contract("Ship it"),
            [("a", Contract("x")), ("a", Contract("y"))]));
    }

    // ── Status is derived, never stored ──

    [Test]
    public void StatusOf_ANodeWithNoVerdicts_IsPlannedAndUnmet()
    {
        // Two facts, and the enum these replace could only ever say one of them.
        Tree().LifecycleOf("parse").ShouldBe(NodeLifecycle.Planned);
        Tree().OutcomeOf("parse").ShouldBe(ContractOutcome.Unmet);
    }

    [Test]
    public void OutcomeOf_EveryCriterionMet_IsMet()
    {
        var tree = Tree().WithVerdict("parse", Passed("the parser round-trips"));

        tree.OutcomeOf("parse").ShouldBe(ContractOutcome.Met);
    }

    [Test]
    public void OutcomeOf_OneCriterionOfTwoMet_IsUnmet()
    {
        var tree = PlanTree.Create("plan", Contract("Ship it"),
            [("parse", new AgentContract
            {
                Goal = "Parse",
                AcceptanceCriteria = ["a", "b"]
            })]).WithVerdict("parse", Passed("a"));

        tree.OutcomeOf("parse").ShouldBe(ContractOutcome.Unmet);
    }

    [Test]
    public void OutcomeOf_AFailingVerdict_IsUnmet()
    {
        var tree = Tree().WithVerdict("parse", Failed("the parser round-trips"));

        tree.OutcomeOf("parse").ShouldBe(ContractOutcome.Unmet);
    }

    [Test]
    public void OutcomeOf_ACriterionThatFailedAndWasThenFixed_IsPassing()
    {
        // Verdicts accumulate rather than overwrite, because that history is the evidence a fix
        // worked. Reading *any* failing verdict as failure would leave a node permanently failed by
        // a result that has since been superseded — the same mistake as discarding an old record
        // instead of re-running the check that produced it.
        var tree = Tree()
            .WithVerdict("parse", Failed("the parser round-trips"))
            .WithVerdict("parse", Passed("the parser round-trips"));

        tree.OutcomeOf("parse").ShouldBe(ContractOutcome.Met);
        tree.Node("parse").Verdicts.Count.ShouldBe(2);   // both are still on the record
    }

    [Test]
    public void OutcomeOf_ACriterionThatPassedAndThenRegressed_IsUnmet()
    {
        var tree = Tree()
            .WithVerdict("parse", Passed("the parser round-trips"))
            .WithVerdict("parse", Failed("the parser round-trips"));

        tree.OutcomeOf("parse").ShouldBe(ContractOutcome.Unmet);
    }

    [Test]
    public void OutcomeOf_TheRoot_WaitsOnItsChildren()
    {
        // A parent cannot be satisfied by its own check alone: verification cannot happen below the
        // altitude that decided, and the children are below it.
        var tree = Tree().WithVerdict("plan", Passed("the build is green"));

        tree.OutcomeOf("plan").ShouldNotBe(ContractOutcome.Met);

        var complete = tree
            .WithVerdict("parse", Passed("the parser round-trips"))
            .WithVerdict("render", Passed("output matches the golden file"));

        complete.OutcomeOf("plan").ShouldBe(ContractOutcome.Met);
    }

    [Test]
    public void OutcomeOf_AChildThatFailed_LeavesTheRootUnmet()
    {
        var tree = Tree()
            .WithVerdict("plan", Passed("the build is green"))
            .WithVerdict("parse", Failed("the parser round-trips"));

        tree.OutcomeOf("plan").ShouldBe(ContractOutcome.Unmet);
    }

    [Test]
    public void OutcomeOf_AReportedContradiction_IsItsOwnOutcome()
    {
        // Distinct from Failed on purpose: "this did not work" and "what I was asked for is wrong"
        // travel to different places and need different answers.
        var tree = Tree().WithViolation("parse", Dispute());

        tree.OutcomeOf("parse").ShouldBe(ContractOutcome.Disputed);
        tree.OutcomeOf("plan").ShouldBe(ContractOutcome.Disputed);
    }

    // ── Versions are minted on re-ruling, and only on re-ruling ──

    [Test]
    public void WithVerdict_DoesNotMintAVersion()
    {
        var tree = Tree();
        for (var i = 0; i < 10; i++)
            tree = tree.WithVerdict("parse", Passed($"criterion {i}"));

        tree.Lineage.Count.ShouldBe(1);
    }

    [Test]
    public void WithViolation_DoesNotMintAVersion()
    {
        // Reporting that a contract is wrong is not the same as changing it. Whether the plan
        // adapts belongs to whoever authored the criterion.
        Tree().WithViolation("parse", Dispute()).Lineage.Count.ShouldBe(1);
    }

    [Test]
    public void Rerule_MintsAVersionAndKeepsTheReason()
    {
        var tree = Tree().Rerule(
            "parse",
            Contract("Parse, streaming", "the parser handles files larger than memory"),
            "the input turned out to be 40 GB, so a whole-file parser cannot meet the original criterion");

        tree.Lineage.Count.ShouldBe(2);
        tree.Current.Number.ShouldBe(2);
        tree.Current.ReRuledNodeId.ShouldBe("parse");
        tree.Current.Reason.ShouldNotBeNull();
        tree.Current.Reason.ShouldContain("40 GB");

        // The reason a status flag would have destroyed is still readable.
        tree.Lineage[0].Nodes["parse"].Contract.Goal.ShouldBe("Parse");
        tree.Current.Nodes["parse"].Contract.Goal.ShouldBe("Parse, streaming");
    }

    [Test]
    public void Rerule_ClearsVerdictsInTheSubtreeItInvalidates_AndNowhereElse()
    {
        var tree = Tree()
            .WithVerdict("parse", Passed("the parser round-trips"))
            .WithVerdict("render", Passed("output matches the golden file"))
            .Rerule("parse", Contract("Parse, streaming"), "the input no longer fits in memory");

        // Judged against a criterion that no longer applies.
        tree.Node("parse").Verdicts.ShouldBeEmpty();

        // Outside the re-ruled subtree, nothing moved.
        tree.Node("render").Verdicts.Count.ShouldBe(1);
    }

    [Test]
    public void Rerule_ANodeWhoseOwnContractDidNotChange_KeepsItsVerdicts()
    {
        // A child that was not re-issued anything different has a verdict that is still a fact:
        // criteria that still stand, decided by an oracle that still would. What a re-ruling above
        // it changes is whether its work is still *wanted*, and that is said by whether it is still
        // in the tree — not by destroying the record that it was done.
        var tree = Tree()
            .WithVerdict("parse", Passed("the parser round-trips"))
            .WithVerdict("render", Passed("output matches the golden file"))
            .Rerule("plan", Contract("Ship something else"), "the goal changed");

        tree.Node("parse").Verdicts.Count.ShouldBe(1);
        tree.Node("render").Verdicts.Count.ShouldBe(1);
        tree.Node("plan").Verdicts.ShouldBeEmpty();     // its own contract did change
    }

    [Test]
    public void Rerule_ANodeWhoseOwnContractDidNotChange_KeepsItsAttempts()
    {
        // The same line the verdicts follow: a node that was not re-issued anything different has
        // spent those attempts on work that still stands, and giving them back would let a re-ruling
        // anywhere in the plan quietly reset a bound that is doing its job.
        var tree = Tree()
            .WithReading("parse", Reading())
            .Rerule("plan", Contract("Ship something else"), "the goal changed");

        tree.Node("parse").ReadCount.ShouldBe(1);
    }

    [Test]
    public void Rerule_TheAttemptsSpentOnTheOldContract_StayReadableInTheVersionThatHadThem()
    {
        // Nothing is lost, in the same way a dropped node is not lost: the count belongs to the
        // version whose contract it was spent on, and that version is still there.
        var tree = Tree()
            .WithReading("parse", Reading())
            .WithReading("parse", Reading())
            .Rerule("parse", Contract("Parse, streaming"), "the input no longer fits in memory");

        tree.Lineage[0].Nodes["parse"].ReadCount.ShouldBe(2);
        tree.Current.Nodes["parse"].ReadCount.ShouldBe(0);
    }

    // ── A redesign may change the decomposition, not only the wording ──

    [Test]
    public void Rerule_WithReplacementChildren_DropsWhatIsNoLongerNeededAndAddsWhatIs()
    {
        var tree = Tree()
            .WithVerdict("parse", Passed("the parser round-trips"))
            .Rerule(
                "plan",
                Contract("Ship it, streaming", "the build is green"),
                "the input no longer fits in memory, so buffering is not the work any more",
                children:
                [
                    ("parse", Contract("Parse", "the parser round-trips")),   // unchanged, kept
                    ("stream", Contract("Stream", "a file larger than memory round-trips"))
                ]);

        tree.Root.ChildIds.ShouldBe(["parse", "stream"]);
        tree.Current.DroppedNodeIds.ShouldBe(["render"]);
        tree.Current.Nodes.ShouldNotContainKey("render");

        // Kept work survives the redesign; new work starts pending.
        tree.Node("parse").Verdicts.Count.ShouldBe(1);
        tree.LifecycleOf("stream").ShouldBe(NodeLifecycle.Planned);
    }

    [Test]
    public void Rerule_ADroppedNode_StaysReadableInTheVersionThatHadIt()
    {
        // The whole reason for a lineage. "Cancelled" records that something stopped; this records
        // what it was, what it was for, and which change of plan made it unnecessary.
        var tree = Tree().Rerule(
            "plan", Contract("Ship it"), "streaming removed the need to buffer",
            children: [("parse", Contract("Parse", "the parser round-trips"))]);

        tree.Current.Nodes.ShouldNotContainKey("render");

        var before = tree.Lineage[0];
        before.Nodes["render"].Contract.Goal.ShouldBe("Render");
        tree.Current.Reason.ShouldNotBeNull();
        tree.Current.Reason.ShouldContain("streaming removed the need");
    }

    [Test]
    public void Rerule_ReplacingAChildsContract_ClearsThatChildsVerdictsOnly()
    {
        var tree = Tree()
            .WithVerdict("parse", Passed("the parser round-trips"))
            .WithVerdict("render", Passed("output matches the golden file"))
            .Rerule(
                "plan", Contract("Ship it", "the build is green"), "the parser criterion was wrong",
                children:
                [
                    ("parse", Contract("Parse", "the parser handles files larger than memory")),
                    ("render", Contract("Render", "output matches the golden file"))
                ]);

        tree.Node("parse").Verdicts.ShouldBeEmpty();      // re-issued differently
        tree.Node("render").Verdicts.Count.ShouldBe(1);   // untouched
    }

    [Test]
    public void Rerule_SharesUnchangedSubtreesByReference()
    {
        // "Unchanged subtrees are shared, not copied" — asserted rather than assumed, because it is
        // what keeps a long lineage affordable.
        var tree = Tree().Rerule("parse", Contract("Parse, streaming"), "input grew");

        ReferenceEquals(tree.Lineage[0].Nodes["render"], tree.Current.Nodes["render"]).ShouldBeTrue();
    }

    // ── A re-ruled node's own contract carries no claim about the old one ──

    [Test]
    public void Rerule_ANodeWithAnOutstandingQuestion_ClearsIt()
    {
        // A node re-ruled instead of answered had its contract replaced; whatever it was waiting to be
        // told is a question about a contract that no longer exists.
        var tree = Tree()
            .WithQuestion("parse", new NodeQuestion { Asks = "which encoding?", At = T0 })
            .Rerule("parse", Contract("Parse, streaming"), "the input no longer fits in memory");

        tree.Node("parse").Question.ShouldBeNull();
    }

    [Test]
    public void Rerule_ANodeThatFailed_ClearsTheFailure()
    {
        // The contract that failed is not the contract it has now, and it has not attempted this one.
        var tree = Tree()
            .WithFailure("parse", new NodeFailure { Message = "the tool timed out", At = T0 })
            .Rerule("parse", Contract("Parse, streaming"), "the input no longer fits in memory");

        tree.Node("parse").Failure.ShouldBeNull();
    }

    [Test]
    public void Reissue_AChildReListedIdentically_KeepsItsQuestion()
    {
        // The fix does not overreach: a child re-listed with the exact same contract was not handed
        // different work, so what it was waiting on is still a question about the contract it has.
        var tree = Tree()
            .WithQuestion("parse", new NodeQuestion { Asks = "which encoding?", At = T0 })
            .Rerule(
                "plan", Contract("Ship it", "the build is green"), "the render step needs revisiting",
                children:
                [
                    ("parse", Contract("Parse", "the parser round-trips")),   // unchanged, kept
                    ("render", Contract("Render, twice", "output matches the golden file"))
                ]);

        tree.Node("parse").Question.ShouldNotBeNull();
    }

    // ── Fixtures ──

    private static PlanTree Tree() => PlanTree.Create(
        "plan",
        Contract("Ship it", "the build is green"),
        [
            ("parse", Contract("Parse", "the parser round-trips")),
            ("render", Contract("Render", "output matches the golden file"))
        ]);

    private static AgentContract Contract(string goal, params string[] criteria) =>
        new() { Goal = goal, AcceptanceCriteria = criteria };

    private static NodeReading Reading() => new()
    {
        At = T0,
        PlanVersion = 1,
        ProjectedTokens = 10
    };

    private static CriterionVerdict Passed(string criterion) =>
        new() { Criterion = criterion, Passed = true, Oracle = "dotnet test", At = T0 };

    private static CriterionVerdict Failed(string criterion) =>
        new() { Criterion = criterion, Passed = false, Oracle = "dotnet test", At = T0 };

    private static PlanViolation Dispute() => new()
    {
        Criterion = "the parser round-trips",
        Reason = "round-tripping is impossible for the input format as specified",
        At = T0
    };

    // ── A plan that contains itself ──────────────────────────────────────

    [Test]
    public void ARerulingThatMakesANodeItsOwnChild_IsRefused()
    {
        // Found by a live run: the walk is not defensive about a ring. PostOrder recurses until the
        // stack ends, which kills the process rather than throwing — uncatchable, and with nothing
        // said about why. So it is refused where the id is still attributable to whoever named it.
        var tree = PlanTree.Create(
            "plan",
            Contract("Ship it"),
            [("parse", Contract("Parse the input"))]);

        Action cycle = () => tree.Rerule(
            "parse",
            Contract("Parse it differently"),
            "the parser cannot round-trip",
            children: [("parse", Contract("Parse the input"))]);

        cycle.ShouldThrow<ArgumentException>().Message.ShouldContain("that node itself");
    }

    [Test]
    public void ARerulingThatMakesAnAncestorItsOwnDescendant_IsRefused()
    {
        var tree = PlanTree.Create(
            "plan",
            Contract("Ship it"),
            [("parse", Contract("Parse the input"))]);

        Action cycle = () => tree.Rerule(
            "parse",
            Contract("Parse it differently"),
            "the parser cannot round-trip",
            children: [("plan", Contract("Ship it, somehow"))]);

        cycle.ShouldThrow<ArgumentException>().Message.ShouldContain("an ancestor of it");
    }

    [Test]
    public void ARerulingThatNamesTheSameChildTwice_IsRefused()
    {
        var tree = PlanTree.Create(
            "plan",
            Contract("Ship it"),
            [("parse", Contract("Parse the input"))]);

        Action twice = () => tree.Rerule(
            "parse",
            Contract("Parse it differently"),
            "the parser cannot round-trip",
            children:
            [
                ("strict", Contract("The documented dialect")),
                ("strict", Contract("The legacy dialect"))
            ]);

        twice.ShouldThrow<ArgumentException>().Message.ShouldContain("named twice");
    }
}
