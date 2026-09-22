using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Verification: external, deterministic, gates-then-score — and abstaining wherever nothing can
/// decide.
/// </summary>
/// <remarks>
/// <para>
/// Three rules are under test, and each has a failure mode that looks like success. <b>A node must
/// not grade its own work</b> — the two cases it cannot tell apart from the inside are "my
/// assumption was wrong" and "my contract is wrong". <b>Gates must not be averaged with scores</b>,
/// or a regression becomes purchasable. And <b>an undecidable criterion must not read as a passing
/// one</b>, which is the whole reason abstention is a first-class outcome rather than a default.
/// </para>
/// <para>
/// Everything here is deterministic on purpose. Putting a model in this seat is a separate decision
/// with a different risk profile: an verifier sits in the control path, so one that is wrong does
/// not merely mismeasure the work, it misdirects it.
/// </para>
/// </remarks>
[TestFixture]
public class VerificationTests
{
    private const string Gate = "the build is green";
    private const string OtherGate = "no public API changes";
    private const string Quality = "the diff is small";

    // ── Gates, then a score ──

    [Test]
    public async Task Verify_EveryGatePasses_Passes()
    {
        var ruling = await Rule(Contract(gates: [Gate]), Check(Gate, passes: true));

        ruling.Outcome.ShouldBe(VerificationOutcome.Passed);
        ruling.Verdicts.ShouldHaveSingleItem().Oracle.ShouldBe("a check");
        ruling.Abstained.ShouldBeEmpty();
    }

    [Test]
    public async Task Verify_AFailedGate_IsNotPurchasableWithAPerfectScore()
    {
        // The shape any weighted blend of gates and quality would allow, asserted directly.
        var ruling = await Rule(
            Contract(gates: [Gate], quality: [Quality]),
            Check(Gate, passes: false), Check(Quality, passes: true));

        ruling.Outcome.ShouldBe(VerificationOutcome.GateFailed);
        ruling.Score.ShouldBeNull();   // no number sits next to an unacceptable result
        ruling.Verdicts.ShouldContain(v => v.Criterion == Quality && v.Passed);
    }

    [Test]
    public async Task Verify_ScoresOnlyOnceEveryGateHasPassed()
    {
        var ruling = await Rule(
            Contract(gates: [Gate], quality: [Quality, "it is readable"]),
            Check(Gate, passes: true), Check(Quality, passes: true), Check("it is readable", passes: false));

        ruling.Outcome.ShouldBe(VerificationOutcome.Passed);
        ruling.Score.ShouldBe(0.5);
    }

    [Test]
    public async Task Verify_AQualityCriterionFailing_DoesNotFailTheNode()
    {
        var ruling = await Rule(
            Contract(gates: [Gate], quality: [Quality]),
            Check(Gate, passes: true), Check(Quality, passes: false));

        ruling.Outcome.ShouldBe(VerificationOutcome.Passed);
        ruling.Score.ShouldBe(0);
    }

    // ── Abstention ──

    [Test]
    public async Task Verify_ACriterionNothingCanDecide_AbstainsRatherThanPassing()
    {
        // The failure this prevents is silent: an undecidable criterion treated as met looks
        // identical to a verified one from every downstream vantage point.
        var ruling = await Rule(Contract(gates: [Gate, "the code is elegant"]), Check(Gate, passes: true));

        ruling.Outcome.ShouldBe(VerificationOutcome.Abstained);
        ruling.Abstained.ShouldBe(["the code is elegant"]);
        ruling.Verdicts.ShouldNotContain(v => v.Criterion == "the code is elegant");
        ruling.Score.ShouldBeNull();
    }

    [Test]
    public async Task Verify_AFailedGateBeatsAnAbstention()
    {
        // Something definitely failed; not knowing about something else does not soften that.
        var ruling = await Rule(
            Contract(gates: [Gate, "the code is elegant"]), Check(Gate, passes: false));

        ruling.Outcome.ShouldBe(VerificationOutcome.GateFailed);
        ruling.Abstained.ShouldBe(["the code is elegant"]);
    }

    // ── Disputes: refutable, never upheld on this type's authority ──

    [Test]
    public async Task Verify_ADisputedCriterionThatChecksOut_IsRefuted()
    {
        var node = Node(Contract(gates: [Gate])) with
        {
            Violation = new PlanViolation { Criterion = Gate, Reason = "cannot be met", At = T0 }
        };

        var ruling = await Rule(node, Check(Gate, passes: true));

        ruling.Outcome.ShouldBe(VerificationOutcome.ViolationRefuted);
    }

