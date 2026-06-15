using Ananke.Abstractions.Agents;
using Ananke.Organics.Division;
using Ananke.Organics.Healing;
using Ananke.Organics.Kernel.Lineage;
using Ananke.Organics.Sensing;
using Shouldly;

namespace Ananke.Organics.Tests;

/// <summary>Tests for Phase 2 biology regulatory loops (L1–L5).</summary>
[TestFixture]
public class AdaptiveMeshTests
{
    // ══════════════════════════════════════════════════════════════════
    // L1 — Lineage
    // ══════════════════════════════════════════════════════════════════

    [Test]
    public async Task InMemoryLineageStore_RecordBirth_GetAsync_Roundtrip()
    {
        var store = new InMemoryLineageStore();
        var lineage = new CellLineage
        {
            CellId = "cell-1",
            WorkflowName = "search",
            Generation = 0,
            BornAt = DateTimeOffset.UtcNow
        };

        await store.RecordBirthAsync(lineage);
        var retrieved = await store.GetAsync("cell-1");

        retrieved.ShouldNotBeNull();
        retrieved!.CellId.ShouldBe("cell-1");
        retrieved.Generation.ShouldBe(0);
        retrieved.DiedAt.ShouldBeNull();
    }

    [Test]
    public async Task InMemoryLineageStore_RecordDeath_DoesNotDeleteRecord()
    {
        var store = new InMemoryLineageStore();
        var born = DateTimeOffset.UtcNow;
        await store.RecordBirthAsync(new CellLineage
        {
            CellId = "cell-a",
            WorkflowName = "a",
            Generation = 1,
            BornAt = born
        });

        await store.RecordDeathAsync("cell-a", born.AddHours(2), "idle");

        var record = await store.GetAsync("cell-a");
        record.ShouldNotBeNull();
        record!.DiedAt.ShouldNotBeNull();
        record.DeathReason.ShouldBe("idle");
        record.BornAt.ShouldBe(born);   // birth preserved
    }

    [Test]
    public async Task InMemoryLineageStore_RecordDeath_UnknownCell_IsNoOp()
    {
        var store = new InMemoryLineageStore();
        await store.RecordDeathAsync("never-born", DateTimeOffset.UtcNow, "test");
        var record = await store.GetAsync("never-born");
        record.ShouldBeNull();
    }

    [Test]
    public async Task InMemoryLineageStore_GetDescendants_ReturnsFull3GenerationTree()
    {
        var store = new InMemoryLineageStore();
        var now = DateTimeOffset.UtcNow;

        await store.RecordBirthAsync(new CellLineage { CellId = "root", WorkflowName = "root", Generation = 0, BornAt = now });
        await store.RecordBirthAsync(new CellLineage { CellId = "child-a", WorkflowName = "child-a", ParentCellId = "root", Generation = 1, BornAt = now.AddMinutes(10) });
        await store.RecordBirthAsync(new CellLineage { CellId = "child-b", WorkflowName = "child-b", ParentCellId = "root", Generation = 1, BornAt = now.AddMinutes(11) });
        await store.RecordBirthAsync(new CellLineage { CellId = "grandchild", WorkflowName = "grandchild", ParentCellId = "child-a", Generation = 2, BornAt = now.AddMinutes(20) });

        var descendants = await store.GetDescendantsAsync("root");

        descendants.Count.ShouldBe(3);
        descendants.ShouldContain(d => d.CellId == "child-a");
        descendants.ShouldContain(d => d.CellId == "child-b");
        descendants.ShouldContain(d => d.CellId == "grandchild");
    }

    [Test]
    public async Task InMemoryLineageStore_GetByGeneration_FiltersCorrectly()
    {
        var store = new InMemoryLineageStore();
        var now = DateTimeOffset.UtcNow;
        await store.RecordBirthAsync(new CellLineage { CellId = "g0", WorkflowName = "g0", Generation = 0, BornAt = now });
        await store.RecordBirthAsync(new CellLineage { CellId = "g1a", WorkflowName = "g1a", Generation = 1, BornAt = now });
        await store.RecordBirthAsync(new CellLineage { CellId = "g1b", WorkflowName = "g1b", Generation = 1, BornAt = now });

        var gen1 = await store.GetByGenerationAsync(1);
        gen1.Count.ShouldBe(2);
        gen1.ShouldAllBe(r => r.Generation == 1);
    }

