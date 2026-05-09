using Ananke.Federation.Monitoring;
using Shouldly;

namespace Ananke.Federation.Tests;

[TestFixture]
public sealed class RemoteMetricsTrackerTests
{
    private RemoteMetricsTracker _tracker = null!;

    [SetUp]
    public void SetUp()
    {
        _tracker = new RemoteMetricsTracker(windowSize: 10, minSamplesForTrend: 3);
    }

    [TearDown]
    public void TearDown()
    {
        _tracker.Dispose();
    }

    // ── Recording ────────────────────────────────────────────────────

    [Test]
    public void Record_ZeroExecutions_IsNoOp()
    {
        var metrics = MakeMetrics("dep-1", executionCount: 0, totalTokens: 0, toolCallCount: 0);
        _tracker.Record(metrics);

        _tracker.GetTrend("dep-1").ShouldBeNull();
    }

    [Test]
    public void GetTrend_UnknownDeployment_ReturnsNull()
    {
        _tracker.GetTrend("unknown").ShouldBeNull();
    }

    [Test]
    public void GetTrend_InsufficientSamples_ReturnsNull()
    {
        _tracker.Record(MakeMetrics("dep-1", executionCount: 10, totalTokens: 3000, toolCallCount: 40));
        _tracker.Record(MakeMetrics("dep-1", executionCount: 20, totalTokens: 6500, toolCallCount: 85));

        _tracker.GetTrend("dep-1").ShouldBeNull();
    }

    // ── Trend computation ────────────────────────────────────────────

    [Test]
    public void GetTrend_StableMetrics_ReturnsStable()
    {
        // tokens/exec ~ 300, tool-calls/exec ~ 4, stable across samples
        for (var i = 1; i <= 5; i++)
        {
            _tracker.Record(MakeMetrics("dep-1",
                executionCount: i * 10,
                totalTokens: i * 10 * 300,
                toolCallCount: i * 10 * 4));
        }

        var trend = _tracker.GetTrend("dep-1");

        trend.ShouldNotBeNull();
        trend.IsStable.ShouldBeTrue();
        trend.IsStrugglingGeneralist.ShouldBeFalse();
        trend.SampleCount.ShouldBe(5);
    }

    [Test]
    public void GetTrend_IncreasingTokensAndCalls_DetectsStrugglingGeneralist()
    {
        // tokens/exec increasing: 300, 400, 500, 600, 700
        // tool-calls/exec increasing: 3, 4, 5, 6, 7
        for (var i = 1; i <= 5; i++)
        {
            var tokensPerExec = 200 + i * 100;
            var callsPerExec = 2 + i;
            _tracker.Record(MakeMetrics("dep-1",
                executionCount: i * 10,
                totalTokens: i * 10 * tokensPerExec,
                toolCallCount: i * 10 * callsPerExec));
        }

        var trend = _tracker.GetTrend("dep-1");

        trend.ShouldNotBeNull();
        trend.TokensPerExecutionSlope.ShouldBeGreaterThan(0.05);
        trend.ToolCallsPerExecutionSlope.ShouldBeGreaterThan(0.05);
        trend.IsStrugglingGeneralist.ShouldBeTrue();
    }

    [Test]
    public void GetTrend_DecreasingMetrics_PostDivisionImprovement()
    {
        // tokens/exec decreasing: 700, 600, 500, 400, 300
        for (var i = 1; i <= 5; i++)
        {
            var tokensPerExec = 800 - i * 100;
            var callsPerExec = 8 - i;
            _tracker.Record(MakeMetrics("dep-1",
                executionCount: i * 10,
                totalTokens: i * 10 * tokensPerExec,
                toolCallCount: i * 10 * callsPerExec));
        }

        var trend = _tracker.GetTrend("dep-1");

        trend.ShouldNotBeNull();
        trend.TokensPerExecutionSlope.ShouldBeLessThan(-0.05);
        trend.ToolCallsPerExecutionSlope.ShouldBeLessThan(-0.05);
        trend.IsStrugglingGeneralist.ShouldBeFalse();
    }

    // ── Window management ────────────────────────────────────────────

    [Test]
    public void SlidingWindow_OldSamplesDropped()
    {
        // Window size is 10; add 12 samples
        for (var i = 1; i <= 12; i++)
        {
            _tracker.Record(MakeMetrics("dep-1",
                executionCount: i * 10,
                totalTokens: i * 10 * 300,
                toolCallCount: i * 10 * 4));
        }

        var trend = _tracker.GetTrend("dep-1");
        trend.ShouldNotBeNull();
        trend.SampleCount.ShouldBe(10); // capped at window size
    }

    [Test]
    public void Clear_RemovesDeploymentData()
    {
        for (var i = 1; i <= 5; i++)
            _tracker.Record(MakeMetrics("dep-1", executionCount: i * 10, totalTokens: i * 3000, toolCallCount: i * 40));

        _tracker.GetTrend("dep-1").ShouldNotBeNull();

        _tracker.Clear("dep-1");

        _tracker.GetTrend("dep-1").ShouldBeNull();
    }

    [Test]
    public void GetTrackableDeployments_ReturnsOnlyWithEnoughSamples()
    {
        // dep-1: 5 samples (enough)
        for (var i = 1; i <= 5; i++)
            _tracker.Record(MakeMetrics("dep-1", executionCount: i * 10, totalTokens: i * 3000, toolCallCount: i * 40));

        // dep-2: 2 samples (not enough)
        for (var i = 1; i <= 2; i++)
            _tracker.Record(MakeMetrics("dep-2", executionCount: i * 10, totalTokens: i * 3000, toolCallCount: i * 40));

        var trackable = _tracker.GetTrackableDeployments();
        trackable.ShouldContain("dep-1");
        trackable.ShouldNotContain("dep-2");
    }

    // ── MetricsSample.FromMetrics ────────────────────────────────────

    [Test]
    public void MetricsSample_FromMetrics_ComputesPerExecutionAverages()
    {
        var metrics = MakeMetrics("dep-1", executionCount: 100, totalTokens: 50_000, toolCallCount: 400);
        var sample = MetricsSample.FromMetrics(metrics);

        sample.ShouldNotBeNull();
        sample.TokensPerExecution.ShouldBe(500);
        sample.ToolCallsPerExecution.ShouldBe(4);
    }

    [Test]
    public void MetricsSample_FromMetrics_ZeroExecutions_ReturnsNull()
    {
        var metrics = MakeMetrics("dep-1", executionCount: 0, totalTokens: 0, toolCallCount: 0);
        MetricsSample.FromMetrics(metrics).ShouldBeNull();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static RemoteCellMetrics MakeMetrics(
        string deploymentId,
        long executionCount,
        long totalTokens,
        long toolCallCount,
        double errorRate = 0) => new()
    {
        DeploymentId = deploymentId,
        ExecutionCount = executionCount,
        TotalTokens = totalTokens,
        ToolCallCount = toolCallCount,
        ErrorRate = errorRate,
        MeasuredAt = DateTimeOffset.UtcNow
    };
}