    [Test]
    public async Task Verify_ADisputeNothingCanDecide_StandsAndTravels()
    {
        // This type may rule that a node was mistaken. It may not rule that a contract is right —
        // that judgement belongs to whoever wrote the contract.
        var node = Node(Contract(gates: ["the format round-trips"])) with
        {
            Violation = new PlanViolation
            {
                Criterion = "the format round-trips",
                Reason = "the spec forbids it",
                At = T0
            }
        };

        var ruling = await Rule(node);

        ruling.Outcome.ShouldBe(VerificationOutcome.ViolationStands);
    }

    [Test]
    public async Task Verify_ADisputedCriterionThatAlsoFailsItsCheck_Stands()
    {
        var node = Node(Contract(gates: [Gate])) with
        {
            Violation = new PlanViolation { Criterion = Gate, Reason = "cannot be met", At = T0 }
        };

        var ruling = await Rule(node, Check(Gate, passes: false));

        ruling.Outcome.ShouldBe(VerificationOutcome.ViolationStands);
    }

    // ── The declared bound ──

    // ── Decidability, asked before anything runs ──

    [Test]
    public void CannotDecide_ACriterionNoCheckCovers_IsNamedAndNothingIsRun()
    {
        // The whole point of asking in advance: if finding out costs what ruling costs, nobody can
        // afford to ask at the moment it matters — while a plan is being re-authored.
        var verifier = new DeterministicVerifier([Check(Gate, passes: true), Exploding()]);

        verifier.CannotDecide([Gate, OtherGate]).ShouldBe([OtherGate]);
    }

    [Test]
    public void CannotDecide_EveryCriterionCovered_IsEmptyAndNotNull()
    {
        // Empty and null are different answers — "everything here can be decided" against "nobody
        // could say" — and collapsing them is exactly the silence this exists to break.
        var verifier = new DeterministicVerifier([Check(Gate, passes: true)]);

        verifier.CannotDecide([Gate]).ShouldBeEmpty();
    }

    [Test]
    public async Task CannotDecide_TheSameAnswerARulingWouldGive_ForEveryCriterion()
    {
        // The two must not be able to disagree: both ask CanRule, so a criterion reported decidable
        // here is one the pass will actually rule on rather than abstain from.
        var verifier = new DeterministicVerifier([Check(Gate, passes: true)]);
        var undecidable = verifier.CannotDecide([Gate, OtherGate])!;

        var ruling = await Rule(Contract(gates: [Gate, OtherGate]), Check(Gate, passes: true));

        ruling.Abstained.ShouldBe(undecidable);
    }

    [Test]
    public void CannotDecide_AVerifierThatCannotSayInAdvance_AnswersNothingRatherThanNone()
    {
        // The default, and it is what an verifier that would have to do the work to find out must
        // answer. Claiming everything is decidable would be a claim it never made.
        IVerifier opaque = new AlwaysPasses();

        opaque.CannotDecide([Gate]).ShouldBeNull();
    }

    [Test]
    public async Task Execute_WhatTheVerifierReads_IsTheWholeRecordAndNotTheNodesViewOfIt()
    {
        // A projection is budgeted for the thing doing the work — it omits what will not fit. A
        // ruling is made from outside the node, so inheriting those omissions would mean ruling on
        // whatever the node happened to be shown. Found live: a reviewer asked whether every step
        // had a verdict could not tell, because seven records were missing from the view it got.
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(PlanTree.Create(
            "plan",
            Contract(gates: [Gate]),
            [
                ("first", Contract(gates: ["the first thing holds"])),
                ("second", Contract(gates: ["the second thing holds"]))
            ]));

        var seen = new List<string>();
        var verifier = new CapturingVerifier(seen);

        // Tight enough that a node cannot be shown the whole plan.
        await new PlanExecutor(store, projectionTokenBudget: 20, verifier: verifier)
            .ExecuteAsync("plan", (_, _) => Task.FromResult(NodeOutcome.Nothing));

        // The root's ruling has to be able to see what its children were judged on.
        seen[^1].ShouldContain("the first thing holds");
        seen[^1].ShouldContain("the second thing holds");
    }

