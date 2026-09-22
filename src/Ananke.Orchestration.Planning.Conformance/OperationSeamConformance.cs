using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Conformance;
using Shouldly;

namespace Ananke.Orchestration.Planning.Conformance;

/// <summary>
/// The conformance contract for the plan tier's operation seam — an <see cref="OperationCatalog"/>
/// and a <see cref="PlanCandidateApplier"/>, as framework-neutral scenarios.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ReferenceSeam"/> is the subject the suite is self-validated against. On a runner that
/// discovers tests by attribute, consume <c>Ananke.Orchestration.Planning.Conformance.NUnit</c>
/// instead of driving this list directly.
/// </para>
/// <para>
/// Half of what is proved here is the tier's behaviour rather than the consumer's — what never
/// reaches an applier, and what a rejection carries into the next attempt. That is deliberate: the
/// seam has no consumer inside this repository, so these scenarios are what a change to it has to
/// keep true, and a consumer runs the same list to find out whether a framework upgrade moved it.
/// </para>
/// </remarks>
public static class OperationSeamConformance
{
    private const string PlanId = "conformance";

    /// <summary>Every rule an operation seam must satisfy.</summary>
    public static IReadOnlyList<ConformanceScenario<PlanOperationSeam>> Scenarios { get; } =
    [
        // ── 1. What the catalog rules on, before anything is driven ──────────

        new("Admits_TheAdmissibleOperation_ReturnsNoFinding", seam =>
        {
            seam.Operations.Admits(seam.Admissible)
                .ShouldBeNull("the catalog refused the operation the subject offered as admissible");
            return ConformanceOutcome.Passed;
        }),

        new("Admits_TheRefusedOperation_ReturnsAFinding", seam =>
        {
            seam.Operations.Admits(seam.Refused)
                .ShouldNotBeNullOrWhiteSpace("the catalog admitted the operation the subject offered as refused");
            return ConformanceOutcome.Passed;
        }),

        new("Admits_NoOperationAtAll_ReturnsAFinding", seam =>
        {
            seam.Operations.Admits(null).ShouldNotBeNullOrWhiteSpace();
            seam.Operations.Admits(new Operation { Name = " " }).ShouldNotBeNullOrWhiteSpace();
            return ConformanceOutcome.Passed;
        }),

        new("Legend_NamesEveryOperationTheCatalogHolds", seam =>
        {
            var legend = seam.Operations.Legend();

            foreach (var definition in seam.Operations.Definitions)
                legend.ShouldContain(definition.Name);

            return ConformanceOutcome.Passed;
        }),

        // ── 2. What reaches the applier when the tier drives the seam ────────

        new("Execute_AnAdmittedProposalFromADoneStep_ReachesTheApplier", async (seam, ct) =>
        {
            var reached = await DriveAsync(seam, new NodeOutcome { Candidate = seam.Admissible, Done = true }, ct);

            reached.Steps.ShouldBeGreaterThan(0, "the step never ran, so this proves nothing");
            reached.Calls.ShouldBe(1, "an admitted proposal did not reach the applier exactly once");
            reached.Tree.Node(PlanId).Failure
                .ShouldBeNull("applying an admitted proposal was recorded as a shape failure");

            return ConformanceOutcome.Passed;
        }),

        new("Execute_AProposalTheCatalogRefuses_NeverReachesTheApplier", async (seam, ct) =>
        {
            var reached = await DriveAsync(seam, new NodeOutcome { Candidate = seam.Refused, Done = true }, ct);

            reached.Steps.ShouldBeGreaterThan(0, "the step never ran, so this proves nothing");
            reached.Calls.ShouldBe(0, "a proposal the catalog refused was handed to the applier anyway");
            return ConformanceOutcome.Passed;
        }),

        // A step's contract cannot be met while it only proposes — nothing has applied the change or
        // checked it — so a proposing step reports Done: true and a step that could not do its part
        // reports false. The tier drops the candidate of a step that reports false, before the gate
        // and before the applier, and it does so silently: no event, no rejection, no retry. A seam
        // whose steps report false will therefore never apply anything, which is a failure mode that
        // costs a live run to find and looks exactly like a model that proposed nothing.
        new("Execute_ACandidateFromAStepThatIsNotDone_NeverReachesTheApplier", async (seam, ct) =>
        {
            var reached = await DriveAsync(seam, new NodeOutcome { Candidate = seam.Admissible, Done = false }, ct);

            reached.Steps.ShouldBeGreaterThan(0, "the step never ran, so this proves nothing");
            reached.Calls.ShouldBe(0, "a candidate from a step reporting not-done reached the applier");
            return ConformanceOutcome.Passed;
        }),

        new("Execute_NoCandidateAtAll_NeverReachesTheApplier", async (seam, ct) =>
        {
            var reached = await DriveAsync(seam, new NodeOutcome { Done = true }, ct);

            reached.Steps.ShouldBeGreaterThan(0, "the step never ran, so this proves nothing");
            reached.Calls.ShouldBe(0, "a step that proposed nothing reached the applier");
            return ConformanceOutcome.Passed;
        }),

        // ── 3. What a rejection carries back ─────────────────────────────────

        // The runner is stateless between attempts and the tree remembers only settled facts, so a
        // finding that does not travel here is lost, and the next attempt repeats the mistake with
        // nothing to correct against.
        new("Execute_ARejectedApplication_CarriesItsFindingIntoTheNextAttempt", async (seam, ct) =>
        {
            const string finding = "conformance: this is the finding that must travel";

            var seen = new List<NodeRejection?>();

            var store = new InMemoryPlanTreeStore();
            await store.SaveAsync(Tree(), ct).ConfigureAwait(false);

            var executor = new PlanExecutor(
                store,
                applier: (_, _, _) => Task.FromResult(PlanApplication.Rejected(finding)),
                operations: seam.Operations);

            // The tier catches its own shape rejection and records it on the node, so this returns
            // normally however many attempts were refused. What is under test is what each attempt
            // was handed.
            await executor.ExecuteAsync(PlanId, (context, _) =>
            {
                seen.Add(context.Rejection);
                return Task.FromResult(new NodeOutcome { Candidate = seam.Admissible, Done = true });
            }, ct).ConfigureAwait(false);

            seen.Count.ShouldBeGreaterThan(1, "a rejected application was never retried");
            seen[0].ShouldBeNull("the first attempt was handed a rejection it could not have earned");
            seen[1].ShouldNotBeNull();
            seen[1]!.Finding.ShouldBe(finding, "the applier's finding did not travel verbatim");
            seen[1]!.Candidate.ShouldBe(seam.Admissible, "the rejected candidate did not travel with its finding");

            return ConformanceOutcome.Passed;
        })
    ];

