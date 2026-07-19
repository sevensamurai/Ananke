using Ananke.Organics.Division;
using Ananke.Organics.Healing;
using Shouldly;

namespace Ananke.Organics.Tests.Division;

[TestFixture]
public class ThresholdHealingPolicyTests
{
    private ThresholdHealingPolicy _policy = null!;

    [SetUp]
    public void SetUp()
    {
        _policy = new ThresholdHealingPolicy
        {
            ErrorRateThreshold = 0.3f,
            ConsecutiveFailureWindows = 3
        };
    }

    private static HealthSnapshot MakeHealth(
        string name = "cell-a", float errorRate = 0f,
        float latencySlope = 0f, float costSlope = 0f,
        float workflowErrorRate = 0f, float upstreamErrorRate = 0f) => new()
        {
            WorkflowName = name,
            ErrorRate = errorRate,
            WorkflowErrorRate = workflowErrorRate,
            UpstreamErrorRate = upstreamErrorRate,
            LatencyTrendSlope = latencySlope,
            CostTrendSlope = costSlope,
            WindowSize = 10,
            MeasuredAt = DateTimeOffset.UtcNow
        };

    private static ComplexitySnapshot MakeComplexity(
        string name = "cell-a", int toolCount = 4) => new()
        {
            WorkflowName = name,
            ToolCount = toolCount,
            JobCount = 2,
            TagClusterCount = 1,
            RoutingEntropy = 0.5f,
            ResourceSpan = 2,
            ContextUtilization = 0.2f,
            MeasuredAt = DateTimeOffset.UtcNow
        };

    [Test]
    public async Task Healthy_ReturnsNull()
    {
        var result = await _policy.EvaluateAsync(
            MakeHealth(errorRate: 0.1f),
            MakeComplexity());

        result.ShouldBeNull();
    }

    [Test]
    public async Task SingleWindow_AboveThreshold_ReturnsNull()
    {
        // First evaluation above threshold — not yet sustained
        var result = await _policy.EvaluateAsync(
            MakeHealth(errorRate: 0.5f),
            MakeComplexity());

        result.ShouldBeNull();
    }

    [Test]
    public async Task TwoWindows_AboveThreshold_StillNull()
    {
        await _policy.EvaluateAsync(MakeHealth(errorRate: 0.5f), MakeComplexity());
        var result = await _policy.EvaluateAsync(MakeHealth(errorRate: 0.5f), MakeComplexity());

        result.ShouldBeNull();
    }

    [Test]
    public async Task ThreeConsecutiveWindows_TriggersHealing()
    {
        await _policy.EvaluateAsync(MakeHealth(errorRate: 0.5f), MakeComplexity());
        await _policy.EvaluateAsync(MakeHealth(errorRate: 0.5f), MakeComplexity());
        var result = await _policy.EvaluateAsync(MakeHealth(errorRate: 0.5f), MakeComplexity());

        result.ShouldNotBeNull();
        result!.WorkflowName.ShouldBe("cell-a");
        result.Strategy.ShouldBe(HealingStrategy.Rollback);
        result.TriggeringHealth.ErrorRate.ShouldBe(0.5f);
    }

    [Test]
    public async Task RecoveryBetweenWindows_ResetsCounter()
    {
        // Two bad, then one good, then two bad — should NOT trigger
        await _policy.EvaluateAsync(MakeHealth(errorRate: 0.5f), MakeComplexity());
        await _policy.EvaluateAsync(MakeHealth(errorRate: 0.5f), MakeComplexity());
        await _policy.EvaluateAsync(MakeHealth(errorRate: 0.1f), MakeComplexity()); // recovery
        await _policy.EvaluateAsync(MakeHealth(errorRate: 0.5f), MakeComplexity());
        var result = await _policy.EvaluateAsync(MakeHealth(errorRate: 0.5f), MakeComplexity());

        result.ShouldBeNull(); // only 2 consecutive, not 3
    }