    // ══════════════════════════════════════════════════════════════════
    // L2 — Fitness feedback (DivisionExperience + IDivisionPolicy overload)
    // ══════════════════════════════════════════════════════════════════

    [Test]
    public async Task IDivisionPolicy_ExperienceOverload_DefaultDelegatesToLegacy()
    {
        var policy = new ThresholdDivisionPolicy(minTools: 1, minClusters: 1);
        IDivisionPolicy policyInterface = policy;
        var snapshot = MakeSnapshot("cell", toolCount: 2, clusters: 2);
        var manifest = MakeManifest("cell");

        // Experience-aware overload should forward to the legacy one and return a plan
        var planWithExperience = await policyInterface.EvaluateAsync(
            snapshot, manifest,
            new List<DivisionExperience>
            {
                new DivisionExperience
                {
                    LineageId = "cell",
                    Plan = new DivisionPlan { ParentWorkflow = "cell", Reason = "test", Children = Array.Empty<ChildSpec>() },
                    Metrics = new DivisionOutcomeMetrics(),
                    Verdict = DivisionVerdict.Improved
                }
            });

        var planWithout = await policyInterface.EvaluateAsync(snapshot, manifest);

        // Both should return a non-null plan (threshold met)
        planWithExperience.ShouldNotBeNull();
        planWithout.ShouldNotBeNull();
    }

    [Test]
    public void DivisionExperience_VerdictEnum_HasThreeValues()
    {
        var values = Enum.GetValues<DivisionVerdict>();
        values.ShouldContain(DivisionVerdict.Improved);
        values.ShouldContain(DivisionVerdict.Neutral);
        values.ShouldContain(DivisionVerdict.Regressed);
    }

    // ══════════════════════════════════════════════════════════════════
    // L3 — Metabolism
    // ══════════════════════════════════════════════════════════════════

    [Test]
    public void MetabolicThresholds_Classify_Healthy_WhenNoTelemetry()
    {
        var thresholds = MetabolicThresholds.Default;
        var snapshot = MakeSnapshot("cell", toolCount: 2, clusters: 1);

        thresholds.Classify(snapshot).ShouldBe(MetabolicSignal.Healthy);
    }

    [Test]
    public void MetabolicThresholds_Classify_Stressed_OnHighErrorRate()
    {
        var thresholds = MetabolicThresholds.Default;
        var snapshot = MakeSnapshot("cell", toolCount: 2, clusters: 1) with { ErrorRate = 0.15 };

        thresholds.Classify(snapshot).ShouldBe(MetabolicSignal.Stressed);
    }

    [Test]
    public void MetabolicThresholds_Classify_Starved_OnVeryHighErrorRate()
    {
        var thresholds = MetabolicThresholds.Default;
        var snapshot = MakeSnapshot("cell", toolCount: 2, clusters: 1) with { ErrorRate = 0.35 };

        thresholds.Classify(snapshot).ShouldBe(MetabolicSignal.Starved);
    }

    [Test]
    public void MetabolicThresholds_Classify_Stressed_OnHighLatency()
    {
        var thresholds = MetabolicThresholds.Default;
        var snapshot = MakeSnapshot("cell", toolCount: 2, clusters: 1) with { LatencyP95Ms = 6_000 };

        thresholds.Classify(snapshot).ShouldBe(MetabolicSignal.Stressed);
    }

    [Test]
    public async Task MetabolicDivisionApprovalGate_Healthy_DelegatesToInner()
    {
        var inner = new Ananke.Organics.Division.Approval.AutoApprovalGate();
        var gate = new MetabolicDivisionApprovalGate(inner);
        var snapshot = MakeSnapshot("cell", toolCount: 2, clusters: 1);
        var plan = new DivisionPlan { ParentWorkflow = "cell", Reason = "test", Children = Array.Empty<ChildSpec>() };

        var result = await gate.ReviewAsync(plan, snapshot);

        result.IsApproved.ShouldBeTrue();
    }

