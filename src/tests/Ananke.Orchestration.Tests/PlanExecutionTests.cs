using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Streaming;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Running a plan: children in order, verdicts written back, and nodes reading the tree rather than
/// receiving handoffs.
/// </summary>
/// <remarks>
/// <para>
/// The claim under test is that <b>a loss becomes recoverable rather than destructive</b>. What a
/// handoff failed to carry was gone; what a node failed to read is still in the tree. The tests that
/// matter here are the ones that would fail if the executor started carrying results between nodes
/// in a local variable — which would look identical from the outside on a clean run, and differ
/// entirely the first time one crashed.
/// </para>
/// <para>
/// A node that reads its context <em>and still loses information</em> is the interesting failure
/// mode, and it is not one these tests can reach: it needs a real model. What they can pin is that
/// the information was there to be read.
/// </para>
/// </remarks>
[TestFixture]
public class PlanExecutionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 24, 9, 0, 0, TimeSpan.Zero);

    // ── Order and write-back ──

    [Test]
    public async Task Execute_RunsChildrenInOrderAndTheParentLast()
    {
        var store = await Seeded();
        var seen = new List<string>();

        var result = await new PlanExecutor(store).ExecuteAsync("plan", (ctx, _) =>
        {
            seen.Add(ctx.Node.Id);
            return Task.FromResult(Meets(ctx));
        });

        // Post-order: no leaf-level check proves a commitment made higher up still holds, so the
        // parent's own criteria are decided after its children have run.
        seen.ShouldBe(["parse", "render", "plan"]);
        result.RootOutcome.ShouldBe(ContractOutcome.Met);
    }

    [Test]
    public async Task Execute_WritesEveryVerdictBackIntoTheStore()
    {
        var store = await Seeded();

        await new PlanExecutor(store).ExecuteAsync("plan", (ctx, _) => Task.FromResult(Meets(ctx)));

        var reloaded = await store.LoadAsync("plan");
        reloaded.ShouldNotBeNull();
        reloaded.Node("parse").Verdicts.ShouldNotBeEmpty();
        reloaded.OutcomeOf("plan").ShouldBe(ContractOutcome.Met);
    }

    [Test]
    public async Task Execute_RecordingVerdicts_MintsNoNewVersion()
    {
        var store = await Seeded();

        var result = await new PlanExecutor(store)
            .ExecuteAsync("plan", (ctx, _) => Task.FromResult(Meets(ctx)));

        result.Tree.Lineage.Count.ShouldBe(1);
    }

    // ── Nothing crosses a node boundary ──

    [Test]
    public async Task Execute_ALaterNode_ReadsAnEarlierNodesVerdictFromTheTree()
    {
        var store = await Seeded();
        var viewsByNode = new Dictionary<string, string>(StringComparer.Ordinal);

        await new PlanExecutor(store).ExecuteAsync("plan", (ctx, _) =>
        {
            viewsByNode[ctx.Node.Id] = ctx.TreeView;
            return Task.FromResult(Meets(ctx));
        });

        // The second child was never handed anything by the first. It read what the first wrote.
        viewsByNode["render"].ShouldContain("the parser round-trips");
        viewsByNode["render"].ShouldContain("[parse]");

        // And the first could not have read the second — nothing had been established yet.
        viewsByNode["parse"].ShouldNotContain("[render]");
    }

    [Test]
    public async Task Execute_ANodesContext_CarriesNoSiblingResult()
    {
        // Stated as a test because it is the property that would silently rot: adding a
        // "PreviousOutcome" convenience to the context would make every test above still pass while
        // turning the design back into a handoff.
        var store = await Seeded();
        var contexts = new List<PlanNodeContext>();

        await new PlanExecutor(store).ExecuteAsync("plan", (ctx, _) =>
        {
            contexts.Add(ctx);
            return Task.FromResult(Meets(ctx));
        });

        // An exact list, not a "does not contain" — the point is that anything added here has to
        // be argued for. `PlanId` and `PlanVersion` earned their place by being facts about the
        // tree this node is reading, which is also what the tree records against it afterwards.
        // A property naming another node's output would not. `Rejection` (E6) is this same node's
        // own prior attempt, not a handoff from anywhere else — the executor is stateless between
        // attempts by design, so it is the only way a mechanical retry can see what it said last time.
        typeof(PlanNodeContext).GetProperties()
            .Select(p => p.Name)
            .ShouldBe(
                ["PlanId", "PlanVersion", "Node", "Contract", "TreeView", "Projection", "Rejection"],
                ignoreOrder: true);

        contexts.ShouldAllBe(c => c.Node.Verdicts.Count == 0);
    }

    /// <summary>
    /// The store really is the channel, not a copy the executor keeps in a local.
    /// </summary>
    /// <remarks>
    /// This is the test that was missing when the suite was first written, and its absence showed:
    /// replacing the executor's re-read with "keep the tree we already have" passed every other test
    /// here. Both look identical while the executor is the only writer — and diverge the moment
    /// anything else writes, which is exactly what an external verifier recording a verdict does.
    /// </remarks>
    [Test]
    public async Task Execute_AVerdictWrittenByAnyoneElseWhileANodeRuns_IsNotOverwritten()
    {
        var store = await Seeded();

        await new PlanExecutor(store).ExecuteAsync("plan", async (ctx, ct) =>
        {
            if (ctx.Node.Id == "parse")
            {
                // Something outside this node writes into the tree while the node is working.
                var live = await store.LoadAsync("plan", ct).ConfigureAwait(false);
                await store.SaveAsync(
                    live!.WithVerdict("render", Verdict(OutOfBand, passed: true)),
                    ct).ConfigureAwait(false);
            }

            return Meets(ctx);
        });

        var reloaded = await store.LoadAsync("plan");

        // The criterion is one no runner here ever produces, so its survival can only mean the
        // executor applied its own outcome on top of what the store actually held. Asserting merely
        // that render has *some* verdict proves nothing — render produces one itself.
        reloaded!.Node("render").Verdicts.Select(v => v.Criterion).ShouldContain(OutOfBand);
        reloaded.Node("parse").Verdicts.ShouldNotBeEmpty();
    }

    private const string OutOfBand = "checked by an observer while another node was running";

    /// <summary>
    /// The recoverability claim, made concrete: a run that dies halfway loses nothing, because what
    /// it recorded was in the tree and not in its own memory.
    /// </summary>
    [Test]
    public async Task Execute_AfterACrashMidRun_ResumesWithoutRedoingSatisfiedWork()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var crashed = new FilePlanTreeStore(root);
            await crashed.SaveAsync(Tree());

            // First pass: the second child throws. It no longer takes the run with it — the pass
            // stops there and records the failure — but the point of this test is unchanged: what
            // "parse" established is on disk, and nothing in memory carries it forward.
            var died = await new PlanExecutor(crashed).ExecuteAsync("plan", (ctx, _) =>
                ctx.Node.Id == "render"
                    ? throw new InvalidOperationException("the process died here")
                    : Task.FromResult(Meets(ctx)));

            died.HaltedAt.ShouldBe("render");
            died.Tree.Node("render").Failure.ShouldNotBeNull();

            // A different store instance, reading the same files — nothing carried over in memory.
            var resumed = new FilePlanTreeStore(root);
            var seen = new List<string>();

            var result = await new PlanExecutor(resumed).ExecuteAsync("plan", (ctx, _) =>
            {
                seen.Add(ctx.Node.Id);
                return Task.FromResult(Meets(ctx));
            });

            seen.ShouldBe(["render", "plan"]);        // "parse" was not redone
            result.Skipped.ShouldContain("parse");
            result.RootOutcome.ShouldBe(ContractOutcome.Met);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task FileStore_RoundTripsAWholeLineage()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var store = new FilePlanTreeStore(root);
            await store.SaveAsync(Tree()
                .WithVerdict("parse", Verdict("the parser round-trips", passed: true))
                .Rerule("render", Contract("Render, streaming"), "output no longer fits in memory"));

            var reloaded = await store.LoadAsync("plan");

            reloaded.ShouldNotBeNull();
            reloaded.Lineage.Count.ShouldBe(2);
            reloaded.Current.Reason.ShouldNotBeNull();
            reloaded.Current.Reason.ShouldContain("no longer fits");
            reloaded.Node("parse").Verdicts.Count.ShouldBe(1);
            reloaded.Node("render").Contract.Goal.ShouldBe("Render, streaming");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── A contradiction stops the pass and is returned, not resolved ──

    [Test]
    public async Task Execute_AStandingDispute_HaltsThePassWithoutRerulingAnything()
    {
        // A dispute reaches the tree through DisputeAsync now — an external call, never a node's own
        // report (R26) — so it is seeded before the pass rather than returned by the runner.
        var store = await Seeded();
        var dispute = Dispute();
        await new PlanExecutor(store).DisputeAsync("plan", "parse", dispute.Criterion, dispute.Reason);

        var result = await new PlanExecutor(store).ExecuteAsync("plan", (ctx, _) => Task.FromResult(Meets(ctx)));

        result.HaltedAt.ShouldBe("parse");
        result.Executed.ShouldBe(["parse"]);          // "render" and "plan" never ran
        result.RootOutcome.ShouldBe(ContractOutcome.Disputed);

        // The executor has no opinion on whether the plan should change.
        result.Tree.Lineage.Count.ShouldBe(1);
    }

    // ── A node that throws ──

    [Test]
    public async Task Execute_ANodeThatThrows_IsRecordedAsFailedRatherThanEndingTheRun()
    {
        // Live run 1 died this way: a provider outage inside one node propagated out of the plan and
        // faulted the whole workflow. A plan could record a step that disputed and could only ever
        // *die* of one that failed.
        var store = await Seeded();

        var result = await new PlanExecutor(store).ExecuteAsync("plan", (ctx, _) =>
            ctx.Node.Id == "parse"
                ? throw new InvalidOperationException("429 Too Many Requests (quota exhausted)")
                : Task.FromResult(Meets(ctx)));

        result.HaltedAt.ShouldBe("parse");
        // S9's finding: the loop's own attempt count, then the failure's own words, verbatim.
        result.Tree.Node("parse").Failure!.Message
            .ShouldBe("could not be run: 3 attempts, 429 Too Many Requests (quota exhausted)");
        result.Outcome.ShouldBe(PlanRunOutcome.Faulted);
    }

    [Test]
    public async Task Execute_AFailedNode_KeepsWhatThrewInItsOwnWordsAndWritesNoVerdict()
    {
        // A verdict claims something was checked, and a node that died checked nothing. Writing a
        // failing verdict instead would put a claim in the tree that nobody made.
        var store = await Seeded();

        await new PlanExecutor(store).ExecuteAsync("plan", (ctx, _) =>
            ctx.Node.Id == "parse"
                ? throw new InvalidOperationException("the tool loop exceeded 3 rounds")
                : Task.FromResult(Meets(ctx)));

        var node = (await store.LoadAsync("plan"))!.Node("parse");

        node.Failure!.Message.ShouldBe("could not be run: 3 attempts, the tool loop exceeded 3 rounds");
        node.Verdicts.ShouldBeEmpty();
        node.Violation.ShouldBeNull(); // it decided nothing; it is not disputing
    }

    [Test]
    public async Task Execute_ANodeThatFailedAndThenRanToCompletion_IsNoLongerFailed()
    {
        // The asymmetry with a dispute, and the reason a retry is worth having: a dispute is a claim
        // about the contract and stands until the contract changes; a failure is a claim about one
        // attempt, and an attempt that finished is the direct answer to it.
        var store = await Seeded();
        var attempts = 0;

        // Retries off, deliberately. E9 re-issues a dead attempt inside the same pass, so a runner
        // that fails once and then works never records a failure at all — which is the right
        // behaviour and the wrong fixture for this claim. What is under test here is the one after
        // it: that a failure already on the record is cleared by a later attempt completing.
        var executor = new PlanExecutor(store, retries: PlanRetryPolicy.Once);

        Task<NodeOutcome> Flaky(PlanNodeContext ctx, CancellationToken ct) =>
            ctx.Node.Id == "parse" && ++attempts == 1
                ? throw new InvalidOperationException("transient")
                : Task.FromResult(Meets(ctx));

        await executor.ExecuteAsync("plan", Flaky);
        (await store.LoadAsync("plan"))!.LifecycleOf("parse").ShouldBe(NodeLifecycle.Faulted);

        var second = await executor.ExecuteAsync("plan", Flaky);

        second.HaltedAt.ShouldBeNull();
        (await store.LoadAsync("plan"))!.Node("parse").Failure.ShouldBeNull();
    }

    [Test]
    public void Execute_ACancelledRun_IsNotRecordedAsANodeFailure()
    {
        // Cancellation is not a node going wrong; it is the caller leaving. Recording it as a
        // failure would put a defect in the tree for something the plan did correctly.
        var store = Seeded().GetAwaiter().GetResult();
        using var cancelled = new CancellationTokenSource();

        Should.Throw<OperationCanceledException>(async () =>
            await new PlanExecutor(store).ExecuteAsync(
                "plan",
                (_, _) => throw new OperationCanceledException(cancelled.Token),
                cancelled.Token));

        store.LoadAsync("plan").GetAwaiter().GetResult()!.Node("parse").Failure.ShouldBeNull();
    }

    [Test]
    public async Task Execute_AFailedNode_StopsThePassWhereItStoodRatherThanCarryingOn()
    {
        // Post-order: a later node's input is an earlier node's output, so running on past a step
        // that produced nothing would be checking work against a state nobody established.
        var store = await Seeded();

        var result = await new PlanExecutor(store).ExecuteAsync("plan", (ctx, _) =>
            ctx.Node.Id == "parse"
                ? throw new InvalidOperationException("stopped")
                : Task.FromResult(Meets(ctx)));

        result.Executed.ShouldBe(["parse"]);
    }

    [Test]
    public async Task Rerule_ThenRerun_ContinuesUnderTheNewContractAndKeepsTheOldOneReadable()
    {
        var store = await Seeded();
        var executor = new PlanExecutor(store);

        // A dispute reaches the tree through DisputeAsync now — an external call, never a node's own
        // report (R26) — so it is seeded before the pass rather than returned by the runner.
        var dispute = Dispute();
        await executor.DisputeAsync("plan", "parse", dispute.Criterion, dispute.Reason);
        await executor.ExecuteAsync("plan", (ctx, _) => Task.FromResult(Meets(ctx)));

        await executor.ReruleAsync(
            "plan", "parse",
            Contract("Parse, streaming", "the parser handles files larger than memory"),
            "round-tripping is impossible for the format as specified");

        var result = await executor.ExecuteAsync("plan", (ctx, _) => Task.FromResult(Meets(ctx)));

        result.HaltedAt.ShouldBeNull();
        result.RootOutcome.ShouldBe(ContractOutcome.Met);
        result.Tree.Lineage.Count.ShouldBe(2);
        result.Tree.Lineage[0].Nodes["parse"].Contract.Goal.ShouldBe("Parse");
        result.Tree.Node("parse").Contract.Goal.ShouldBe("Parse, streaming");
    }

    // ── Attestation: the tree records that a node ran, even when nothing changed ──

    [Test]
    public async Task Execute_ANodeThatChangedNothing_StillLeavesARecordThatItRan()
    {
        // The gap this closes: everything else is written only when work *produces* something, so a
        // node that ran, read its surroundings and had nothing to add left no trace at all.
        var store = await Seeded();

        await new PlanExecutor(store).ExecuteAsync(
            "plan", (_, _) => Task.FromResult(NodeOutcome.Nothing));

        var tree = await store.LoadAsync("plan");
        var node = tree!.Node("parse");

        node.Verdicts.ShouldBeEmpty();          // nothing changed
        node.LastRead.ShouldNotBeNull();        // but it demonstrably ran
        node.ReadCount.ShouldBe(1);
        node.LastRead.PlanVersion.ShouldBe(1);

        // The two axes, on the one node that separates them: it ran, and it decided nothing. The
        // single enum these replace called this "Pending" — the same word as never having run.
        tree.LifecycleOf("parse").ShouldBe(NodeLifecycle.Completed);
        tree.OutcomeOf("parse").ShouldBe(ContractOutcome.Unmet);
    }

    [Test]
    public async Task Execute_TheReading_SaysHowMuchOfTheTreeTheNodeWasShown()
    {
        // The separation that matters when a node goes wrong: content withheld from it, versus
        // content it was shown and did not use.
        var store = await Seeded();

        await new PlanExecutor(store).ExecuteAsync("plan", (ctx, _) => Task.FromResult(Meets(ctx)));

        var tree = await store.LoadAsync("plan");
        var reading = tree!.Node("render").LastRead;

        reading.ShouldNotBeNull();
        reading.ProjectedTokens.ShouldBeGreaterThan(0);
        reading.SawEverything.ShouldBeTrue();
    }

    [Test]
    public async Task Execute_TheReading_KeepsWhatTheNodeSaidItDid()
    {
        // The one thing about a run that no verdict and no dispute can reconstruct. Whoever has to
        // decide what a halted plan should become is otherwise reasoning about work nobody described.
        var store = await Seeded();

        await new PlanExecutor(store).ExecuteAsync("plan", (ctx, _) => Task.FromResult(
            new NodeOutcome { Summary = $"did the work for {ctx.Node.Id}" }));

        var tree = await store.LoadAsync("plan");

        tree!.Node("parse").LastRead!.Summary.ShouldBe("did the work for parse");
        tree.Node("render").LastRead!.Summary.ShouldBe("did the work for render");
    }

    [Test]
    public async Task Execute_ARunnerThatDescribesNothing_LeavesNoAccountRatherThanAnEmptyOne()
    {
        // A shell check and a person need offer no prose. "It said nothing" and "it said the empty
        // string" are the same fact, and only one of them reads that way to whoever is shown it.
        var store = await Seeded();

        await new PlanExecutor(store).ExecuteAsync(
            "plan", (_, _) => Task.FromResult(new NodeOutcome { Summary = "   " }));

        var tree = await store.LoadAsync("plan");

        tree!.Node("parse").LastRead.ShouldNotBeNull();
        tree.Node("parse").LastRead!.Summary.ShouldBeNull();
    }

    [Test]
    public async Task Execute_UnderATightProjectionBudget_TheReadingRecordsWhatWasWithheld()
    {
        var store = new InMemoryPlanTreeStore();
        var seeded = Tree();
        for (var i = 0; i < 12; i++)
            seeded = seeded.WithVerdict("parse", Verdict($"an earlier finding number {i}", passed: true));

        await store.SaveAsync(seeded);

        await new PlanExecutor(store, projectionTokenBudget: 40)
            .ExecuteAsync("plan", (ctx, _) => Task.FromResult(Meets(ctx)));

        var reading = (await store.LoadAsync("plan"))!.Node("render").LastRead;

        reading.ShouldNotBeNull();
        reading.SawEverything.ShouldBeFalse();
        reading.OmittedRecords.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task Execute_ReadCount_DistinguishesRetriedWorkFromSettledWork()
    {
        var store = await Seeded();
        var executor = new PlanExecutor(store);

        await executor.ExecuteAsync("plan", (_, _) => Task.FromResult(NodeOutcome.Nothing));
        await executor.ExecuteAsync("plan", (_, _) => Task.FromResult(NodeOutcome.Nothing));

        (await store.LoadAsync("plan"))!.Node("parse").ReadCount.ShouldBe(2);
    }

    // ── Drift: satisfied against a plan that has since moved on ──

    [Test]
    public async Task Rerule_ASiblingSubtree_LeavesTheOtherSatisfiedButVisiblyStale()
    {
        // Re-ruling clears verdicts in the subtree it invalidates and nowhere else, which is right —
        // contracts flow down, not sideways. So "render" stays legitimately satisfied while the plan
        // around it has changed, and nothing about its verdicts would say so.
        var store = await Seeded();
        var executor = new PlanExecutor(store);

        await executor.ExecuteAsync("plan", (ctx, _) => Task.FromResult(Meets(ctx)));

        await executor.ReruleAsync(
            "plan", "parse", Contract("Parse, streaming", "handles files larger than memory"),
            "the input no longer fits in memory");

        var tree = (await store.LoadAsync("plan"))!;

        tree.OutcomeOf("render").ShouldBe(ContractOutcome.Met);
        tree.IsStale("render").ShouldBeTrue();
        tree.Node("render").LastRead!.PlanVersion.ShouldBe(1);
        tree.Current.Number.ShouldBe(2);
    }

    [Test]
    public async Task Execute_AfterARerule_ReportsStaleSkippedNodesWithoutRerunningThem()
    {
        var store = await Seeded();
        var executor = new PlanExecutor(store);

        await executor.ExecuteAsync("plan", (ctx, _) => Task.FromResult(Meets(ctx)));
        await executor.ReruleAsync(
            "plan", "parse", Contract("Parse, streaming", "handles files larger than memory"),
            "the input no longer fits in memory");

        var seen = new List<string>();
        var result = await executor.ExecuteAsync("plan", (ctx, _) =>
        {
            seen.Add(ctx.Node.Id);
            return Task.FromResult(Meets(ctx));
        });

        // Only "parse" re-runs; the other two are satisfied and skipped rather than re-checked.
        seen.ShouldBe(["parse"]);
        result.Skipped.ShouldBe(["render", "plan"], ignoreOrder: true);

        // Drift is a question for the tree, not for the pass. Both stale nodes are named — and the
        // root's presence is the useful half: its own criterion ("the build is green") was decided
        // before "parse" was re-ruled, so it is satisfied against a plan that no longer exists.
        result.Tree.StaleNodes().ShouldBe(["render", "plan"], ignoreOrder: true);
    }

    [Test]
    public void IsStale_ANodeThatNeverRan_IsPlannedRatherThanStale()
    {
        // Conflating the two would turn every fresh plan into a wall of warnings.
        var tree = Tree();

        tree.IsStale("parse").ShouldBeFalse();
        tree.LifecycleOf("parse").ShouldBe(NodeLifecycle.Planned);
    }

    [Test]
    public void WithReading_MintsNoVersion()
    {
        var tree = Tree().WithReading("parse", new NodeReading
        {
            At = T0,
            PlanVersion = 1,
            ProjectedTokens = 10
        });

        tree.Lineage.Count.ShouldBe(1);
    }

    // ── The projection: read at whatever depth the allocation allows ──

    [Test]
    public void Project_WithNoBudget_ShowsTheWholeSurroundings()
    {
        var tree = Tree().WithVerdict("parse", Verdict("the parser round-trips", passed: true));

        var projection = PlanTreeProjection.Project(tree, "render");

        projection.Text.ShouldContain("Ship it");                  // the ancestor
        projection.Text.ShouldContain("the parser round-trips");   // what is established
        projection.Text.ShouldContain("Render");                   // its own contract
        projection.OmittedAncestors.ShouldBe(0);
        projection.OmittedVerdicts.ShouldBe(0);
    }

    [Test]
    public void Project_AStepUnderAConstrainedRoot_ShowsTheRootsConstraints()
    {
        var tree = PlanTree.Create(
            "trip",
            new AgentContract { Goal = "A week in Japan", Constraints = ["Hakone is a must"] },
            [("hakone", new AgentContract { Goal = "Book Hakone", AcceptanceCriteria = ["booked(hakone)"] })]);

        PlanTreeProjection.Project(tree, "hakone").Text.ShouldContain("Hakone is a must");
    }

    [Test]
    public void Lineage_APlanChangedOnce_ListsTheVersionBeforeWithItsCriteriaAndWhyItChanged()
    {
        var tree = PlanTree.Create(
                "trip",
                new AgentContract { Goal = "A week in Japan" },
                [("hakone", new AgentContract { Goal = "Book Hakone", AcceptanceCriteria = ["booked(hakone, 2027-04-01, 2027-04-08, 2)"] })])
            .Rerule(
                "trip",
                new AgentContract { Goal = "A week in Japan" },
                "only 2027-04-07 is free",
                [("hakone-one", new AgentContract { Goal = "Book Hakone", AcceptanceCriteria = ["booked(hakone, 2027-04-07, 2027-04-08, 1)"] })]);

        var text = PlanTreeProjection.Lineage(tree);

        text.ShouldContain("version 1: hakone [booked(hakone, 2027-04-01, 2027-04-08, 2)]");
        text.ShouldContain("replaced because: only 2027-04-07 is free");
        text.ShouldNotContain("hakone-one");
    }

    [Test]
    public async Task Execute_AStepThatCouldNotDoItsTask_LeavesItsOptionsWhereThePassHalted()
    {
        var store = await Seeded();
        var seen = new Collecting();
        var verifier = new DeterministicVerifier(
            [new PredicateCheck("the parser", ["the parser round-trips"], _ => false)]);

        PlanRunResult result;

        using (WorkflowEventReporting.BeginScope(seen))
        {
            result = await new PlanExecutor(store, verifier: verifier).ExecuteAsync("plan", (ctx, _) =>
                Task.FromResult(ctx.Node.Id == "parse" ? CouldNot() : Meets(ctx)));
        }

        result.HaltedAt.ShouldBe("parse");

        var question = (await store.LoadAsync("plan"))!.Node("parse").Question.ShouldNotBeNull();
        question.Asks.ShouldBe("only the documented dialect parses");
        question.Options.ShouldBe(["parse the documented dialect only", "add a second parser"]);

        seen.Events.OfType<PlanNodeBlocked>().ShouldHaveSingleItem().Options.Count.ShouldBe(2);
    }

    [Test]
    public async Task Execute_AStepThatCouldNotDoItsTask_IsNotRetriedForTheEmptyOperationItLeft()
    {
        var store = await Seeded();
        var applied = false;
        var verifier = new DeterministicVerifier(
            [new PredicateCheck("the parser", ["the parser round-trips"], _ => false)]);
        var operations = new OperationCatalog(
            [new OperationDefinition { Name = "parse", Arity = 0, Description = "parse the input" }]);

        var result = await new PlanExecutor(
                store,
                verifier: verifier,
                operations: operations,
                applier: (_, _, _) =>
                {
                    applied = true;
                    return Task.FromResult(PlanApplication.Ok());
                })
            .ExecuteAsync("plan", (ctx, _) => Task.FromResult(ctx.Node.Id == "parse"
                ? CouldNot() with { Candidate = new Operation { Name = "" } }
                : Meets(ctx)));

        result.HaltedAt.ShouldBe("parse");
        applied.ShouldBeFalse();

        var node = (await store.LoadAsync("plan"))!.Node("parse");
        node.Failure.ShouldBeNull();
        node.Question.ShouldNotBeNull().Options.Count.ShouldBe(2);
    }

    [Test]
    public async Task Execute_AStepThatCouldNotDoItsTask_IsBlockedEvenWhenItsCheckHolds()
    {
        var store = await Seeded();
        var verifier = new DeterministicVerifier(
            [new PredicateCheck("the parser", ["the parser round-trips"], _ => true)]);

        var result = await new PlanExecutor(store, verifier: verifier).ExecuteAsync("plan", (ctx, _) =>
            Task.FromResult(ctx.Node.Id == "parse" ? CouldNot() : Meets(ctx)));

        result.HaltedAt.ShouldBe("parse");

        var node = (await store.LoadAsync("plan"))!.Node("parse");
        node.State.ShouldBe(StepState.Blocked);
        node.Question.ShouldNotBeNull().Done.ShouldBeFalse();
    }

    [Test]
    public async Task Execute_AStepDoneWithOneOption_IsDoneWithThatOption_AndNotRunAgain()
    {
        var store = await Seeded();
        var ran = new List<string>();

        Task<NodeOutcome> Run(PlanNodeContext ctx, CancellationToken _)
        {
            ran.Add(ctx.Node.Id);
            return Task.FromResult(ctx.Node.Id == "plan" ? Meets(ctx) : DoneWith($"{ctx.Node.Id}: the only way"));
        }

        var result = await new PlanExecutor(store).ExecuteAsync("plan", Run);

        result.HaltedAt.ShouldBeNull();
        result.Outcome.ShouldBe(PlanRunOutcome.Completed);

        var tree = (await store.LoadAsync("plan"))!;
        tree.Node("parse").State.ShouldBe(StepState.Done);
        tree.Node("parse").Result.ShouldBe("parse: the only way");
        tree.State.ShouldBe(StepState.Done);

        ran.Clear();
        await new PlanExecutor(store).ExecuteAsync("plan", Run);
        ran.ShouldNotContain("parse");
    }

    [Test]
    public async Task Execute_AStepDoneWithSeveralOptions_IsBlockedWithThemToChooseFrom()
    {
        var store = await Seeded();

        var result = await new PlanExecutor(store).ExecuteAsync("plan", (ctx, _) =>
            Task.FromResult(ctx.Node.Id == "parse" ? DoneWith("the fast parser", "the strict parser") : Meets(ctx)));

        result.HaltedAt.ShouldBe("parse");

        var tree = (await store.LoadAsync("plan"))!;
        tree.Node("parse").State.ShouldBe(StepState.Blocked);
        tree.State.ShouldBe(StepState.Blocked);

        var question = tree.Node("parse").Question.ShouldNotBeNull();
        question.Done.ShouldBeTrue();
        question.Options.ShouldBe(["the fast parser", "the strict parser"]);
    }

    [Test]
    public async Task Answer_ToAQuestionFromAStepThatCouldNotDoItsTask_LeavesItPending()
    {
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(Tree()
            .WithQuestion("parse", new NodeQuestion
            {
                Asks = "only the documented dialect parses",
                Options = ["parse the documented dialect only", "add a second parser"],
                Done = false,
                At = T0
            })
            .WithState("parse", StepState.Blocked));

        await new PlanExecutor(store).AnswerAsync(
            "plan", "parse", "add a second parser", "a person");

        var node = (await store.LoadAsync("plan"))!.Node("parse");
        node.State.ShouldBe(StepState.Pending);
        node.Result.ShouldBeNull();
    }

    [Test]
    public async Task Execute_AStepNotDoneWithNoOptions_ReportsItBlocked()
    {
        var store = await Seeded();
        var seen = new Collecting();

        using (WorkflowEventReporting.BeginScope(seen))
        {
            await new PlanExecutor(store).ExecuteAsync("plan", (ctx, _) =>
                Task.FromResult(ctx.Node.Id == "parse"
                    ? new NodeOutcome { Summary = "nothing found", Done = false }
                    : Meets(ctx)));
        }

        var blocked = seen.Events.OfType<PlanNodeBlocked>().ShouldHaveSingleItem();
        blocked.Asks.ShouldBe("nothing found");
        blocked.Options.ShouldBeEmpty();
    }

    [Test]
    public async Task Answer_OneOfTheOptionsOfAStepThatDidItsTask_MarksItDoneWithThatOption()
    {
        var store = await Seeded();
        var executor = new PlanExecutor(store);

        await executor.ExecuteAsync("plan", (ctx, _) =>
            Task.FromResult(ctx.Node.Id == "parse" ? DoneWith("the fast parser", "the strict parser") : Meets(ctx)));

        await executor.AnswerAsync("plan", "parse", "The strict parser", "supervisor");

        var node = (await store.LoadAsync("plan"))!.Node("parse");
        node.State.ShouldBe(StepState.Done);
        node.Result.ShouldBe("the strict parser");
        node.Question.ShouldBeNull();
    }

    [Test]
    public async Task LeavesOnly_ANodeWithChildren_NeverReachesTheRunnerAndRecordsNothing()
    {
        // A node with children only decomposes: what its criteria ask for is met by the steps beneath
        // it, so asking a model to do its work as well is a call nobody reads.
        var asked = new List<string>();

        var runner = PlanNodeRunners.LeavesOnly((ctx, _) =>
        {
            asked.Add(ctx.Node.Id);
            return Task.FromResult(Meets(ctx));
        });

        var outcome = await runner(Context(Tree(), "plan"), CancellationToken.None);

        outcome.ShouldBe(NodeOutcome.Nothing);
        asked.ShouldBeEmpty();
    }

    [Test]
    public async Task LeavesOnly_ALeaf_GetsWhateverTheRunnerSaid()
    {
        var runner = PlanNodeRunners.LeavesOnly((ctx, _) => Task.FromResult(DoneWith($"{ctx.Node.Id}: one way")));

        var outcome = await runner(Context(Tree(), "parse"), CancellationToken.None);

        outcome.Options.ShouldBe(["parse: one way"]);
    }

    [Test]
    public void Abandon_AStep_ReadsSkipped() =>
        Tree().Abandon("parse", new PlanAbandonment { Reason = "not wanted", By = "a person", At = T0 })
            .Node("parse").State.ShouldBe(StepState.Skipped);

    [Test]
    public void Rerule_AStepDoneUnderAnotherContract_IsPendingAgain()
    {
        var tree = Tree().WithState("parse", StepState.Done, "the fast parser");

        var reruled = tree.Rerule(
            "plan", Contract("Ship it", "the build is green"),
            "parsing changed",
            [("parse", Contract("Parse both dialects", "the parser round-trips")), ("render", Contract("Render", "output matches the golden file"))]);

        reruled.Node("parse").State.ShouldBe(StepState.Pending);
        reruled.Node("parse").Result.ShouldBeNull();
    }

    [Test]
    public async Task Execute_AStepThatRunsAgain_ClearsTheQuestionItLeftBefore()
    {
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(Tree().WithQuestion("parse", new NodeQuestion
        {
            Asks = "only the documented dialect parses",
            Options = ["parse the documented dialect only"],
            At = T0
        }));

        await new PlanExecutor(store).ExecuteAsync("plan", (ctx, _) => Task.FromResult(Meets(ctx)));

        (await store.LoadAsync("plan"))!.Node("parse").Question.ShouldBeNull();
    }

    [Test]
    public void Lineage_APlanThatNeverChanged_IsEmpty() =>
        PlanTreeProjection.Lineage(PlanTree.Create(
                "trip",
                new AgentContract { Goal = "A week in Japan" },
                [("hakone", new AgentContract { Goal = "Book Hakone" })]))
            .ShouldBeEmpty();

    [Test]
    public void Project_UnderATightBudget_KeepsItsOwnContractAndSaysWhatItLeftOut()
    {
        var tree = Tree();
        for (var i = 0; i < 12; i++)
            tree = tree.WithVerdict("parse", Verdict($"an earlier finding number {i}", passed: true));

        var full = PlanTreeProjection.Project(tree, "render");
        var tight = PlanTreeProjection.Project(tree, "render", full.EstimatedTokens / 3);

        tight.EstimatedTokens.ShouldBeLessThanOrEqualTo(full.EstimatedTokens / 3);

        // The node's own contract is pinned — it is the one thing never given up.
        tight.Text.ShouldContain("Render");
        tight.Text.ShouldContain("output matches the golden file");

        // And the omission announces itself rather than looking like completeness.
        tight.OmittedVerdicts.ShouldBeGreaterThan(0);
        tight.Text.ShouldContain("not shown");
    }

    [Test]
    public void Project_ShowsEveryStepInOrder_WithWhereItStandsAndWhatItSettledOn()
    {
        var tree = Tree().WithState("parse", StepState.Done, "the fast parser");

        var text = PlanTreeProjection.Project(tree, "render").Text;

        text.ShouldContain("- parse: Parse [the parser round-trips] — Done: the fast parser");
        text.ShouldContain("- render: Render [output matches the golden file] — this step");
        text.IndexOf("- parse:", StringComparison.Ordinal)
            .ShouldBeLessThan(text.IndexOf("- render:", StringComparison.Ordinal));
    }

    [Test]
    public void Project_GivesUpEstablishedRecordsBeforeItGivesUpAncestors()
    {
        // Where this node sits survives longer than the detail of what happened elsewhere: losing
        // the former leaves the node working on something it cannot place.
        var tree = Tree();
        for (var i = 0; i < 12; i++)
            tree = tree.WithVerdict("parse", Verdict($"an earlier finding number {i}", passed: true));

        var full = PlanTreeProjection.Project(tree, "render");
        var tight = PlanTreeProjection.Project(tree, "render", full.EstimatedTokens / 2);

        tight.OmittedVerdicts.ShouldBeGreaterThan(0);
        tight.OmittedAncestors.ShouldBe(0);
        tight.Text.ShouldContain("Ship it");
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

    private static CriterionVerdict Verdict(string criterion, bool passed) =>
        new() { Criterion = criterion, Passed = passed, Oracle = "dotnet test", At = T0 };

    private static PlanViolation Dispute() => new()
    {
        Criterion = "the parser round-trips",
        Reason = "round-tripping is impossible for the input format as specified",
        At = T0
    };

    private static PlanNodeContext Context(PlanTree tree, string nodeId)
    {
        var projection = PlanTreeProjection.Project(tree, nodeId);

        return new PlanNodeContext
        {
            PlanId = tree.PlanId,
            PlanVersion = tree.Current.Number,
            Node = tree.Node(nodeId),
            Contract = tree.Node(nodeId).Contract,
            TreeView = projection.Text,
            Projection = projection
        };
    }

    private static NodeOutcome DoneWith(params string[] options) => new()
    {
        Summary = "found what the contract asks for",
        Options = options
    };

    private static NodeOutcome CouldNot() => new()
    {
        Summary = "only the documented dialect parses",
        Done = false,
        Options = ["parse the documented dialect only", "add a second parser"]
    };

    private sealed class Collecting : IWorkflowEventSink
    {
        public List<WorkflowEvent> Events { get; } = [];

        public ValueTask ReportAsync(WorkflowEvent evt, CancellationToken ct = default)
        {
            Events.Add(evt);
            return ValueTask.CompletedTask;
        }
    }
}