    [Test]
    public async Task HighLatencySlope_RecommendsRestart()
    {
        var policy = new ThresholdHealingPolicy
        {
            ErrorRateThreshold = 0.3f,
            ConsecutiveFailureWindows = 1, // trigger immediately for this test
            LatencySlopeRestartThreshold = 5.0f
        };

        var result = await policy.EvaluateAsync(
            MakeHealth(errorRate: 0.5f, latencySlope: 10.0f),
            MakeComplexity());

        result.ShouldNotBeNull();
        result!.Strategy.ShouldBe(HealingStrategy.Restart);
        result.Reason.ShouldContain("context bloat");
    }

    [Test]
    public async Task FlatLatency_RecommendsRollback()
    {
        var policy = new ThresholdHealingPolicy
        {
            ErrorRateThreshold = 0.3f,
            ConsecutiveFailureWindows = 1
        };

        var result = await policy.EvaluateAsync(
            MakeHealth(errorRate: 0.5f, latencySlope: 1.0f),
            MakeComplexity());

        result.ShouldNotBeNull();
        result!.Strategy.ShouldBe(HealingStrategy.Rollback);
        result.Reason.ShouldContain("configuration issue");
    }

    [Test]
    public async Task ComplexityGate_HighToolCount_ReturnsNull()
    {
        var policy = new ThresholdHealingPolicy
        {
            ErrorRateThreshold = 0.3f,
            ConsecutiveFailureWindows = 1,
            MaxComplexityForHealing = 6
        };

        // Cell has 10 tools — too complex, should divide not heal
        var result = await policy.EvaluateAsync(
            MakeHealth(errorRate: 0.8f),
            MakeComplexity(toolCount: 10));

        result.ShouldBeNull();
    }

    [Test]
    public async Task ComplexityGate_LowToolCount_Heals()
    {
        var policy = new ThresholdHealingPolicy
        {
            ErrorRateThreshold = 0.3f,
            ConsecutiveFailureWindows = 1,
            MaxComplexityForHealing = 6
        };

        var result = await policy.EvaluateAsync(
            MakeHealth(errorRate: 0.8f),
            MakeComplexity(toolCount: 4));

        result.ShouldNotBeNull();
    }

    [Test]
    public async Task AfterHealing_CounterResets()
    {
        // Trigger healing (3 consecutive)
        await _policy.EvaluateAsync(MakeHealth(errorRate: 0.5f), MakeComplexity());
        await _policy.EvaluateAsync(MakeHealth(errorRate: 0.5f), MakeComplexity());
        var result = await _policy.EvaluateAsync(MakeHealth(errorRate: 0.5f), MakeComplexity());
        result.ShouldNotBeNull();

        // Counter is now reset — next evaluation should not trigger immediately
        var next = await _policy.EvaluateAsync(MakeHealth(errorRate: 0.5f), MakeComplexity());
        next.ShouldBeNull(); // only 1 window since reset
    }

    [Test]
    public async Task IndependentCells_TrackedSeparately()
    {
        // cell-a fails, cell-b is healthy
        await _policy.EvaluateAsync(MakeHealth("cell-a", errorRate: 0.5f), MakeComplexity("cell-a"));
        await _policy.EvaluateAsync(MakeHealth("cell-a", errorRate: 0.5f), MakeComplexity("cell-a"));
        await _policy.EvaluateAsync(MakeHealth("cell-a", errorRate: 0.5f), MakeComplexity("cell-a"));

        // cell-b should not be affected
        var resultB = await _policy.EvaluateAsync(MakeHealth("cell-b", errorRate: 0.5f), MakeComplexity("cell-b"));
        resultB.ShouldBeNull(); // only 1 window for cell-b
    }

    [Test]
    public void Reset_ClearsCounter()
    {
        // Accumulate 2 failures
        _policy.EvaluateAsync(MakeHealth(errorRate: 0.5f), MakeComplexity()).GetAwaiter().GetResult();
        _policy.EvaluateAsync(MakeHealth(errorRate: 0.5f), MakeComplexity()).GetAwaiter().GetResult();

        // Reset
        _policy.Reset("cell-a");

        // Should need 3 more consecutive to trigger
        _policy.EvaluateAsync(MakeHealth(errorRate: 0.5f), MakeComplexity()).GetAwaiter().GetResult().ShouldBeNull();
        _policy.EvaluateAsync(MakeHealth(errorRate: 0.5f), MakeComplexity()).GetAwaiter().GetResult().ShouldBeNull();
    }

