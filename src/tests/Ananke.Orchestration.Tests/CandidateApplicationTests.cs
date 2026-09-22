using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// The executor proposes; the loop applies it (R27) — and the shape gate that sits between them
/// checks the message, never the work (R29's own falsifier, pinned by S0 and S8).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two small worlds, on purpose.</b> <c>PatchTree</c> stands in for S1's code generation — a file
/// nobody but the loop ever writes; <c>TripTree</c> stands in for S5's trip — a place nobody but the
/// loop ever carries. Neither is the real thing the trip domain ships with (<c>ItineraryDemo</c>); the
/// claim under test is that one mechanism serves both, and a synthetic pair proves that without a live
/// model anywhere in the loop.
/// </para>
/// <para>
/// <b>S0 is the one that would have spun forever before this item.</b> A candidate the shape gate
/// keeps rejecting has no verdict to reach a supervisor with, so the executor is asked again,
/// mechanically, carrying exactly what it said and why it did not land — nothing this loop composed —
/// until it gives up and becomes ordinary evidence instead.
/// </para>
/// <para>
/// <b>S8 is what a shape gate must not do</b>, discovered by applying R29's own test to an earlier
/// draft: rejecting <c>"carry atlantis"</c> for naming nowhere real would have bounced it before the
/// world ever saw it, and the pass would spin on an impossible step reporting nothing.
/// </para>
/// </remarks>
[TestFixture]
public class CandidateApplicationTests
{
    // ── S1 — it just works (code) ──

    [Test]
    public async Task Execute_AShapeValidCandidate_IsAppliedAndTheCheckSeesIt()
    {
        var written = new HashSet<string>(StringComparer.Ordinal);
        var applied = new List<string>();

        var store = await Seeded(PatchTree());

        var executor = new PlanExecutor(
            store,
            verifier: new DeterministicVerifier([Written(written)]),
            applier: (candidate, _, _) =>
            {
                applied.Add(candidate.ToString());
                written.Add("report.cs");
                return Task.FromResult(PlanApplication.Ok());
            });

        var result = await executor.ExecuteAsync("patch", (_, _) =>
            Task.FromResult(new NodeOutcome
            {
                Candidate = new Operation { Name = "patch", Arguments = ["report.cs", "--json"] }
            }));

        applied.ShouldBe(["patch(report.cs, --json)"]);
        result.HaltedAt.ShouldBeNull();
        result.RootOutcome.ShouldBe(ContractOutcome.Met);
    }

    // ── S5 — it just works (trip) ──

    [Test]
    public async Task Execute_ATripCandidate_CarriesThePlaceAndSettles()
    {
        var carried = new HashSet<string>(StringComparer.Ordinal);

        var store = await Seeded(TripTree());

        var executor = new PlanExecutor(
            store,
            verifier: new DeterministicVerifier([Carried(carried)]),
            applier: (candidate, _, _) =>
            {
                carried.Add(candidate.Argument(0)!);
                return Task.FromResult(PlanApplication.Ok());
            });

        var result = await executor.ExecuteAsync("hakone", (_, _) =>
            Task.FromResult(new NodeOutcome
            {
                Candidate = new Operation { Name = "carry", Arguments = ["hakone"] }
            }));

        carried.ShouldContain("hakone");
        result.HaltedAt.ShouldBeNull();
        result.RootOutcome.ShouldBe(ContractOutcome.Met);
    }

    // ── S8 — the plan is simply wrong ──

    [Test]
    public async Task Execute_ACandidateNamingSomewhereTheWorldDoesNotKnow_IsAppliedNotRejected()
    {
        // The live bug R29 was written against: a shape gate that judged the referent would have
        // bounced "carry atlantis" before the world ever saw it, and the pass would spin on an
        // impossible step forever, reporting nothing. Applying it and letting the acceptance check
        // fail — the world's answer, not the message's defect — is the fix.
        var applierCalls = 0;

        var store = await Seeded(TripTree());

        var executor = new PlanExecutor(
            store,
            verifier: new DeterministicVerifier([Carried(new HashSet<string>(StringComparer.Ordinal))]),
            applier: (_, _, _) =>
            {
                applierCalls++;
                return Task.FromResult(PlanApplication.Ok());
            });

        var result = await executor.ExecuteAsync("hakone", (_, _) =>
            Task.FromResult(new NodeOutcome
            {
                Candidate = new Operation { Name = "carry", Arguments = ["atlantis"] }
            }));

        applierCalls.ShouldBe(1); // looked at, not bounced at the gate
        result.Tree.Node("hakone").Failure.ShouldBeNull(); // not a shape failure
        result.HaltedAt.ShouldBe("hakone"); // the acceptance gate is what stops it, not this
    }

