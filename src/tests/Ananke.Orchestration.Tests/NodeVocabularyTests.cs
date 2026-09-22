using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// The two axes a node is read on, and the one word a run ends with.
/// </summary>
/// <remarks>
/// <para>
/// <b>What these pin is a distinction one enum used to collapse.</b> A single status returned
/// <c>Failed</c> both for an attempt that threw and for a criterion that did not hold, and
/// <c>Pending</c> both for a node nobody had started and for one that ran and decided nothing — so
/// the two questions a reader actually asks, <em>what happened when we tried this</em> and
/// <em>does the contract hold</em>, had one answer between them.
/// </para>
/// <para>
/// <b>And one of the five values is stored rather than derived</b>, which is the only place this
/// design keeps a fact about now. That buys a reconciliation obligation, and the test for it is the
/// one that matters: a run that dies mid-step must not leave a node reading <c>Running</c> forever.
/// </para>
/// </remarks>
[TestFixture]
public class NodeVocabularyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);

    // ── The lifecycle ──

    [Test]
    public async Task LifecycleOf_ANodeBeingAttempted_ReadsRunningFromTheStore()
    {
        // Read through the store, not through a local, because that is the whole use: somebody
        // looking at a plan that has been paused for days is not holding the executor's variables.
        var store = await Seeded();
        NodeLifecycle? seen = null;

        await new PlanExecutor(store).ExecuteAsync("plan", async (ctx, ct) =>
        {
            if (ctx.Node.Id == "parse")
                seen = (await store.LoadAsync("plan", ct))!.LifecycleOf("parse");

            return Meets(ctx);
        });

        seen.ShouldBe(NodeLifecycle.Running);
    }

    [Test]
    public async Task LifecycleOf_ARunThatDiedMidStep_ReadsPlannedAgainOnTheNextPass()
    {
        // The obligation a stored fact comes with. Nothing will ever clear a mark left by a process
        // that is gone, so the next pass corrects it rather than inheriting it — and a node reading
        // Running forever would be worse than one that never reported it at all.
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(Tree().WithAttempt("render", T0));

        (await store.LoadAsync("plan"))!.LifecycleOf("render").ShouldBe(NodeLifecycle.Running);

        // Halts at 'parse', so the pass never reaches 'render' itself.
        await new PlanExecutor(store).ExecuteAsync("plan", (ctx, _) =>
            ctx.Node.Id == "parse"
                ? throw new InvalidOperationException("the process went away")
                : Task.FromResult(Meets(ctx)));

        (await store.LoadAsync("plan"))!.LifecycleOf("render").ShouldBe(NodeLifecycle.Planned);
    }

    [Test]
    public async Task LifecycleOf_AnAttemptThatThrew_IsFaultedRatherThanRunning()
    {
        var store = await Seeded();

        await new PlanExecutor(store).ExecuteAsync("plan", (ctx, _) =>
            ctx.Node.Id == "parse"
                ? throw new InvalidOperationException("429 Too Many Requests")
                : Task.FromResult(Meets(ctx)));

        var tree = (await store.LoadAsync("plan"))!;
        tree.LifecycleOf("parse").ShouldBe(NodeLifecycle.Faulted);
        tree.OutcomeOf("parse").ShouldBe(ContractOutcome.Unmet);
    }

    [Test]
    public void LifecycleOf_AParentWhoseChildrenHaveRun_IsStillItsOwnAttempt()
    {
        // Not aggregated upward, deliberately: "has this node been attempted" is a question about
        // this node, and rolling children into it would quietly answer a different one.
        var tree = Tree().WithReading("parse", Reading());

        tree.LifecycleOf("parse").ShouldBe(NodeLifecycle.Completed);
        tree.LifecycleOf("plan").ShouldBe(NodeLifecycle.Planned);
    }

    // ── The outcome, and where it parts company with the lifecycle ──

    [Test]
    public void Settled_ANodeWhoseCriteriaHoldButWhoseLastAttemptDied_IsNotSettled()
    {
        // Both halves of Settled, and the second is not redundant: the verdicts describe what was
        // true before something went wrong, and skipping on them would report a run as finished on
        // evidence that predates its own failure.
        var tree = Tree()
            .WithVerdict("parse", Verdict("the parser round-trips", passed: true))
            .WithFailure("parse", new NodeFailure { Message = "the tool died", At = T0 });

        tree.OutcomeOf("parse").ShouldBe(ContractOutcome.Met);
        tree.LifecycleOf("parse").ShouldBe(NodeLifecycle.Faulted);
        tree.Settled("parse").ShouldBeFalse();
    }

    [Test]
    public async Task Execute_ANodeWhoseCriteriaHoldButWhoseLastAttemptDied_IsRunAgain()
    {
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(Tree()
            .WithVerdict("parse", Verdict("the parser round-trips", passed: true))
            .WithFailure("parse", new NodeFailure { Message = "the tool died", At = T0 }));

        var result = await new PlanExecutor(store).ExecuteAsync(
            "plan", (ctx, _) => Task.FromResult(Meets(ctx)));

        result.Executed.ShouldContain("parse");
        result.Skipped.ShouldNotContain("parse");
    }

    // ── How a run ended ──

    [Test]
    public async Task Outcome_EveryContractMet_IsCompleted()
    {
        var store = await Seeded();

        var result = await new PlanExecutor(store).ExecuteToCompletionAsync(
            "plan", (ctx, _) => Task.FromResult(Meets(ctx)));

        result.Outcome.ShouldBe(PlanRunOutcome.Completed);
        result.RootOutcome.ShouldBe(ContractOutcome.Met);
    }

    [Test]
    public async Task Outcome_AnAttemptThatDied_IsFaulted()
    {
        var store = await Seeded();

        var result = await new PlanExecutor(store).ExecuteAsync("plan", (ctx, _) =>
            ctx.Node.Id == "parse"
                ? throw new InvalidOperationException("the provider is down")
                : Task.FromResult(Meets(ctx)));

        result.Outcome.ShouldBe(PlanRunOutcome.Faulted);
    }

    [Test]
    public async Task Outcome_AStandingDispute_IsBlockedAndTheRecordSaysWhy()
    {
        // A dispute reaches the tree through DisputeAsync now — an external call, never a node's own
        // report (R26), so there is only one way left to raise one through this executor.
        var store = await Seeded();
        await new PlanExecutor(store).DisputeAsync(
            "plan", "parse", "the parser round-trips",
            "round-tripping is impossible for the input format as specified");

        var result = await new PlanExecutor(store).ExecuteAsync("plan", (ctx, _) =>
            ctx.Node.Id == "parse" ? Task.FromResult(NodeOutcome.Nothing) : Task.FromResult(Meets(ctx)));

        result.Outcome.ShouldBe(PlanRunOutcome.Blocked);
        result.Tree.Node("parse").Violation!.Reason.ShouldContain("impossible");
    }

    // ── Fixtures ──

    private static async Task<IPlanTreeStore> Seeded()
    {
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(Tree()).ConfigureAwait(false);
        return store;
    }

    private static PlanTree Tree() => PlanTree.Create(
        "plan",
        Contract("Ship it", "the build is green"),
        [
            ("parse", Contract("Parse", "the parser round-trips")),
            ("render", Contract("Render", "output matches the golden file"))
        ]);

    private static AgentContract Contract(string goal, params string[] criteria) =>
        new() { Goal = goal, AcceptanceCriteria = criteria };

    private static NodeOutcome Meets(PlanNodeContext ctx) => new()
    {
        Verdicts = [.. ctx.Contract.AcceptanceCriteria.Select(c => Verdict(c, passed: true))]
    };

    private static NodeOutcome Fails(PlanNodeContext ctx) => new()
    {
        Verdicts = [.. ctx.Contract.AcceptanceCriteria.Select(c => Verdict(c, passed: false))]
    };

    private static CriterionVerdict Verdict(string criterion, bool passed) =>
        new() { Criterion = criterion, Passed = passed, Oracle = "dotnet test", At = T0 };

    private static NodeReading Reading() => new()
    {
        At = T0,
        PlanVersion = 1,
        ProjectedTokens = 10
    };
}