    [Test]
    public async Task ReasonIncludesMetrics()
    {
        var policy = new ThresholdHealingPolicy
        {
            ErrorRateThreshold = 0.3f,
            ConsecutiveFailureWindows = 1
        };

        var result = await policy.EvaluateAsync(
            MakeHealth(errorRate: 0.45f),
            MakeComplexity());

        result.ShouldNotBeNull();
        result!.Reason.ShouldContain("45%");
        result.Reason.ShouldContain("30%");
    }

    // ── Failure origin classification tests ─────────────────────────

    [Test]
    public async Task UpstreamOnlyErrors_DoNotTriggerHealing()
    {
        var policy = new ThresholdHealingPolicy
        {
            ErrorRateThreshold = 0.3f,
            ConsecutiveFailureWindows = 1
        };

        // 80% total error rate, but ALL errors are upstream — workflow is fine
        var result = await policy.EvaluateAsync(
            MakeHealth(errorRate: 0.8f, workflowErrorRate: 0f, upstreamErrorRate: 0.8f),
            MakeComplexity());

        result.ShouldBeNull();
    }

    [Test]
    public async Task WorkflowErrors_TriggerHealing()
    {
        var policy = new ThresholdHealingPolicy
        {
            ErrorRateThreshold = 0.3f,
            ConsecutiveFailureWindows = 1
        };

        // 50% workflow errors — workflow itself is broken
        var result = await policy.EvaluateAsync(
            MakeHealth(errorRate: 0.5f, workflowErrorRate: 0.5f, upstreamErrorRate: 0f),
            MakeComplexity());

        result.ShouldNotBeNull();
    }

    [Test]
    public async Task MixedErrors_OnlyWorkflowRateCounts()
    {
        var policy = new ThresholdHealingPolicy
        {
            ErrorRateThreshold = 0.3f,
            ConsecutiveFailureWindows = 1
        };

        // 60% total errors: 20% workflow + 40% upstream
        // Only 20% workflow → below 30% threshold → no healing
        var result = await policy.EvaluateAsync(
            MakeHealth(errorRate: 0.6f, workflowErrorRate: 0.2f, upstreamErrorRate: 0.4f),
            MakeComplexity());

        result.ShouldBeNull();
    }

    [Test]
    public async Task UnclassifiedErrors_FallBackToTotalRate()
    {
        var policy = new ThresholdHealingPolicy
        {
            ErrorRateThreshold = 0.3f,
            ConsecutiveFailureWindows = 1
        };

        // 50% total errors, but no classification available (both rates = 0)
        // Falls back to total ErrorRate
        var result = await policy.EvaluateAsync(
            MakeHealth(errorRate: 0.5f, workflowErrorRate: 0f, upstreamErrorRate: 0f),
            MakeComplexity());

        result.ShouldNotBeNull();
    }

    [Test]
    public async Task CapabilityMismatchOnly_DoesNotTriggerHealing()
    {
        var policy = new ThresholdHealingPolicy
        {
            ErrorRateThreshold = 0.3f,
            ConsecutiveFailureWindows = 1
        };

        // Cell has 0% workflow errors, 0% upstream errors, 60% capability mismatch
        // The cell is healthy — it just can't serve these requests. Don't heal.
        var health = new HealthSnapshot
        {
            WorkflowName = "cell-a",
            ErrorRate = 0f,
            WorkflowErrorRate = 0f,
            UpstreamErrorRate = 0f,
            CapabilityMismatchRate = 0.6f,
            LatencyTrendSlope = 0f,
            CostTrendSlope = 0f,
            WindowSize = 10,
            MeasuredAt = DateTimeOffset.UtcNow
        };

        var result = await policy.EvaluateAsync(health, MakeComplexity());
        result.ShouldBeNull();
    }
}