    // ── What a step's own Done flag does to its candidate ──

    [Test]
    public async Task Execute_ACandidateFromAStepThatIsNotDone_NeverReachesTheApplier()
    {
        // A step that proposes a change cannot have met its contract yet: nothing has applied the
        // change or checked it. It reports Done: false and a candidate, and the candidate is
        // discarded before the gate and before the applier.
        var applierCalls = 0;

        var store = await Seeded(TripTree());

        var executor = new PlanExecutor(
            store,
            applier: (_, _, _) =>
            {
                applierCalls++;
                return Task.FromResult(PlanApplication.Ok());
            },
            operations: new OperationCatalog(
            [
                new OperationDefinition { Name = "carry", Arity = 1, Description = "carry(place)" }
            ]));

        var result = await executor.ExecuteAsync("hakone", (_, _) =>
            Task.FromResult(new NodeOutcome
            {
                Candidate = new Operation { Name = "carry", Arguments = ["hakone"] },
                Done = false
            }));

        applierCalls.ShouldBe(0);
        result.Tree.Node("hakone").Failure.ShouldBeNull(); // not a shape failure — nothing was gated
    }

    // ── What the legend shows a step ──

    [Test]
    public void Legend_AnOperationWithAnExample_ShowsIt()
    {
        var legend = new OperationCatalog(
        [
            new OperationDefinition
            {
                Name = "carry",
                Arity = 1,
                Description = "carry(place)",
                Example = "carry(\"hakone\")"
            }
        ]).Legend();

        legend.ShouldContain("carry(arg1) — carry(place)");
        legend.ShouldContain("carry(\"hakone\")");
    }

    [Test]
    public void Legend_AnOperationWithNoExample_ReadsExactlyAsItAlwaysHas()
    {
        var legend = new OperationCatalog(
        [
            new OperationDefinition { Name = "carry", Arity = 1, Description = "carry(place)" }
        ]).Legend();

        legend.ShouldBe("  carry(arg1) — carry(place)");
    }

    // ── The catalog rules before the applier is reached ──

    [Test]
    public async Task Execute_AnOperationTheCatalogRefuses_NeverReachesTheApplier()
    {
        // It was declared and wired to nothing: the catalog printed its legend into the prompt and
        // ruled on nothing that came back, so an applier's first line indexed arguments no one had
        // counted.
        var applierCalls = 0;

        var store = await Seeded(TripTree());

        var executor = new PlanExecutor(
            store,
            applier: (_, _, _) =>
            {
                applierCalls++;
                return Task.FromResult(PlanApplication.Ok());
            },
            operations: new OperationCatalog(
            [
                new OperationDefinition { Name = "carry", Arity = 1, Description = "carry(place)" }
            ]));

        var result = await executor.ExecuteAsync("hakone", (_, _) =>
            Task.FromResult(new NodeOutcome
            {
                Candidate = new Operation { Name = "carry", Arguments = [] }
            }));

        applierCalls.ShouldBe(0);
        result.Tree.Node("hakone").Failure.ShouldNotBeNull()
            .Message.ShouldContain("takes 1 argument(s), and 0 were given");
    }

    [Test]
    public async Task Execute_AnOperationTheCatalogAdmits_ReachesTheApplier()
    {
        var applied = new List<string>();

        var store = await Seeded(TripTree());

        var executor = new PlanExecutor(
            store,
            verifier: new DeterministicVerifier([Carried(new HashSet<string>(StringComparer.Ordinal) { "hakone" })]),
            applier: (candidate, _, _) =>
            {
                applied.Add(candidate.ToString());
                return Task.FromResult(PlanApplication.Ok());
            },
            operations: new OperationCatalog(
            [
                new OperationDefinition { Name = "carry", Arity = 1, Description = "carry(place)" }
            ]));

        await executor.ExecuteAsync("hakone", (_, _) =>
            Task.FromResult(new NodeOutcome
            {
                Candidate = new Operation { Name = "carry", Arguments = ["hakone"] }
            }));

        applied.ShouldBe(["carry(hakone)"]);
    }

