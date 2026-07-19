using Ananke.Federation.Google;
using Ananke.Federation.Google.Observability;
using Shouldly;

namespace Ananke.Federation.Google.Tests;

[TestFixture]
public sealed class VertexAIRemoteCellMonitorTests
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Fake observability client
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class FakeObservabilityClient : IAgentObservabilityClient
    {
        private readonly IReadOnlyList<TraceRecord> _traces;
        private readonly ObservabilitySnapshot? _snapshot;

        public FakeObservabilityClient(
            IReadOnlyList<TraceRecord>? traces = null,
            ObservabilitySnapshot? snapshot = null)
        {
            _traces = traces ?? [];
            _snapshot = snapshot;
        }

        public Task<IReadOnlyList<TraceRecord>> GetTracesAsync(
            string deploymentId, int lookBackMinutes, CancellationToken ct = default) =>
            Task.FromResult(_traces);

        public Task<ObservabilitySnapshot?> GetMetricsSnapshotAsync(
            string deploymentId, int lookBackMinutes, CancellationToken ct = default) =>
            Task.FromResult(_snapshot);
    }

    private sealed class ThrowingObservabilityClient : IAgentObservabilityClient
    {
        public Task<IReadOnlyList<TraceRecord>> GetTracesAsync(
            string deploymentId, int lookBackMinutes, CancellationToken ct = default) =>
            throw new HttpRequestException("Cloud Trace unavailable.");

        public Task<ObservabilitySnapshot?> GetMetricsSnapshotAsync(
            string deploymentId, int lookBackMinutes, CancellationToken ct = default) =>
            throw new HttpRequestException("Cloud Monitoring unavailable.");
    }

    private static VertexAIRemoteCellMonitor Make(
        IAgentObservabilityClient? client = null,
        RemoteCellMonitorOptions? options = null) =>
        new(client ?? new FakeObservabilityClient(), options);

    private static TraceRecord GoodTrace(double latencyMs = 200) => new()
    {
        StartTime = DateTimeOffset.UtcNow,
        LatencyMs = latencyMs,
        IsError = false
    };

    private static TraceRecord ErrorTrace(double latencyMs = 300) => new()
    {
        StartTime = DateTimeOffset.UtcNow,
        LatencyMs = latencyMs,
        IsError = true
    };

    // ─────────────────────────────────────────────────────────────────────────
    //  Platform property
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void Platform_is_vertex_ai()
    {
        Make().Platform.ShouldBe("vertex-ai");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GetHealthAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task GetHealth_no_traces_returns_healthy_baseline()
    {
        var monitor = Make(new FakeObservabilityClient(traces: []));

        var health = await monitor.GetHealthAsync("dep-1");

        health.DeploymentId.ShouldBe("dep-1");
        health.IsHealthy.ShouldBeTrue();
        health.ErrorCount.ShouldBe(0);
        health.LatencyMs.ShouldBe(0);
    }

    [Test]
    public async Task GetHealth_all_good_traces_is_healthy()
    {
        var traces = new[] { GoodTrace(100), GoodTrace(200), GoodTrace(150) };
        var monitor = Make(new FakeObservabilityClient(traces));

        var health = await monitor.GetHealthAsync("dep-1");

        health.IsHealthy.ShouldBeTrue();
        health.ErrorCount.ShouldBe(0);
        health.LatencyMs.ShouldBe(150, tolerance: 1);
    }

    [Test]
    public async Task GetHealth_high_error_rate_is_unhealthy()
    {
        // 3/3 errors → error rate = 1.0, above 0.10 threshold
        var traces = new[] { ErrorTrace(), ErrorTrace(), ErrorTrace() };
        var monitor = Make(new FakeObservabilityClient(traces));

        var health = await monitor.GetHealthAsync("dep-1");

        health.IsHealthy.ShouldBeFalse();
        health.ErrorCount.ShouldBe(3);
    }

    [Test]
    public async Task GetHealth_below_threshold_error_rate_is_healthy()
    {
        // 1/20 errors → error rate = 0.05, below default 0.10 threshold
        var traces = Enumerable.Range(0, 19).Select(_ => GoodTrace())
            .Append(ErrorTrace())
            .ToList();
        var monitor = Make(new FakeObservabilityClient(traces));

        var health = await monitor.GetHealthAsync("dep-1");

        health.IsHealthy.ShouldBeTrue();
    }

    [Test]
    public async Task GetHealth_high_latency_is_unhealthy()
    {
        var options = new RemoteCellMonitorOptions { LatencyThresholdMs = 500 };
        var traces = new[] { GoodTrace(600), GoodTrace(700) };
        var monitor = Make(new FakeObservabilityClient(traces), options);

        var health = await monitor.GetHealthAsync("dep-1");

        health.IsHealthy.ShouldBeFalse();
    }

    [Test]
    public async Task GetHealth_observability_api_error_returns_unhealthy()
    {
        var monitor = Make(new ThrowingObservabilityClient());

        var health = await monitor.GetHealthAsync("dep-1");

        health.DeploymentId.ShouldBe("dep-1");
        health.IsHealthy.ShouldBeFalse();
    }

    [Test]
    public async Task GetHealth_respects_custom_error_rate_threshold()
    {
        // 3/10 = 30% errors; unhealthy at 20% threshold but healthy at 40% threshold
        var traces = Enumerable.Range(0, 7).Select(_ => GoodTrace())
            .Concat(Enumerable.Range(0, 3).Select(_ => ErrorTrace()))
            .ToList();

        var strictOptions = new RemoteCellMonitorOptions { ErrorRateThreshold = 0.20 };
        var lenientOptions = new RemoteCellMonitorOptions { ErrorRateThreshold = 0.40 };

        (await Make(new FakeObservabilityClient(traces), strictOptions).GetHealthAsync("d"))
            .IsHealthy.ShouldBeFalse();

        (await Make(new FakeObservabilityClient(traces), lenientOptions).GetHealthAsync("d"))
            .IsHealthy.ShouldBeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GetMetricsAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task GetMetrics_no_snapshot_returns_zero_baseline()
    {
        var monitor = Make(new FakeObservabilityClient(snapshot: null));

        var metrics = await monitor.GetMetricsAsync("dep-1");

        metrics.DeploymentId.ShouldBe("dep-1");
        metrics.ExecutionCount.ShouldBe(0);
        metrics.TotalTokens.ShouldBe(0);
        metrics.ErrorRate.ShouldBe(0);
    }

    [Test]
    public async Task GetMetrics_maps_snapshot_fields()
    {
        var snapshot = new ObservabilitySnapshot
        {
            ExecutionCount = 42,
            TotalTokens = 8_500,
            ToolCallCount = 17,
            ErrorRate = 0.04
        };
        var monitor = Make(new FakeObservabilityClient(snapshot: snapshot));

        var metrics = await monitor.GetMetricsAsync("dep-1");

        metrics.ExecutionCount.ShouldBe(42);
        metrics.TotalTokens.ShouldBe(8_500);
        metrics.ToolCallCount.ShouldBe(17);
        metrics.ErrorRate.ShouldBe(0.04);
    }

    [Test]
    public async Task GetMetrics_observability_api_error_returns_zero_baseline()
    {
        var monitor = Make(new ThrowingObservabilityClient());

        var metrics = await monitor.GetMetricsAsync("dep-1");

        metrics.ExecutionCount.ShouldBe(0);
        metrics.ErrorRate.ShouldBe(0);
    }
}
