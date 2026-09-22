using System.Text.Json;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// A model in the verifier's seat: what it may rule on, and the three things it may never do.
/// </summary>
/// <remarks>
/// <para>
/// An verifier sits in the <em>control</em> path, so one that is wrong does not merely mismeasure
/// the work — it misdirects it. That is why every test here is about a refusal rather than about the
/// happy path: passing what it could not tell, ruling a criterion wrong, and refuting the one report
/// a node is entitled to make.
/// </para>
/// <para>
/// The live finding this answers: a stronger planner authored visibly better criteria on every pass,
/// nothing could rule on any of them, and the plan could not settle however good the re-rulings got.
/// </para>
/// </remarks>
[TestFixture]
public class AgentVerifierTests
{
    private const string Gate = "the build is green";
    private const string Unmeasured = "the notes read well";

    // ── What it adds ──

    [Test]
    public async Task Verify_ACriterionNoCheckCovers_IsRuledByTheReviewer()
    {
        var reviewer = Answering(("met", "the record shows notes.md with a summary of every change"));

        var ruling = await Rule(Contract(gates: [Gate, Unmeasured]), reviewer, Check(Gate, passes: true));

        ruling.Outcome.ShouldBe(VerificationOutcome.Passed);
        ruling.Abstained.ShouldBeEmpty();

        var verdict = ruling.Verdicts.Single(v => v.Criterion == Unmeasured);
        verdict.Passed.ShouldBeTrue();
        verdict.Oracle.ShouldBe("the reviewer, from the record");

        // A judgement without its grounds is a claim nobody can weigh — the same shape as a change
        // of plan recorded without its reason.
        verdict.Basis.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task Verify_WhatTheChecksDecided_IsLeftAlone()
    {
        // Checks are cheaper, repeatable, and answer about the artifact rather than the record. A
        // reviewer that could overturn one would make the weaker authority the final one.
        var reviewer = Answering(("met", "it looks fine to me"));

        var ruling = await Rule(Contract(gates: [Gate]), reviewer, Check(Gate, passes: false));

        ruling.Outcome.ShouldBe(VerificationOutcome.GateFailed);
        ruling.Verdicts.ShouldHaveSingleItem().Oracle.ShouldBe("a check");
    }

    [Test]
    public async Task Verify_NothingLeftUndecided_NeverAsksTheModel()
    {
        // A model call for a node whose gates all decided is a cost with no question attached.
        var reviewer = new CountingModel("{}");

        await Rule(Contract(gates: [Gate]), reviewer, Check(Gate, passes: true));

        reviewer.Calls.ShouldBe(0);
    }

    // ── What it may never do ──

    [Test]
    public async Task Verify_ACriterionItCannotTell_StaysAbstained()
    {
        var reviewer = Answering(("cannot tell", "the record says nothing about the notes"));

        var ruling = await Rule(Contract(gates: [Gate, Unmeasured]), reviewer, Check(Gate, passes: true));

        ruling.Abstained.ShouldBe([Unmeasured]);
        ruling.Outcome.ShouldBe(VerificationOutcome.Abstained);
        ruling.Verdicts.ShouldNotContain(v => v.Criterion == Unmeasured);
    }

    [Test]
    public async Task Verify_AnAnswerOffScript_IsReadAsAnAbstention()
    {
        // The safe direction, and it has to be the default: a model that answers something nobody
        // asked for must not produce a verdict, because the failure that matters is an unverified
        // criterion recorded as met.
        var reviewer = Answering(("probably fine, honestly", "hard to say"));

        var ruling = await Rule(Contract(gates: [Unmeasured]), reviewer, Check(Gate, passes: true));

        ruling.Abstained.ShouldBe([Unmeasured]);
        ruling.Verdicts.ShouldBeEmpty();
    }

    [Test]
    public async Task Verify_ADisputedNode_KeepsItsDisputeWhateverTheReviewerSays()
    {
        // A check that refutes a dispute ran a program over the artifact. A model that refutes one
        // has re-read the record the node read and disagreed with the witness — so the dispute
        // travels, and the coordinator is shown both.
        var reviewer = Answering(("met", "looks met to me"));

        var node = Node(Contract(gates: [Unmeasured])) with
        {
            Violation = new PlanViolation
            {
                Criterion = Unmeasured,
                Reason = "there is no way to tell",
                At = T0
            }
        };

        var ruling = await Rule(node, reviewer, Check(Gate, passes: true));

        ruling.Outcome.ShouldBe(VerificationOutcome.ViolationStands);
        ruling.Verdicts.ShouldContain(v => v.Criterion == Unmeasured && v.Passed);
    }

    [Test]
    public void CannotDecide_IsAlwaysNobodyCouldSay()
    {
        // Deciding whether it can rule *is* the ruling, so it does not claim in advance — and what
        // the checks beneath it would abstain on is not the answer either: those are exactly the
        // criteria this exists to try.
        new AgentVerifier(Answering(("met", "x")), new DeterministicVerifier([Check(Gate, true)]))
            .CannotDecide([Gate, Unmeasured])
            .ShouldBeNull();
    }

    // ── Through the supervision ──

    [Test]
    public void ResolvedVerifier_IsNeverBuiltImplicitly()
    {
        // There was sugar here that made a verifier out of a `Reviewer` model, and R8 retired that
        // role: what no check can decide goes to the Supervisor. A supervision that wants a model in
        // the loop composes it, because which authority runs first is a decision worth writing down
        // — and the checks are the cheaper and stronger one.
        new SupervisionOptions
        {
            Runner = (_, _) => Task.FromResult(NodeOutcome.Nothing),
            Supervisor = Answering(("met", "x"))
        }.ResolvedVerifier.ShouldBeNull();
    }

    [Test]
    public void ResolvedVerifier_IsWhateverTheSupervisionWasGiven()
    {
        new SupervisionOptions
        {
            Runner = (_, _) => Task.FromResult(NodeOutcome.Nothing),
            Verifier = new DeterministicVerifier([Check(Gate, passes: true)])
        }.ResolvedVerifier.ShouldBeOfType<DeterministicVerifier>();
    }

    // ── Fixtures ──

    private static readonly DateTimeOffset T0 = new(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);

    private static Task<Verification> Rule(
        AgentContract contract, IAgentModel reviewer, IDeterministicCheck check) =>
        Rule(Node(contract), reviewer, check);

    private static Task<Verification> Rule(
        PlanNode node, IAgentModel reviewer, IDeterministicCheck check) =>
        new AgentVerifier(reviewer, new DeterministicVerifier([check]))
            .VerifyAsync(new VerificationRequest
            {
                Node = node,
                TreeView = "the plan, as recorded"
            });

    private static AgentContract Contract(string[] gates) => new()
    {
        Goal = "Do the work",
        AcceptanceCriteria = gates
    };

    private static PlanNode Node(AgentContract contract) => new() { Id = "n", Contract = contract };

    private static IDeterministicCheck Check(string criterion, bool passes) =>
        new PredicateCheck("a check", [criterion], _ => passes);

    /// <summary>A reviewer that answers with the given verdict for every criterion it is shown.</summary>
    private static IAgentModel Answering(params (string Verdict, string Basis)[] findings) =>
        new CountingModel(JsonSerializer.Serialize(new
        {
            findings = findings.Select((f, i) => new { number = i + 1, verdict = f.Verdict, basis = f.Basis })
        }));

    private sealed class CountingModel(string json) : IAgentModel
    {
        public int Calls { get; private set; }

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new AgentResponse { Text = json });
        }
    }
}