    [Test]
    public async Task MetabolicDivisionApprovalGate_Stressed_Rejects()
    {
        var inner = new Ananke.Organics.Division.Approval.AutoApprovalGate();
        var gate = new MetabolicDivisionApprovalGate(inner);
        var snapshot = MakeSnapshot("cell", toolCount: 2, clusters: 1) with { ErrorRate = 0.15 };
        var plan = new DivisionPlan { ParentWorkflow = "cell", Reason = "test", Children = Array.Empty<ChildSpec>() };

        var result = await gate.ReviewAsync(plan, snapshot);

        result.IsApproved.ShouldBeFalse();
        result.Reason.ShouldContain("Stressed");
    }

    [Test]
    public async Task MetabolicDivisionApprovalGate_Starved_Rejects()
    {
        var inner = new Ananke.Organics.Division.Approval.AutoApprovalGate();
        var gate = new MetabolicDivisionApprovalGate(inner);
        var snapshot = MakeSnapshot("cell", toolCount: 2, clusters: 1) with { ErrorRate = 0.40 };
        var plan = new DivisionPlan { ParentWorkflow = "cell", Reason = "test", Children = Array.Empty<ChildSpec>() };

        var result = await gate.ReviewAsync(plan, snapshot);

        result.IsApproved.ShouldBeFalse();
        result.Reason.ShouldContain("Starved");
    }

    [Test]
    public void ComplexitySnapshot_NewMetabolicFields_DefaultToNull_And_Healthy()
    {
        var snapshot = new ComplexitySnapshot
        {
            WorkflowName = "x",
            ToolCount = 1,
            JobCount = 1,
            TagClusterCount = 1,
            RoutingEntropy = 0f,
            ResourceSpan = 1,
            ContextUtilization = 0f,
            MeasuredAt = DateTimeOffset.UtcNow
        };

        snapshot.TokensPerExecution.ShouldBeNull();
        snapshot.LatencyP95Ms.ShouldBeNull();
        snapshot.ErrorRate.ShouldBeNull();
        snapshot.Metabolism.ShouldBe(MetabolicSignal.Healthy);
    }

    // ══════════════════════════════════════════════════════════════════
    // L4 — Apoptosis
    // ══════════════════════════════════════════════════════════════════

    [Test]
    public async Task IdleCellPrunePolicy_BelowThreshold_ReturnsNull()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var policy = new IdleCellPrunePolicy(TimeSpan.FromMinutes(5), clock);
        var health = MakeHealth("cell", lastRequest: clock.GetUtcNow().AddMinutes(-2));

        var plan = await policy.EvaluateAsync(health, MakeSnapshot("cell", 2, 1));