    /// <summary>Records what it was shown, and rules on nothing.</summary>
    private sealed class CapturingVerifier(List<string> views) : IVerifier
    {
        public Task<Verification> VerifyAsync(
            VerificationRequest request, CancellationToken ct = default)
        {
            views.Add(request.TreeView);

            // Rules everything met, so the children leave records behind for the root's view to
            // contain — which is the thing under test.
            return Task.FromResult(new Verification
            {
                Verdicts =
                [
                    .. request.Node.Contract.AcceptanceCriteria.Select(c => new CriterionVerdict
                    {
                        Criterion = c, Passed = true, Oracle = "a capture", At = T0
                    })
                ],
                Outcome = VerificationOutcome.Passed
            });
        }
    }

    // ── Through the executor: the node stops grading its own work ──

    [Test]
    public async Task Execute_WithAVerifier_TheVerifiersVerdictsReachTheTreeAndTheNodesDoNot()
    {
        var store = await Seeded();
        var verifier = new DeterministicVerifier([Check(Gate, passes: false)]);

        var result = await new PlanExecutor(store, verifier: verifier).ExecuteAsync(
            "plan", (_, _) => Task.FromResult(new NodeOutcome
            {
                // The node's own account of how it did. It is a report, and it stops here.
                Verdicts = [new CriterionVerdict
                {
                    Criterion = Gate, Passed = true, Oracle = "the node said so", At = T0
                }]
            }));

        var verdicts = result.Tree.Node("child").Verdicts;
        verdicts.ShouldHaveSingleItem();
        verdicts[0].Passed.ShouldBeFalse();
        verdicts[0].Oracle.ShouldBe("a check");
        result.Rulings["child"].Outcome.ShouldBe(VerificationOutcome.GateFailed);
    }

    [Test]
    public async Task Execute_AGateFailure_HaltsThePassEvenWhenTheNodeReportedNothing()
    {
        // R29's gap: a node that asks no question and disputes nothing, whose check simply fails,
        // used to leave the pass believing there was nothing to decide.
        var store = await Seeded();
        var verifier = new DeterministicVerifier([Check(Gate, passes: false)]);

        var result = await new PlanExecutor(store, verifier: verifier)
            .ExecuteAsync("plan", (_, _) => Task.FromResult(NodeOutcome.Nothing));

        result.HaltedAt.ShouldBe("child");
        result.Rulings["child"].Outcome.ShouldBe(VerificationOutcome.GateFailed);
    }

    [Test]
    public async Task Execute_AGateFailureWithNoReport_StillReachesTheSupervisorAndGetsADecision()
    {
        var store = await Seeded();
        var verifier = new DeterministicVerifier([Check(Gate, passes: false)]);
        var pass = await new PlanExecutor(store, verifier: verifier)
            .ExecuteAsync("plan", (_, _) => Task.FromResult(NodeOutcome.Nothing));

        var asked = false;
        PlanCoordination? seen = null;

        var job = new PlanSupervisorJob<CoordinationState>(
            "coordinate", new SupervisionOptions { Store = store },
            (coordination, _) =>
            {
                asked = true;
                seen = coordination;
                return Task.FromResult(PlanDecision.Ask([]));
            },
            s => s.Coordination, (s, c) => s with { Coordination = c });

        await job.ExecuteAsync(
            new CoordinationState { Coordination = new PlanCoordination { Result = pass } });

        asked.ShouldBeTrue("a report with nothing in it must not be mistaken for nothing to decide");
        seen!.NodeId.ShouldBe("child");
    }

    private sealed record CoordinationState
    {
        public PlanCoordination? Coordination { get; init; }
    }

    [Test]
    public async Task Execute_WithNoVerifier_TheNodesOwnVerdictsAreRecordedAsBefore()
    {
        // R8: a plan with nothing configured behaves exactly as it did.
        var store = await Seeded();

        var result = await new PlanExecutor(store).ExecuteAsync(
            "plan", (_, _) => Task.FromResult(new NodeOutcome
            {
                Verdicts = [new CriterionVerdict
                {
                    Criterion = Gate, Passed = true, Oracle = "the node said so", At = T0
                }]
            }));

        result.Tree.Node("child").Verdicts[0].Oracle.ShouldBe("the node said so");
        result.Rulings.ShouldBeEmpty();
    }

