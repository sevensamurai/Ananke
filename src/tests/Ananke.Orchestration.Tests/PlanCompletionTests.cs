using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Running a plan to completion: passes repeat while the tree is still changing, and stop when it
/// is not.
/// </summary>
/// <remarks>
/// <para>
/// The loop used to be written by each caller, and the demo's version had no bound. That is how a
/// wrong <c>StatusOf</c> — a node that failed, was fixed and passed, but stayed <c>Failed</c> —
/// presented as the demo <em>hanging</em> rather than as a wrong status. A bounded loop does not make
/// that defect correct; it makes it visible as the wrong answer it is, which is the difference
/// between a bug you can read and a run you have to kill.
/// </para>
/// <para>
/// So the tests worth having here are the ones about <b>stopping</b>: a plan that changes nothing, a
/// plan that oscillates, a plan whose nodes have used up their declared attempts, and a plan halted
/// by a dispute. Each carries <c>CancelAfter</c>, because the failure mode under test is a run that
/// never returns.
/// </para>
/// </remarks>
[TestFixture]
public class PlanCompletionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);

    // ── Converging ──

    [Test, CancelAfter(10_000)]
    public async Task ExecuteToCompletion_WhenWorkConvergesOnASecondAttempt_StopsAtSatisfied(
        CancellationToken ct)
    {
        var store = await Seeded();
        var attempts = new Dictionary<string, int>(StringComparer.Ordinal);

        var result = await new PlanExecutor(store).ExecuteToCompletionAsync(
            "plan",
            (context, _) =>
            {
                var attempt = attempts[context.Node.Id] = attempts.GetValueOrDefault(context.Node.Id) + 1;
                return Task.FromResult(Answer(context, passed: attempt > 1));
            },
            ct);

        result.RootOutcome.ShouldBe(ContractOutcome.Met);
        result.Tree.Node("parse").ReadCount.ShouldBe(2, "one failing attempt, then the fix");
    }

    [Test, CancelAfter(10_000)]
    public async Task ExecuteToCompletion_WhenTheFirstPassSatisfiesEverything_RunsNoSecondPass(
        CancellationToken ct)
    {
        var store = await Seeded();

        var result = await new PlanExecutor(store).ExecuteToCompletionAsync(
            "plan", (context, _) => Task.FromResult(Answer(context, passed: true)), ct);

        result.RootOutcome.ShouldBe(ContractOutcome.Met);
        foreach (var nodeId in result.Tree.Current.Nodes.Keys)
            result.Tree.Node(nodeId).ReadCount.ShouldBe(1, $"{nodeId} had nothing left to do");

        // The returned result is the pass that did the work, not a no-op pass after it. A caller
        // reading Executed to say what happened would otherwise be told nothing did.
        result.Executed.ShouldBe(["parse", "render", "plan"]);
    }

    // ── Stopping ──

    [Test, CancelAfter(10_000)]
    public async Task ExecuteToCompletion_WhenAPassChangesNothing_Stops(CancellationToken ct)
    {
        // The shape that used to hang: the root never reaches Satisfied, and nothing bounds the loop.
        var store = await Seeded();
        var passes = 0;

        var result = await new PlanExecutor(store).ExecuteToCompletionAsync(
            "plan",
            (context, _) =>
            {
                passes++;
                return Task.FromResult(Answer(context, passed: false));
            },
            ct);

        result.RootOutcome.ShouldBe(ContractOutcome.Unmet);
        passes.ShouldBe(6, "three nodes over two passes — the second one proved the first had settled");
    }

    [Test, CancelAfter(10_000)]
    public async Task ExecuteToCompletion_WhenTheWorkOscillates_Stops(CancellationToken ct)
    {
        // Never two consecutive passes alike: one criterion is repaired exactly as the other breaks.
        // Comparing only against the previous pass would follow this round forever.
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(PlanTree.Create(
            "plan",
            Contract("Ship it", "the build is green", "the docs are current"),
            // Explicitly typed: `[]` cannot pick between Create's two children overloads.
            children: Array.Empty<(string Id, AgentContract Contract)>()));

        var attempt = 0;

        var result = await new PlanExecutor(store).ExecuteToCompletionAsync(
            "plan",
            (context, _) =>
            {
                var flip = ++attempt % 2 == 1;
                return Task.FromResult(new NodeOutcome
                {
                    Verdicts =
                    [
                        Verdict("the build is green", passed: flip),
                        Verdict("the docs are current", passed: !flip)
                    ]
                });
            },
            ct);

        result.RootOutcome.ShouldBe(ContractOutcome.Unmet);
        attempt.ShouldBe(3, "the third pass returned to the state the first one left");
    }

    [Test, CancelAfter(10_000)]
    public async Task ExecuteToCompletion_APlanThatSettles_ReportsNoHalt(CancellationToken ct)
    {
        // The other half: nothing is invented for a run that finished its work.
        var store = await Seeded();

        var result = await new PlanExecutor(store).ExecuteToCompletionAsync(
            "plan", (context, _) => Task.FromResult(Answer(context, passed: true)), ct);

        result.RootOutcome.ShouldBe(ContractOutcome.Met);
        result.HaltedAt.ShouldBeNull();
    }

    [Test, CancelAfter(10_000)]
    public async Task ExecuteToCompletion_ADisputeHaltsTheRun_AndIsReturnedUnresolved(
        CancellationToken ct)
    {
        var store = await Seeded();

        // A dispute reaches the tree through DisputeAsync now — an external call, never a node's own
        // report (R26) — so it is seeded before the run rather than returned by the runner.
        var dispute = Dispute();
        await new PlanExecutor(store).DisputeAsync("plan", "parse", dispute.Criterion, dispute.Reason, ct);

        var calls = 0;

        var result = await new PlanExecutor(store).ExecuteToCompletionAsync(
            "plan",
            (context, _) =>
            {
                calls++;
                return Task.FromResult(Answer(context, passed: true));
            },
            ct);

        result.HaltedAt.ShouldBe("parse");
        result.RootOutcome.ShouldBe(ContractOutcome.Disputed);
        calls.ShouldBe(1, "the pass stopped at the dispute, and repeating it would only re-report it");
    }

    [Test, CancelAfter(10_000)]
    public async Task ExecuteToCompletion_APlanVersionMintedMidRun_CountsAsProgress(
        CancellationToken ct)
    {
        // Constructed to isolate the plan version as the *only* thing that moved: `render` never
        // reports anything, so it holds no verdicts, and re-ruling it with the contract it already
        // has leaves every status and every verdict exactly where they were. A new version is a
        // statement that the plan changed, so the run gets another pass to act on it — stopping
        // there would end the run on the strength of work done under a plan that no longer applies.
        var renderContract = Contract("Render", "output matches the golden file");

        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(PlanTree.Create(
            "plan",
            Contract("Ship it", "the build is green"),
            [
                ("parse", Contract("Parse", "the parser round-trips")),
                ("render", renderContract)
            ]));

        var executor = new PlanExecutor(store);
        var passes = 0;

        var result = await executor.ExecuteToCompletionAsync(
            "plan",
            async (context, token) =>
            {
                if (context.Node.Id == "render")
                    return NodeOutcome.Nothing;

                // The root runs last, so this counts completed passes.
                if (context.Node.Id == "plan" && ++passes == 2)
                {
                    await executor.ReruleAsync(
                        "plan", "render", renderContract, "restated, otherwise unchanged", ct: token);
                }

                return Answer(context, passed: false);
            },
            ct);

        result.Tree.Lineage.Count.ShouldBe(2);
        passes.ShouldBe(3, "the version minted on pass two bought a third pass, which settled");
    }

    // ── What it must not do ──

    [Test, CancelAfter(10_000)]
    public async Task ExecuteToCompletion_MintsNoPlanVersion(CancellationToken ct)
    {
        var store = await Seeded();

        var result = await new PlanExecutor(store).ExecuteToCompletionAsync(
            "plan", (context, _) => Task.FromResult(Answer(context, passed: false)), ct);

        // Deciding the plan should change belongs to whoever authored the criterion. Repeating
        // passes is not a decision about the plan, so it leaves the lineage alone.
        result.Tree.Lineage.Count.ShouldBe(1);
    }

    [Test, CancelAfter(10_000)]
    public async Task Execute_IsStillOnePass(CancellationToken ct)
    {
        // The single-pass entry point stays public: a caller that re-rules, asks a person or runs a
        // build between passes needs them separated.
        var store = await Seeded();
        var attempts = new Dictionary<string, int>(StringComparer.Ordinal);

        var result = await new PlanExecutor(store).ExecuteAsync(
            "plan",
            (context, _) =>
            {
                var attempt = attempts[context.Node.Id] = attempts.GetValueOrDefault(context.Node.Id) + 1;
                return Task.FromResult(Answer(context, passed: attempt > 1));
            },
            ct);

        result.RootOutcome.ShouldBe(ContractOutcome.Unmet);
        result.Tree.Node("parse").ReadCount.ShouldBe(1);
    }

    // ── Helpers ──

    private static async Task<IPlanTreeStore> Seeded()
    {
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(PlanTree.Create(
            "plan",
            Contract("Ship it", "the build is green"),
            [
                ("parse", Contract("Parse", "the parser round-trips")),
                ("render", Contract("Render", "output matches the golden file"))
            ])).ConfigureAwait(false);
        return store;
    }

    private static AgentContract Contract(string goal, params string[] criteria) =>
        new() { Goal = goal, AcceptanceCriteria = criteria };

    private static NodeOutcome Answer(PlanNodeContext context, bool passed) => new()
    {
        Verdicts = [.. context.Contract.AcceptanceCriteria.Select(c => Verdict(c, passed))]
    };

    private static CriterionVerdict Verdict(string criterion, bool passed) =>
        new() { Criterion = criterion, Passed = passed, Oracle = "dotnet test", At = T0 };

    private static PlanViolation Dispute() => new()
    {
        Criterion = "the parser round-trips",
        Reason = "round-tripping is impossible for the input format as specified",
        At = T0
    };
}