    // ── S0 — the candidate is unusable ──

    [Test]
    public async Task Execute_AnEmptyCandidate_NeverReachesTheApplier()
    {
        var applierCalls = 0;

        var store = await Seeded(PatchTree());

        var executor = new PlanExecutor(
            store,
            applier: (_, _, _) =>
            {
                applierCalls++;
                return Task.FromResult(PlanApplication.Ok());
            });

        var result = await executor.ExecuteAsync("patch", (_, _) =>
            Task.FromResult(new NodeOutcome { Candidate = new Operation { Name = "" } }));

        applierCalls.ShouldBe(0); // nothing is applied, nothing is built, no test runs
        result.Tree.Node("patch").Failure.ShouldNotBeNull()
            .Message.ShouldBe("the response named no operation.");
        result.HaltedAt.ShouldBe("patch");
    }

    [Test]
    public async Task Execute_ACandidateTheShapeGateKeepsRejecting_RetriesMechanicallyThenBecomesEvidence()
    {
        // What a retry may carry, and nothing else (§2.3): the contract (pinned, unchanged across
        // attempts), the rejected candidate, and the finding — verbatim, never a word this loop wrote.
        var seen = new List<(string? Candidate, string? Finding)>();
        var applierCalls = 0;

        var store = await Seeded(PatchTree());

        var executor = new PlanExecutor(
            store,
            applier: (_, _, _) =>
            {
                applierCalls++;
                return Task.FromResult(PlanApplication.Rejected("not an operation I recognise."));
            });

        var result = await executor.ExecuteAsync("patch", (ctx, _) =>
        {
            seen.Add((ctx.Rejection?.Candidate.ToString(), ctx.Rejection?.Finding));
            return Task.FromResult(new NodeOutcome
            {
                Candidate = new Operation { Name = "gibberish" }
            });
        });

        applierCalls.ShouldBe(3);
        seen.ShouldBe(
        [
            (null, null),
            ("gibberish()", "not an operation I recognise."),
            ("gibberish()", "not an operation I recognise.")
        ]);

        result.HaltedAt.ShouldBe("patch");
        result.Tree.Node("patch").Failure.ShouldNotBeNull()
            .Message.ShouldBe("not an operation I recognise.");
    }

    [Test]
    public async Task Execute_ANodeWithNoCandidate_NeverConsultsTheApplierOrTheGate()
    {
        // A node that only decomposes has nothing to propose, and that is not a shape failure —
        // the applier is never asked about work nobody offered.
        var applierCalls = 0;

        var store = await Seeded(PatchTree());

        var executor = new PlanExecutor(
            store,
            applier: (_, _, _) =>
            {
                applierCalls++;
                return Task.FromResult(PlanApplication.Ok());
            });

        var result = await executor.ExecuteAsync("patch", (_, _) =>
            Task.FromResult(new NodeOutcome { Summary = "nothing of its own to change" }));

        applierCalls.ShouldBe(0);
        result.Tree.Node("patch").Failure.ShouldBeNull();
    }

    // ── Fixtures ──

    private static async Task<IPlanTreeStore> Seeded(PlanTree tree)
    {
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(tree).ConfigureAwait(false);
        return store;
    }

    private static PlanTree PatchTree() =>
        PlanTree.Create("patch", Contract("Add --json to the report command", "written(report.cs)"), Array.Empty<(string, AgentContract)>());

    private static PlanTree TripTree() =>
        PlanTree.Create("hakone", Contract("Put hakone on the trip", "carried(hakone)"), Array.Empty<(string, AgentContract)>());

    private static AgentContract Contract(string goal, params string[] criteria) =>
        new() { Goal = goal, AcceptanceCriteria = criteria };

    private static InvocationCheck Written(ISet<string> written) => new(
        "filesystem",
        [CheckDefinition.Of("written", 1, "the file exists", c => written.Contains(c.Argument(0)!))]);

    private static InvocationCheck Carried(ISet<string> carried) => new(
        "world",
        [CheckDefinition.Of("carried", 1, "the place is on the trip", c => carried.Contains(c.Argument(0)!))]);
}