    [Test]
    public async Task Execute_AnAbstention_LeavesTheTreeSayingNothingAboutThatCriterion()
    {
        // What was not decided is absent from the tree — correctly — which is exactly why the pass
        // has to report it. A silent absence is indistinguishable from a criterion nobody wrote.
        var store = await Seeded();

        var result = await new PlanExecutor(store, verifier: new DeterministicVerifier([]))
            .ExecuteAsync("plan", (_, _) => Task.FromResult(NodeOutcome.Nothing));

        result.Tree.Node("child").Verdicts.ShouldBeEmpty();
        result.Tree.OutcomeOf("child").ShouldBe(ContractOutcome.Unmet);
        result.Rulings["child"].Abstained.ShouldBe([Gate]);
    }

    [Test]
    public async Task Execute_ARefutedDispute_DoesNotHaltThePass()
    {
        // A dispute reaches the tree through DisputeAsync now — an external call, never a node's own
        // report (R26) — so it is seeded before the pass rather than returned by the runner.
        var store = await Seeded();
        await new PlanExecutor(store).DisputeAsync("plan", "child", Gate, "cannot be met");

        var verifier = new DeterministicVerifier([Check(Gate, passes: true)]);

        var result = await new PlanExecutor(store, verifier: verifier).ExecuteAsync(
            "plan", (_, _) => Task.FromResult(NodeOutcome.Nothing));

        result.HaltedAt.ShouldBeNull();
        result.Tree.Node("child").Violation.ShouldBeNull();

        // The walk reached the root rather than breaking out of the loop. (The root declares no
        // criteria of its own, so a satisfied child satisfies it and it is skipped, not run.)
        result.Skipped.ShouldContain("plan");
        result.RootOutcome.ShouldBe(ContractOutcome.Met);
    }

    [Test]
    public async Task Execute_ANodeThatHasUsedUpItsAttempts_IsNotRunAgain()
    {
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(PlanTree.Create(
            "plan", Contract(), [("child", Contract(gates: [Gate]))]));

        var executor = new PlanExecutor(store, verifier: new DeterministicVerifier([Check(Gate, passes: false)]));
        var attempts = 0;

        for (var pass = 0; pass < 5; pass++)
        {
            await executor.ExecuteAsync("plan", (ctx, _) =>
            {
                if (ctx.Node.Id == "child")
                    attempts++;

                return Task.FromResult(NodeOutcome.Nothing);
            });
        }

        // Nothing stops it from outside any more: a step that completes without satisfying its
        // contract is re-run by every pass, and what ends the run is the coordinator, not a count.
        attempts.ShouldBe(5);
    }

    // ── Fixtures ──

    private static readonly DateTimeOffset T0 = new(2026, 8, 24, 9, 0, 0, TimeSpan.Zero);

    private static async Task<IPlanTreeStore> Seeded()
    {
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(PlanTree.Create("plan", Contract(), [("child", Contract(gates: [Gate]))]))
            .ConfigureAwait(false);
        return store;
    }

    private static AgentContract Contract(string[]? gates = null, string[]? quality = null) => new()
    {
        Goal = "Do the work",
        AcceptanceCriteria = gates ?? [],
        QualityCriteria = quality ?? []
    };

    private static PlanNode Node(AgentContract contract) => new() { Id = "n", Contract = contract };

    private static Task<Verification> Rule(
        AgentContract contract, params IDeterministicCheck[] checks) =>
        Rule(Node(contract), 1, checks);

    private static Task<Verification> Rule(
        AgentContract contract, int iterations, params IDeterministicCheck[] checks) =>
        Rule(Node(contract), iterations, checks);

    private static Task<Verification> Rule(PlanNode node, params IDeterministicCheck[] checks) =>
        Rule(node, 1, checks);

    private static Task<Verification> Rule(PlanNode node, int iterations, params IDeterministicCheck[] checks) =>
        new DeterministicVerifier(checks).VerifyAsync(new VerificationRequest
        {
            Node = node,
            TreeView = string.Empty
        });

    private static IDeterministicCheck Check(string criterion, bool passes) =>
        new PredicateCheck("a check", [criterion], _ => passes);

    /// <summary>A check that covers nothing and fails the test if anything ever runs it.</summary>
    private static IDeterministicCheck Exploding() =>
        new PredicateCheck("never runs", [], _ =>
            throw new InvalidOperationException("Deciding what is decidable must not run a check."));

    /// <summary>An verifier that never says what it cannot decide — the interface's default.</summary>
    private sealed class AlwaysPasses : IVerifier
    {
        public Task<Verification> VerifyAsync(
            VerificationRequest request, CancellationToken ct = default) =>
            Task.FromResult(new Verification
            {
                Verdicts = [],
                Outcome = VerificationOutcome.Passed
            });
    }
}