        plan.ShouldBeNull();
    }

    [Test]
    public async Task IdleCellPrunePolicy_AboveThreshold_ReturnsPrunePlan()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var policy = new IdleCellPrunePolicy(TimeSpan.FromMinutes(5), clock);
        var health = MakeHealth("cell", lastRequest: clock.GetUtcNow().AddMinutes(-10));

        var plan = await policy.EvaluateAsync(health, MakeSnapshot("cell", 2, 1));

        plan.ShouldNotBeNull();
        plan!.Strategy.ShouldBe(HealingStrategy.Prune);
        plan.WorkflowName.ShouldBe("cell");
    }

    [Test]
    public async Task IdleCellPrunePolicy_NullLastRequest_UseObservedSince()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var policy = new IdleCellPrunePolicy(TimeSpan.FromMinutes(5), clock);
        // No last request — observed since 10 min ago
        var health = MakeHealth("cell", lastRequest: null, observedSince: clock.GetUtcNow().AddMinutes(-10));

        var plan = await policy.EvaluateAsync(health, MakeSnapshot("cell", 2, 1));

        plan.ShouldNotBeNull();
        plan!.Strategy.ShouldBe(HealingStrategy.Prune);
    }

    [Test]
    public async Task AgedCellPrunePolicy_YoungCell_ReturnsNull()
    {
        var store = new InMemoryLineageStore();
        await store.RecordBirthAsync(new CellLineage
        {
            CellId = "young",
            WorkflowName = "young",
            Generation = 0,
            BornAt = DateTimeOffset.UtcNow.AddDays(-1)  // only 1 day old
        });

        var policy = new AgedCellPrunePolicy(TimeSpan.FromDays(7), minUtilityScore: 0.1, store);
        var health = MakeHealth("young", windowSize: 50);  // still getting traffic

        var plan = await policy.EvaluateAsync(health, MakeSnapshot("young", 2, 1));

        plan.ShouldBeNull();
    }

    [Test]
    public async Task AgedCellPrunePolicy_OldLowUtility_ReturnsPrunePlan()
    {
        var store = new InMemoryLineageStore();
        await store.RecordBirthAsync(new CellLineage
        {
            CellId = "old-idle",
            WorkflowName = "old-idle",
            Generation = 0,
            BornAt = DateTimeOffset.UtcNow.AddDays(-30)  // 30 days old
        });

        var policy = new AgedCellPrunePolicy(TimeSpan.FromDays(7), minUtilityScore: 10.0, store);
        // Window size = 5 executions over 30 days → utility ≈ 0.17 < 10.0
        var health = MakeHealth("old-idle", windowSize: 5);

        var plan = await policy.EvaluateAsync(health, MakeSnapshot("old-idle", 2, 1));

        plan.ShouldNotBeNull();
        plan!.Strategy.ShouldBe(HealingStrategy.Prune);
    }

    [Test]
    public async Task CompositeHealingPolicy_Empty_ReturnsNull()
    {
        var plan = await CompositeHealingPolicy.Empty.EvaluateAsync(
            MakeHealth("cell"), MakeSnapshot("cell", 1, 1));
        plan.ShouldBeNull();
    }

    [Test]
    public async Task CompositeHealingPolicy_FirstMatchWins()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var idlePolicy = new IdleCellPrunePolicy(TimeSpan.FromMinutes(1), clock);
        var composite = new CompositeHealingPolicy(idlePolicy);
        var health = MakeHealth("c", lastRequest: clock.GetUtcNow().AddMinutes(-5));

        var plan = await composite.EvaluateAsync(health, MakeSnapshot("c", 1, 1));

        plan.ShouldNotBeNull();
        plan!.Strategy.ShouldBe(HealingStrategy.Prune);
    }

    // ══════════════════════════════════════════════════════════════════
    // L5 — Quorum sensing
    // ══════════════════════════════════════════════════════════════════

    [Test]
    public void InMemoryMeshAggregator_AllHealthy_ZeroStressRatio()
    {
        var agg = new InMemoryMeshAggregator();
        agg.Report("a", MetabolicSignal.Healthy);
        agg.Report("b", MetabolicSignal.Healthy);

        var signal = agg.CurrentSignal();
        signal.TotalCells.ShouldBe(2);
        signal.StressedCells.ShouldBe(0);
        signal.StressRatio.ShouldBe(0.0);
    }

    [Test]
    public void InMemoryMeshAggregator_MixedStates_ComputesRatioCorrectly()
    {
        var agg = new InMemoryMeshAggregator();
        agg.Report("a", MetabolicSignal.Healthy);
        agg.Report("b", MetabolicSignal.Stressed);
        agg.Report("c", MetabolicSignal.Stressed);
        agg.Report("d", MetabolicSignal.Healthy);

        var signal = agg.CurrentSignal();
        signal.StressRatio.ShouldBe(0.5, tolerance: 0.001);
    }

    [Test]
    public void InMemoryMeshAggregator_Forget_RemovesCell()
    {
        var agg = new InMemoryMeshAggregator();
        agg.Report("a", MetabolicSignal.Stressed);
        agg.Report("b", MetabolicSignal.Healthy);

        agg.Forget("a");

        var signal = agg.CurrentSignal();
        signal.TotalCells.ShouldBe(1);
        signal.StressedCells.ShouldBe(0);
    }

    [Test]
    public void InMemoryMeshAggregator_SignalChanged_FiresWhenRatioCrossesDelta()
    {
        var agg = new InMemoryMeshAggregator(signalDelta: 0.05);
        MeshSignal? received = null;
        agg.SignalChanged += (_, s) => received = s;

        // First report: 0 → 1.0 (delta = 1.0 > 0.05) → should fire
        agg.Report("a", MetabolicSignal.Stressed);

        received.ShouldNotBeNull();
    }

    [Test]
    public void InMemoryMeshAggregator_SignalChanged_DoesNotFireForTinyChange()
    {
        var agg = new InMemoryMeshAggregator(signalDelta: 0.5);
        // Seed 10 cells all healthy to get ratio = 0
        for (var i = 0; i < 10; i++)
            agg.Report($"cell-{i}", MetabolicSignal.Healthy);

        // Now capture the initial fired event (ratio went from -1 to 0)
        var fireCount = 0;
        agg.SignalChanged += (_, _) => fireCount++;

        // Stress just 1/10 cells — delta = 0.1, below threshold 0.5
        agg.Report("cell-0", MetabolicSignal.Stressed);

        fireCount.ShouldBe(0);
    }

    [Test]
    public async Task QuorumApprovalGate_BelowThreshold_DelegatesToInner()
    {
        var agg = new InMemoryMeshAggregator();
        agg.Report("a", MetabolicSignal.Healthy);
        agg.Report("b", MetabolicSignal.Healthy);
        agg.Report("c", MetabolicSignal.Healthy);
        // 0% stressed — below threshold of 0.5

        var inner = new Ananke.Organics.Division.Approval.AutoApprovalGate();
        var gate = new QuorumApprovalGate(inner, agg, stressRatioThreshold: 0.5);

        var plan = new DivisionPlan { ParentWorkflow = "a", Reason = "test", Children = Array.Empty<ChildSpec>() };
        var result = await gate.ReviewAsync(plan, MakeSnapshot("a", 2, 1));

        result.IsApproved.ShouldBeTrue();
    }

    [Test]
    public async Task QuorumApprovalGate_AboveThreshold_Rejects()
    {
        var agg = new InMemoryMeshAggregator();
        agg.Report("a", MetabolicSignal.Stressed);
        agg.Report("b", MetabolicSignal.Stressed);
        agg.Report("c", MetabolicSignal.Healthy);
        // 66% stressed — above threshold of 0.5

        var inner = new Ananke.Organics.Division.Approval.AutoApprovalGate();
        var gate = new QuorumApprovalGate(inner, agg, stressRatioThreshold: 0.5);

        var plan = new DivisionPlan { ParentWorkflow = "a", Reason = "test", Children = Array.Empty<ChildSpec>() };
        var result = await gate.ReviewAsync(plan, MakeSnapshot("a", 2, 1));

        result.IsApproved.ShouldBeFalse();
        result.Reason.ShouldContain("quorum");
    }

    [Test]
    public async Task QuorumApprovalGate_ExactlyAtThreshold_Rejects()
    {
        var agg = new InMemoryMeshAggregator();
        agg.Report("a", MetabolicSignal.Stressed);
        agg.Report("b", MetabolicSignal.Healthy);
        // 50% == threshold of 0.5 → should reject

        var inner = new Ananke.Organics.Division.Approval.AutoApprovalGate();
        var gate = new QuorumApprovalGate(inner, agg, stressRatioThreshold: 0.5);

        var plan = new DivisionPlan { ParentWorkflow = "a", Reason = "test", Children = Array.Empty<ChildSpec>() };
        var result = await gate.ReviewAsync(plan, MakeSnapshot("a", 2, 1));

        result.IsApproved.ShouldBeFalse();
    }

    // ══════════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════════

    private static ComplexitySnapshot MakeSnapshot(string name, int toolCount, int clusters) => new()
    {
        WorkflowName = name,
        ToolCount = toolCount,
        JobCount = 1,
        TagClusterCount = clusters,
        RoutingEntropy = 0f,
        ResourceSpan = 1,
        ContextUtilization = 0f,
        MeasuredAt = DateTimeOffset.UtcNow
    };

    private static Ananke.Design.WorkflowManifest MakeManifest(string name) =>
        Ananke.Design.WorkflowManifest.Parse([
            $"name: {name}",
            "models:",
            "  default:",
            "    provider: openai",
            "    model: gpt-4.1",
            "jobs:",
            "  job1:",
            "    type: agent",
            "    model: default",
            "  job2:",
            "    type: agent",
            "    model: default",
            "connections:",
            "  - job1 -> job2",
        ]);

    private static HealthSnapshot MakeHealth(
        string name,
        DateTimeOffset? lastRequest = null,
        DateTimeOffset? observedSince = null,
        int windowSize = 10) => new()
    {
        WorkflowName = name,
        ErrorRate = 0f,
        LatencyTrendSlope = 0f,
        CostTrendSlope = 0f,
        WindowSize = windowSize,
        MeasuredAt = DateTimeOffset.UtcNow,
        LastRequestAt = lastRequest,
        ObservedSince = observedSince ?? DateTimeOffset.UtcNow
    };

    /// <summary>Deterministic <see cref="TimeProvider"/> for test isolation.</summary>
    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
