using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Tracing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Giving a step up: a state on the step, never an edit to anybody's child list.
/// </summary>
/// <remarks>
/// <para>
/// <b>These exist because the alternative deleted finished work.</b> Before this, the only way to say
/// <em>give this one thing up</em> was to re-author the parent's whole child list — which drops by
/// omission. Asked about one over-full day, a live supervisor chose <em>drop a highlight</em> and
/// destroyed six completed steps it had simply not re-listed, and the run reported every one of them
/// as though the feature were working.
/// </para>
/// <para>
/// So the first test here is the one that matters: <b>nothing is removed.</b> The rest describe what
/// the plan does around a step nobody is going to do.
/// </para>
/// </remarks>
[TestFixture]
public class AbandonmentTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public void Abandon_RemovesNothingAndMintsNoVersion()
    {
        var before = Booked();
        var after = before.Abandon("hakone", Given());

        // The node is still here, its siblings are still here, and the parent still names all three.
        after.Current.Nodes.Keys.Order(StringComparer.Ordinal)
            .ShouldBe(["hakone", "kyoto", "tokyo", "trip"]);
        after.Node("trip").ChildIds.ShouldBe(["tokyo", "hakone", "kyoto"]);
        after.Current.DroppedNodeIds.ShouldBeEmpty();

        // A version marks the plan being re-authored. Nobody re-authored anything.
        after.Lineage.Count.ShouldBe(before.Lineage.Count);
    }

    [Test]
    public void Abandon_KeepsTheWorkTheStepAlreadyDid()
    {
        // It is not a reset and not a retraction: what the step managed is still what happened, and
        // a reader asking how much was already done gets the same answer afterwards.
        var after = Booked().Abandon("tokyo", Given());

        after.Node("tokyo").Verdicts.ShouldNotBeEmpty();
        after.Node("tokyo").Abandonment.ShouldNotBeNull().By.ShouldBe("a person");
    }

    [Test]
    public void LifecycleOf_AnAbandonedNode_IsAbandonedWhateverItDidBefore()
    {
        var after = Booked().Abandon("tokyo", Given());

        after.LifecycleOf("tokyo").ShouldBe(NodeLifecycle.Abandoned);
        after.Settled("tokyo").ShouldBeTrue();
    }

    [Test]
    public void OutcomeOf_AParentStopsWaitingOnAnAbandonedChild()
    {
        // The abandoned node's own outcome stays honest — its contract is not met — and the parent
        // does not stay unmet because of work somebody decided not to do.
        var after = Booked()
            .WithVerdict("kyoto", Verdict("nights(kyoto, 5)"))
            .WithVerdict("trip", Verdict("within_duration()"))
            .Abandon("hakone", Given());

        after.OutcomeOf("hakone").ShouldBe(ContractOutcome.Unmet);
        after.OutcomeOf("trip").ShouldBe(ContractOutcome.Met);
    }

    [Test]
    public void OutcomeOf_AnAbandonedChildsDisputeStopsTravellingUpward()
    {
        // A dispute is a claim that a contract is wrong, and it climbs. Once nobody is going to do
        // the work, there is no contract left to be wrong about.
        var disputed = Booked().WithViolation("hakone", new PlanViolation
        {
            Criterion = "nights(hakone, 3)",
            Reason = "there is one room and the trip needs three nights",
            At = T0
        });

        disputed.OutcomeOf("trip").ShouldBe(ContractOutcome.Disputed);
        disputed.Abandon("hakone", Given()).OutcomeOf("trip").ShouldNotBe(ContractOutcome.Disputed);
    }

    [Test]
    public async Task Execute_AnAbandonedNode_IsNotAttempted()
    {
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(Booked().Abandon("hakone", Given()));

        var seen = new List<string>();

        var result = await new PlanExecutor(store).ExecuteAsync("trip", (ctx, _) =>
        {
            seen.Add(ctx.Node.Id);
            return Task.FromResult(Meets(ctx));
        });

        seen.ShouldNotContain("hakone");
        result.Skipped.ShouldContain("hakone");
    }

    [Test]
    public async Task Abandon_ReportsWhichStepWentAndWhy_SoSomethingCanReleaseWhatItHeld()
    {
        // No version is minted, so without the event the only trace of a decision somebody actually
        // took would be a field on a node nobody thought to read. It is also the seam a consumer
        // releases from: the tier says which step, and what that frees is the domain's to know.
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(Booked());

        var reported = new List<PlanStepAbandoned>();
        using var sink = WorkflowEventReporting.BeginScope(new Collecting(reported));

        await new PlanExecutor(store).AbandonAsync(
            "trip", "hakone", "one room, and the trip needs three nights", "a person");

        var evt = reported.ShouldHaveSingleItem();
        evt.NodeId.ShouldBe("hakone");
        evt.By.ShouldBe("a person");
        evt.Reason.ShouldContain("one room");
    }

    [Test]
    public void PlanQuestion_HasThreeAnswers_AndNothingHasToParseToTellThemApart()
    {
        // R32, and the reason it is a shape rather than a string: picking, saying something else and
        // cancelling do three different things — applied, sent back to the supervisor, ends the run —
        // so whatever reads the answer has to discern them by type, never by inspecting prose.
        // This replaces a test that pinned two *refusals*; giving a step up is an authored option now.
        var offered = new PlanOption { Summary = "take the bullet train", Answer = "bullet train" };
        var question = new PlanQuestion { Options = [offered] };

        question.Outstanding.ShouldBeTrue();

        var picked = question with { Picked = 1 };
        var said = question with { Said = "keep Naoshima, find the slack elsewhere" };
        var cancelled = question with { Refused = PlanRefusal.Cancel };

        // Answered, all three — nothing may move on any of them.
        picked.Outstanding.ShouldBeFalse();
        said.Outstanding.ShouldBeFalse();
        cancelled.Outstanding.ShouldBeFalse();

        // And each is only itself: no answer is reachable by reading another one's field.
        picked.Chosen.ShouldBe(offered);
        picked.Said.ShouldBeNull();
        picked.Refused.ShouldBeNull();

        said.Chosen.ShouldBeNull();
        said.Refused.ShouldBeNull();
        said.Said.ShouldBe("keep Naoshima, find the slack elsewhere");

        cancelled.Chosen.ShouldBeNull();
        cancelled.Said.ShouldBeNull();
    }

    // ── The delegate coordinator path (F1, F2): it must apply a give-up, not just report it ──

    // R30: GiveUp is no longer a decision shape a coordinator returns — the delegate calls
    // AbandonAsync itself, the same door PlanChoiceJob now uses, and hands back Ask with nothing
    // further to offer (there is nothing left to decide).

    [Test]
    public async Task DelegateCoordinator_GivingAStepUp_AbandonsItInTheStore()
    {
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(Booked());
        var supervision = new SupervisionOptions { Store = store };

        var job = new PlanSupervisorJob<CoordinationState>(
            "coordinate", supervision,
            async (coordination, ct) =>
            {
                await supervision.AbandonAsync(
                    coordination.Result.Tree.PlanId, "hakone", "no room available", "a person", ct);
                return PlanDecision.Ask([]);
            },
            s => s.Coordination, (s, c) => s with { Coordination = c });

        await job.ExecuteAsync(new CoordinationState { Coordination = HaltedOn("hakone") });

        var saved = (await store.LoadAsync("trip")).ShouldNotBeNull();
        saved.Node("hakone").Abandonment.ShouldNotBeNull().By.ShouldBe("a person");
        saved.LifecycleOf("hakone").ShouldBe(NodeLifecycle.Abandoned);
    }

    [Test]
    public async Task DelegateCoordinator_GivingAStepUp_SpendsNoChangeOfPlan()
    {
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(Booked());
        var supervision = new SupervisionOptions { Store = store };

        var job = new PlanSupervisorJob<CoordinationState>(
            "coordinate", supervision,
            async (coordination, ct) =>
            {
                await supervision.AbandonAsync(
                    coordination.Result.Tree.PlanId, "hakone", "no room available", "a person", ct);
                return PlanDecision.Ask([]);
            },
            s => s.Coordination, (s, c) => s with { Coordination = c });

        var after = await job.ExecuteAsync(new CoordinationState { Coordination = HaltedOn("hakone") });

        after.Coordination!.Changes.ShouldBe(0);
    }

    [TestCaseSource(nameof(DecisionsAndEscalation))]
    public async Task BothCoordinatorPaths_ChargeTheSameForTheSameDecision(PlanDecision decision, bool escalated)
    {
        var expected = PlanDecision.Spends(decision, escalated) ? 1 : 0;

        (await DelegatePathChangesAsync(decision, escalated)).ShouldBe(expected);
        (await JobPathChangesAsync(decision, escalated)).ShouldBe(expected);
    }

    // ── Fixtures ──

    /// <summary>A trip with three legs, the first of them booked.</summary>
    private static PlanTree Booked() => PlanTree.Create(
        "trip",
        Contract("Two weeks in Japan", "within_duration()"),
        [
            ("tokyo", Contract("Four nights in Tokyo", "nights(tokyo, 4)")),
            ("hakone", Contract("Three nights in Hakone", "nights(hakone, 3)")),
            ("kyoto", Contract("Five nights in Kyoto", "nights(kyoto, 5)"))
        ]).WithVerdict("tokyo", Verdict("nights(tokyo, 4)"));

    private static PlanAbandonment Given() => new()
    {
        Reason = "none of the alternatives was acceptable",
        By = "a person",
        At = T0
    };

    private static AgentContract Contract(string goal, params string[] criteria) =>
        new() { Goal = goal, AcceptanceCriteria = criteria };

    private static CriterionVerdict Verdict(string criterion) =>
        new() { Criterion = criterion, Passed = true, Oracle = "the itinerary", At = T0 };

    private static NodeOutcome Meets(PlanNodeContext ctx) => new()
    {
        Verdicts = [.. ctx.Contract.AcceptanceCriteria.Select(Verdict)]
    };

    private sealed record CoordinationState
    {
        public PlanCoordination? Coordination { get; init; }
    }

    private static PlanCoordination HaltedOn(string nodeId) => new()
    {
        Result = new PlanRunResult
        {
            Tree = Booked(),
            Executed = [nodeId],
            Skipped = [],
            Rulings = new Dictionary<string, Verification>(),
            HaltedAt = nodeId
        }
    };

    private static IEnumerable<TestCaseData> DecisionsAndEscalation()
    {
        (string Label, PlanDecision Decision)[] shapes =
        [
            ("Replan", PlanDecision.Replan(Contract("Three nights in Hakone, revised", "nights(hakone, 3)"))),
            ("Ask", PlanDecision.Ask([]))
        ];

        foreach (var (label, decision) in shapes)
            foreach (var escalated in new[] { false, true })
                yield return new TestCaseData(decision, escalated)
                    .SetName($"BothCoordinatorPaths_{label}_Escalated{escalated}");
    }

    private static async Task<int> DelegatePathChangesAsync(PlanDecision decision, bool escalated)
    {
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(Booked()).ConfigureAwait(false);
        var supervision = new SupervisionOptions { Store = store };

        var job = new PlanSupervisorJob<CoordinationState>(
            "coordinate", supervision, (_, _) => Task.FromResult(decision),
            s => s.Coordination, (s, c) => s with { Coordination = c });

        return await RunWithEscalationAsync(job, escalated).ConfigureAwait(false);
    }

    private static async Task<int> JobPathChangesAsync(PlanDecision decision, bool escalated)
    {
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(Booked()).ConfigureAwait(false);
        var supervision = new SupervisionOptions { Store = store };

        var inner = new FixedDecisionJob(
            decision, supervision, s => s.Coordination, (s, c) => s with { Coordination = c });
        var job = new AccountedSupervisorJob<CoordinationState>(
            inner, s => s.Coordination, (s, c) => s with { Coordination = c });

        return await RunWithEscalationAsync(job, escalated).ConfigureAwait(false);
    }

    private static async Task<int> RunWithEscalationAsync(IJob<CoordinationState> job, bool escalated)
    {
        var previous = WorkflowTraceContext.Value;

        try
        {
            WorkflowTraceContext.Value = escalated
                ? new TraceInfo("workflow", "execution", ResumedInto: true)
                : null;

            var after = await job.ExecuteAsync(new CoordinationState { Coordination = HaltedOn("hakone") })
                .ConfigureAwait(false);
            return after.Coordination!.Changes;
        }
        finally
        {
            WorkflowTraceContext.Value = previous;
        }
    }

    /// <summary>A stand-in for a job-based coordinator: decides, applies, and nothing more — exactly
    /// what <c>AccountedSupervisorJob</c> expects to wrap.</summary>
    private sealed class FixedDecisionJob(
        PlanDecision decision,
        SupervisionOptions supervision,
        Func<CoordinationState, PlanCoordination?> read,
        Func<CoordinationState, PlanCoordination, CoordinationState> write) : IJob<CoordinationState>
    {
        public string Name => "choose";

        public async Task<CoordinationState> ExecuteAsync(CoordinationState state, CancellationToken ct = default)
        {
            var coordination = read(state)!;

            if (decision is PlanDecision.ReplanPlan replan)
            {
                await supervision.ReruleAsync(
                    coordination.Result.Tree.PlanId,
                    replan.NodeId ?? coordination.Result.HaltedAt!,
                    replan.Contract!,
                    coordination.HaltReason ?? "halted",
                    replan.Children,
                    replan.Rationale,
                    ct).ConfigureAwait(false);
            }

            return write(state, coordination with { Decision = decision });
        }
    }

    private sealed class Collecting(List<PlanStepAbandoned> into) : IWorkflowEventSink
    {
        public ValueTask ReportAsync(WorkflowEvent evt, CancellationToken ct = default)
        {
            if (evt is PlanStepAbandoned abandoned)
                into.Add(abandoned);

            return ValueTask.CompletedTask;
        }
    }
}