    /// <summary>
    /// A seam this suite passes against, for validating a runner's wiring before pointing it at a
    /// real one.
    /// </summary>
    /// <remarks>
    /// Its world is a set of strings: <c>carry(place)</c> adds one, and the catalog refuses
    /// <c>carry</c> at any other arity and every other name. Nothing here consults the world to
    /// decide admission.
    /// </remarks>
    public static PlanOperationSeam ReferenceSeam()
    {
        var carried = new HashSet<string>(StringComparer.Ordinal);

        return new PlanOperationSeam
        {
            Operations = new OperationCatalog(
            [
                new OperationDefinition
                {
                    Name = "carry",
                    Arity = 1,
                    Description = "put a place on the trip",
                    Example = "carry(\"hakone\")"
                }
            ]),
            Applier = (candidate, _, _) =>
            {
                carried.Add(candidate.Argument(0)!);
                return Task.FromResult(PlanApplication.Ok());
            },
            Admissible = new Operation { Name = "carry", Arguments = ["hakone"] },
            Refused = new Operation { Name = "carry", Arguments = ["hakone", "by train"] }
        };
    }

    /// <summary>
    /// How many times the step ran, how many times the applier was reached, and the tree the pass
    /// left behind.
    /// </summary>
    /// <remarks>
    /// <see cref="Steps"/> exists so that "the applier was never reached" cannot pass because nothing
    /// ran. Every scenario asserting zero applier calls asserts a non-zero step count first.
    /// </remarks>
    private sealed record Reached(int Steps, int Calls, PlanTree Tree);

    /// <summary>
    /// Runs one pass over a one-node plan whose step returns <paramref name="outcome"/>, with the
    /// subject's catalog and applier wired exactly as a supervision wires them.
    /// </summary>
    /// <remarks>
    /// A proposal the catalog refuses is retried until the budget is spent, and the tier then records
    /// that on the node rather than throwing — so this returns normally in every case, and each
    /// scenario rules on the call count or the node.
    /// </remarks>
    private static async Task<Reached> DriveAsync(
        PlanOperationSeam seam, NodeOutcome outcome, CancellationToken ct)
    {
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(Tree(), ct).ConfigureAwait(false);

        var steps = 0;
        var calls = 0;

        var executor = new PlanExecutor(
            store,
            applier: (candidate, context, token) =>
            {
                calls++;
                return seam.Applier(candidate, context, token);
            },
            operations: seam.Operations);

        var result = await executor
            .ExecuteAsync(PlanId, (_, _) =>
            {
                steps++;
                return Task.FromResult(outcome);
            }, ct)
            .ConfigureAwait(false);

        return new Reached(steps, calls, result.Tree);
    }

    /// <summary>
    /// A one-node plan with one criterion nothing is wired to decide.
    /// </summary>
    /// <remarks>
    /// <b>The criterion is what makes the node run at all.</b> A node with nothing outstanding is
    /// already satisfied and the tier skips it — which would make every scenario below pass without
    /// executing anything. No verifier is supplied, so the criterion is never ruled on and the seam
    /// is the only thing deciding what happens.
    /// </remarks>
    private static PlanTree Tree() =>
        PlanTree.Create(
            PlanId,
            new AgentContract
            {
                Goal = "prove the operation seam",
                AcceptanceCriteria = ["the operation seam was exercised"]
            },
            Array.Empty<(string, AgentContract)>());
}
