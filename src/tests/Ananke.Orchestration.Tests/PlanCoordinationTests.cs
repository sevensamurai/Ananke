using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Why a plan stopped being right, and who is allowed to say it.
/// </summary>
/// <remarks>
/// <para>
/// A version's reason is the one part of a lineage no diff can recover, so a wrong one is permanent.
/// A live planner, handed a required reason field and no evidence, filled it by blaming a tool
/// limitation that does not exist — which is what this shape makes impossible: the reason is
/// <b>derived from the halt</b>, and there is no parameter for anyone to author one.
/// </para>
/// <para>
/// Read from the tree's own durable facts, in the order that answers "what actually happened" — a
/// failing gate's own evidence first (R28), then a standing dispute's words, then a dead attempt's
/// own message. No halt <em>kind</em> is needed to pick between them (R30): at most one of the three
/// is ever present on a node that just halted.
/// </para>
/// </remarks>
[TestFixture]
public class PlanCoordinationTests
{
    private const string Criterion = "the parser round-trips";
    private static readonly DateTimeOffset T0 = new(2026, 8, 29, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public void HaltReason_AFailingVerdict_IsTheChecksOwnEvidence()
    {
        // What a check produced outranks everything else — it is the evidence R28 exists for.
        var coordination = Halted(verdictBasis: "expected <a/> but the parser returned <b/>");

        coordination.HaltReason.ShouldBe("expected <a/> but the parser returned <b/>");
    }

    [Test]
    public void HaltReason_ADispute_IsTheWitnessesOwnWordsVerbatim()
    {
        // The node is the only thing that observed the contradiction. Anything else is a paraphrase
        // of evidence by something that was not there.
        var coordination = Halted(dispute: "the input format has no round-trip");

        coordination.HaltReason.ShouldBe("the input format has no round-trip");
    }

    [Test]
    public void HaltReason_ADisputeWithNothingSaid_IsARecordedFactRatherThanABlank()
    {
        // A gap must read as a gap. An empty reason on a minted version is indistinguishable from a
        // version nobody explained, which is the state this whole shape exists to prevent.
        var coordination = Halted(dispute: "   ");

        coordination.HaltReason.ShouldBe(
            "'parse' reported that its contract cannot be met, and stated no reason.");
    }

    [Test]
    public void HaltReason_AFailure_IsTheToolsOwnMessage()
    {
        // Not rewritten, and not summarised. The text a tool produced is the evidence; anything
        // nicer is an account of it by something that did not fail.
        var coordination = Halted(failure: "429 Too Many Requests (quota exhausted)");

        coordination.HaltReason.ShouldBe("'parse' failed: 429 Too Many Requests (quota exhausted)");
    }

    [Test]
    public void HaltReason_AFailureThatSaidNothing_IsARecordedFactRatherThanABlank()
    {
        var coordination = Halted(failure: "   ");

        coordination.HaltReason.ShouldBe("'parse' failed, and reported nothing about why.");
    }

    [Test]
    public void HaltReason_APlanThatSettled_IsNothingAtAll()
    {
        var settled = new PlanCoordination { Result = Pass(Tree(), haltedAt: null) };

        settled.HaltReason.ShouldBeNull();
    }

    // ── Fixtures ──

    private static PlanCoordination Halted(
        string? dispute = null,
        string? failure = null,
        string? verdictBasis = null,
        int? bound = 2,
        int attempts = 1)
    {
        var tree = Tree(bound);

        for (var i = 0; i < attempts; i++)
        {
            tree = tree.WithReading("parse", new NodeReading
            {
                At = T0,
                PlanVersion = 1,
                ProjectedTokens = 20
            });
        }

        if (dispute is not null)
        {
            tree = tree.WithViolation("parse", new PlanViolation
            {
                Criterion = Criterion,
                Reason = dispute,
                At = T0
            });
        }

        if (failure is not null)
            tree = tree.WithFailure("parse", new NodeFailure { Message = failure, At = T0 });

        if (verdictBasis is not null)
        {
            tree = tree.WithVerdict("parse", new CriterionVerdict
            {
                Criterion = Criterion,
                Passed = false,
                Oracle = "a check",
                Basis = verdictBasis,
                At = T0
            });
        }

        return new PlanCoordination { Result = Pass(tree, "parse") };
    }

    private static PlanRunResult Pass(PlanTree tree, string? haltedAt) => new()
    {
        Tree = tree,
        Executed = haltedAt is null ? [] : [haltedAt],
        Skipped = [],
        Rulings = new Dictionary<string, Verification>(),
        HaltedAt = haltedAt
    };

    private static PlanTree Tree(int? bound = 2) => PlanTree.Create(
        "plan",
        new AgentContract { Goal = "Ship it", AcceptanceCriteria = ["the build is green"] },
        [
            ("parse", new AgentContract
            {
                Goal = "Parse",
                AcceptanceCriteria = [Criterion]
            })
        ]);
}
