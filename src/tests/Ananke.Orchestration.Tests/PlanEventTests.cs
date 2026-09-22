using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// What a plan says about itself while it runs.
/// </summary>
/// <remarks>
/// <para>
/// These are workflow events rather than a plan-shaped observer interface, and the difference is
/// the point: the runner carries them without knowing what a plan is, and a consumer reads them
/// through the stream it already reads.
/// </para>
/// <para>
/// The properties worth pinning are the ones a narrator depends on and cannot recover elsewhere —
/// what a node was <em>shown</em> before it ran, what an verifier could not decide, and why a
/// plan version was minted. Everything else about the run is answerable from the tree afterwards;
/// these three are not.
/// </para>
/// </remarks>
[TestFixture]
public class PlanEventTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);

    private sealed record Delivery
    {
        public required PlanTree Plan { get; init; }
        public PlanRunResult? Outcome { get; init; }
    }

    // ── From the executor ──

    [Test]
    public async Task Execute_ANodeStarting_ReportsWhatItIsBeingShown()
    {
        var events = await CollectAsync(store => new PlanExecutor(store, projectionTokenBudget: 20)
            .ExecuteAsync("plan", Meets));

        var started = events.OfType<PlanNodeStarted>().First(e => e.NodeId == "parse");

        started.Goal.ShouldBe("Parse");
        started.PlanId.ShouldBe("plan");
        started.PlanVersion.ShouldBe(1);
        started.ProjectedTokens.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task Execute_UnderATightProjectionBudget_ReportsWhatTheNodeDidNotSee()
    {
        // Answerable only here. Afterwards the tree keeps one reading per node, so what an earlier
        // attempt was shown is gone — and "content withheld from it" versus "content it was shown
        // and did not use" is the difference between a budget problem and a model problem.
        var tight = await CollectAsync(store => new PlanExecutor(store, projectionTokenBudget: 20)
            .ExecuteAsync("plan", Meets));

        var starved = tight.OfType<PlanNodeStarted>()
            .Where(e => !e.SawEverything)
            .ToList();

        starved.ShouldNotBeEmpty("a 20-token budget cannot show a node its whole surroundings");
        starved.ShouldAllBe(e => e.OmittedAncestors > 0 || e.OmittedRecords > 0);

        var generous = await CollectAsync(store => new PlanExecutor(store, projectionTokenBudget: 10_000)
            .ExecuteAsync("plan", Meets));

        generous.OfType<PlanNodeStarted>().ShouldAllBe(e => e.SawEverything);
    }

    [Test]
    public async Task Execute_ADisputedContract_IsReported()
    {
        // A dispute reaches the tree through DisputeAsync now — an external call, never a node's own
        // report (R26) — so it is seeded before the pass rather than returned by the runner.
        var events = await CollectAsync(async store =>
        {
            var dispute = Dispute();
            await new PlanExecutor(store).DisputeAsync("plan", "parse", dispute.Criterion, dispute.Reason)
                .ConfigureAwait(false);

            return await new PlanExecutor(store).ExecuteAsync(
                "plan", (context, _) => Task.FromResult(Answer(context, passed: true)))
                .ConfigureAwait(false);
        });

        var disputed = events.OfType<PlanNodeDisputed>().ShouldHaveSingleItem();

        disputed.NodeId.ShouldBe("parse");
        disputed.Criterion.ShouldBe("the parser round-trips");
        disputed.Reason.ShouldContain("impossible");
    }

    [Test]
    public async Task Execute_WithAVerifier_ReportsWhatNothingCouldDecide()
    {
        var events = await CollectAsync(store =>
            new PlanExecutor(store, verifier: new DeterministicVerifier([])).ExecuteAsync(
                "plan", Meets));

        var ruled = events.OfType<PlanNodeVerified>().First(e => e.NodeId == "parse");

        ruled.Outcome.ShouldBe(VerificationOutcome.Abstained);

        // The gap between what was verified and what merely looks fine is not in the tree — the
        // tree records verdicts, and an abstention is the absence of one.
        ruled.Abstained.ShouldContain("the parser round-trips");
    }

    [Test]
    public async Task Execute_AtTheEndOfEachPass_ReportsWhatThePassDid()
    {
        var events = await CollectAsync(store => new PlanExecutor(store).ExecuteAsync("plan", Meets));

        var done = events.OfType<PlanPassCompleted>().ShouldHaveSingleItem();

        done.Executed.ShouldBe(["parse", "render", "plan"]);
        done.Skipped.ShouldBeEmpty();
        done.HaltedAt.ShouldBeNull();
        done.RootOutcome.ShouldBe(ContractOutcome.Met);
    }

    [Test]
    public async Task Execute_ASatisfiedNode_IsStillSkippedRatherThanReportedStuck()
    {
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(Tree());

        var executor = new PlanExecutor(store);
        await executor.ExecuteAsync("plan", Meets);

        var sink = new CollectingSink();
        using (WorkflowEventReporting.BeginScope(sink))
            await executor.ExecuteAsync("plan", Meets);

        var done = sink.Events.OfType<PlanPassCompleted>().ShouldHaveSingleItem();

        done.Skipped.ShouldNotBeEmpty();
        done.HaltedAt.ShouldBeNull();
    }

    [Test]
    public async Task Rerule_MintingAVersion_ReportsWhyAndWhatWentWithIt()
    {
        var events = await CollectAsync(async store =>
        {
            var executor = new PlanExecutor(store);
            await executor.ReruleAsync(
                "plan", "plan", Contract("Ship it differently", "the build is green"),
                "buffering cannot work at that size",
                [new AuthoredStep { Id = "render", Contract = Contract("Render", "output matches the golden file") }]);
            return null!;
        });

        var minted = events.OfType<PlanVersionMinted>().ShouldHaveSingleItem();

        minted.PlanVersion.ShouldBe(2);
        minted.ReRuledNodeId.ShouldBe("plan");
        minted.Reason.ShouldBe("buffering cannot work at that size");

        // Dropped, not cancelled — and this is the only place the *event stream* says so.
        minted.DroppedNodeIds.ShouldBe(["parse"]);
    }

    [Test]
    public async Task Rerule_WithAVerifier_ReportsTheGatesNothingCanDecide()
    {
        // I8, made visible. A re-ruling authored in new words can come out of a change of plan gated
        // by nothing at all, because a criterion with no check is abstained rather than failed — and
        // every node still reads as fine.
        var events = await CollectAsync(async store =>
        {
            var executor = new PlanExecutor(
                store, verifier: new DeterministicVerifier([Check("the build is green")]));

            await executor.ReruleAsync(
                "plan", "plan", Contract("Ship it differently", "the build is green", "it feels right"),
                "buffering cannot work at that size",
                [new AuthoredStep { Id = "render", Contract = Contract("Render", "the reviewer is happy") }]);

            return null!;
        });

        var minted = events.OfType<PlanVersionMinted>().ShouldHaveSingleItem();

        // The re-ruled node's and the replacement child's, together: what this change of plan
        // authored is one act, and a reader deciding whether the plan is still gated does not care
        // which line of it a criterion arrived on.
        minted.CriteriaNothingCanDecide.ShouldBe(["it feels right", "the reviewer is happy"]);
    }

    [Test]
    public async Task Rerule_EveryNewGateDecidable_ReportsNoneRatherThanNothing()
    {
        var events = await CollectAsync(async store =>
        {
            var executor = new PlanExecutor(
                store, verifier: new DeterministicVerifier([Check("the build is green")]));

            await executor.ReruleAsync(
                "plan", "plan", Contract("Ship it differently", "the build is green"), "why");

            return null!;
        });

        // Empty, not null: something was asked and answered. The difference is the whole report.
        events.OfType<PlanVersionMinted>().ShouldHaveSingleItem().CriteriaNothingCanDecide.ShouldBeEmpty();
    }

    [Test]
    public async Task Rerule_WithNoVerifier_SaysNobodyCouldSay()
    {
        var events = await CollectAsync(async store =>
        {
            await new PlanExecutor(store).ReruleAsync(
                "plan", "plan", Contract("Ship it differently", "nothing can decide this"), "why");

            return null!;
        });

        // A plan with nothing configured to rule on it has nothing to say about decidability, and
        // reporting "no undecidable criteria" would be a claim nobody made.
        events.OfType<PlanVersionMinted>().ShouldHaveSingleItem().CriteriaNothingCanDecide.ShouldBeNull();
    }

    [Test]
    public async Task Rerule_AQualityCriterionNothingCanDecide_IsNotCounted()
    {
        // Gates only. A quality criterion nothing can decide narrows the score — which is visible in
        // the score — and cannot turn an unverified run into a finished-looking one. Counting both
        // would dilute the number that matters.
        var events = await CollectAsync(async store =>
        {
            var executor = new PlanExecutor(
                store, verifier: new DeterministicVerifier([Check("the build is green")]));

            await executor.ReruleAsync(
                "plan", "plan",
                Contract("Ship it differently", "the build is green") with
                {
                    QualityCriteria = ["the diff is small"]
                },
                "why");

            return null!;
        });

        events.OfType<PlanVersionMinted>().ShouldHaveSingleItem().CriteriaNothingCanDecide.ShouldBeEmpty();
    }

    [Test]
    public async Task Rerule_ChildrenGivenAsAOneShotSequence_IsReadExactlyOnce()
    {
        // The children are now asked two questions — what the new version holds, and what nothing
        // can decide about it — and a caller handing in a query is entitled to have it enumerated
        // once. The demo passes a LINQ projection, which is one `foreach` away from this.
        var reads = 0;

        IEnumerable<AuthoredStep> Once()
        {
            reads++;
            yield return new AuthoredStep { Id = "render", Contract = Contract("Render", "the reviewer is happy") };
        }

        var events = await CollectAsync(async store =>
        {
            var executor = new PlanExecutor(
                store, verifier: new DeterministicVerifier([Check("the build is green")]));

            await executor.ReruleAsync(
                "plan", "plan", Contract("Ship it differently", "the build is green"), "why", Once());

            return null!;
        });

        reads.ShouldBe(1);
        events.OfType<PlanVersionMinted>().ShouldHaveSingleItem()
            .CriteriaNothingCanDecide.ShouldBe(["the reviewer is happy"]);
    }

    // ── Identity ──

    [Test]
    public async Task Execute_InsideASupervisedJob_TheEventsNameTheWorkflowAroundThem()
    {
        var workflow = new Workflow<Delivery>("delivery")
            .Supervise("deliver", s => s.Plan, new SupervisionOptions { Runner = Meets },
                (s, r) => s with { Outcome = r })
            .Then("deliver", Workflow.End);

        var events = new List<WorkflowEvent>();
        await foreach (var evt in workflow.StreamAsync(new Delivery { Plan = Tree() }))
            events.Add(evt);

        var started = events.OfType<PlanNodeStarted>().First();

        started.WorkflowName.ShouldBe("delivery");
        started.ExecutionId.ShouldNotBeNullOrEmpty();
    }

    [Test]
    public async Task Execute_OutsideAWorkflow_ClaimsNoWorkflow()
    {
        var events = await CollectAsync(store => new PlanExecutor(store).ExecuteAsync("plan", Meets));

        var started = events.OfType<PlanNodeStarted>().First();

        // Inventing a workflow name would be worse than saying there is none. The plan is
        // identified by PlanId either way.
        started.WorkflowName.ShouldBeEmpty();
        started.ExecutionId.ShouldBeEmpty();
        started.PlanId.ShouldBe("plan");
    }

    [Test]
    public async Task Execute_WithNobodyListening_RunsExactlyTheSame()
    {
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(Tree());

        var result = await new PlanExecutor(store).ExecuteAsync("plan", Meets);

        result.RootOutcome.ShouldBe(ContractOutcome.Met);
    }

    // ── Fixtures ──

    private static async Task<List<WorkflowEvent>> CollectAsync(
        Func<IPlanTreeStore, Task<PlanRunResult>> run)
    {
        var sink = new CollectingSink();
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(Tree()).ConfigureAwait(false);

        using (WorkflowEventReporting.BeginScope(sink))
            await run(store).ConfigureAwait(false);

        return sink.Events;
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

    private static IDeterministicCheck Check(params string[] criteria) =>
        new PredicateCheck("a check", criteria, _ => true);

    private static Task<NodeOutcome> Meets(PlanNodeContext context, CancellationToken ct) =>
        Task.FromResult(Answer(context, passed: true));

    private static NodeOutcome Answer(PlanNodeContext context, bool passed) => new()
    {
        Verdicts =
        [
            .. context.Contract.AcceptanceCriteria.Select(c => new CriterionVerdict
            {
                Criterion = c, Passed = passed, Oracle = "dotnet test", At = T0
            })
        ]
    };

    private static PlanViolation Dispute() => new()
    {
        Criterion = "the parser round-trips",
        Reason = "round-tripping is impossible for the input format as specified",
        At = T0
    };

    private sealed class CollectingSink : IWorkflowEventSink
    {
        public List<WorkflowEvent> Events { get; } = [];

        public ValueTask ReportAsync(WorkflowEvent evt, CancellationToken ct = default)
        {
            Events.Add(evt);
            return ValueTask.CompletedTask;
        }
    }
}
