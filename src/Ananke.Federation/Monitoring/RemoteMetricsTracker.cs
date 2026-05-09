using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Ananke.Federation.Monitoring;

/// <summary>
/// Maintains a sliding window of <see cref="MetricsSample"/>s per deployment
/// and computes <see cref="RemoteCellTrend"/>s using linear regression over
/// the window.
/// </summary>
/// <remarks>
/// <para>
/// The tracker is designed to be called periodically by the polling loop in
/// <c>OrganicHost</c> (via <c>FederatedComplexityMonitor</c>). Each poll
/// produces a new <see cref="RemoteCellMetrics"/> which is converted to a
/// <see cref="MetricsSample"/> and recorded here.
/// </para>
/// <para>
/// Trend computation requires at least <see cref="MinSamplesForTrend"/>
/// samples. Until then, <see cref="GetTrend"/> returns <see langword="null"/>.
/// </para>
/// <para>
/// <b>OpenTelemetry integration:</b> Each <see cref="Record"/> call emits
/// observable gauge measurements via <see cref="System.Diagnostics.Metrics.Meter"/>.
/// Add the <see cref="MeterName"/> source to your OTEL metrics pipeline to
/// export to Prometheus, Grafana, or any OTLP-compatible backend for long-term
/// trend observation.
/// </para>
/// <para>
/// <b>NOTE:</b> Detection of "add tool → tension rises" patterns will be
/// handled by the <c>nnke-platform</c> CLI analysis commands (Phase 6).
/// This tracker provides the raw signal; the CLI interprets it in context
/// of manifest changes.
/// </para>
/// </remarks>
/// <param name="windowSize">Maximum samples to retain per deployment. Default: 20.</param>
/// <param name="minSamplesForTrend">Minimum samples required to compute a trend. Default: 5.</param>
public sealed class RemoteMetricsTracker(int windowSize = 20, int minSamplesForTrend = 5) : IDisposable
{
    /// <summary>
    /// The <see cref="System.Diagnostics.Metrics.Meter"/> name used for OTEL export.
    /// Add this to your OpenTelemetry metrics pipeline:
    /// <c>builder.AddMeter(RemoteMetricsTracker.MeterName)</c>.
    /// </summary>
    public const string MeterName = "Ananke.Federation";

    private readonly Meter _meter = new(MeterName, "1.0.0");
    private readonly ConcurrentDictionary<string, SlidingWindow> _windows = new();
    private readonly ConcurrentDictionary<string, MetricsSample> _latestSamples = new();

    /// <summary>Minimum samples required before trend computation is possible.</summary>
    public int MinSamplesForTrend => minSamplesForTrend;

    /// <summary>
    /// Records a metrics sample for a deployment. Converts raw
    /// <see cref="RemoteCellMetrics"/> to a normalised <see cref="MetricsSample"/>.
    /// No-op if execution count is zero. Also emits OTEL gauge measurements.
    /// </summary>
    /// <param name="metrics">Raw platform metrics.</param>
    public void Record(RemoteCellMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        var sample = MetricsSample.FromMetrics(metrics);
        if (sample is null)
            return;

        var window = _windows.GetOrAdd(metrics.DeploymentId, _ => new SlidingWindow(windowSize));
        window.Add(sample);
        _latestSamples[metrics.DeploymentId] = sample;

        EmitSampleMetrics(metrics.DeploymentId, sample);
    }

    /// <summary>
    /// Computes the current trend for a deployment. Returns <see langword="null"/>
    /// if insufficient samples have been recorded.
    /// </summary>
    /// <param name="deploymentId">Deployment to compute trend for.</param>
    public RemoteCellTrend? GetTrend(string deploymentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentId);

        if (!_windows.TryGetValue(deploymentId, out var window))
            return null;

        var samples = window.GetSamples();
        if (samples.Count < minSamplesForTrend)
            return null;

        var tokensSlope = ComputeNormalisedSlope(samples, s => s.TokensPerExecution);
        var toolCallsSlope = ComputeNormalisedSlope(samples, s => s.ToolCallsPerExecution);
        var errorSlope = ComputeNormalisedSlope(samples, s => s.ErrorRate);

        return new RemoteCellTrend
        {
            DeploymentId = deploymentId,
            TokensPerExecutionSlope = tokensSlope,
            ToolCallsPerExecutionSlope = toolCallsSlope,
            ErrorRateSlope = errorSlope,
            SampleCount = samples.Count,
            ComputedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Returns all deployment IDs that have a computable trend
    /// (at least <see cref="MinSamplesForTrend"/> samples).
    /// </summary>
    public IReadOnlyList<string> GetTrackableDeployments()
    {
        return _windows
            .Where(kv => kv.Value.Count >= minSamplesForTrend)
            .Select(kv => kv.Key)
            .ToList();
    }

    /// <summary>
    /// Removes all recorded samples for a deployment (e.g. after teardown).
    /// </summary>
    public void Clear(string deploymentId)
    {
        _windows.TryRemove(deploymentId, out _);
        _latestSamples.TryRemove(deploymentId, out _);
    }

    /// <summary>
    /// Computes a normalised slope via simple linear regression.
    /// The slope is normalised against the mean value so it represents
    /// relative change per sample interval (e.g. +0.1 = 10% increase per interval).
    /// </summary>
    private static double ComputeNormalisedSlope(
        IReadOnlyList<MetricsSample> samples,
        Func<MetricsSample, double> selector)
    {
        var n = samples.Count;
        if (n < 2)
            return 0;

        // Simple linear regression: y = mx + b where x = sample index
        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        for (var i = 0; i < n; i++)
        {
            var y = selector(samples[i]);
            sumX += i;
            sumY += y;
            sumXY += i * y;
            sumX2 += i * i;
        }

        var denominator = n * sumX2 - sumX * sumX;
        if (Math.Abs(denominator) < 1e-10)
            return 0;

        var slope = (n * sumXY - sumX * sumY) / denominator;

        // Normalise: divide by mean to get relative change
        var mean = sumY / n;
        if (Math.Abs(mean) < 1e-10)
            return 0;

        return slope / mean;
    }

    // ── OTEL Metrics ─────────────────────────────────────────────────

    private Histogram<double>? _tokensPerExecHistogram;
    private Histogram<double>? _toolCallsPerExecHistogram;
    private Histogram<double>? _errorRateHistogram;

    private void EmitSampleMetrics(string deploymentId, MetricsSample sample)
    {
        _tokensPerExecHistogram ??= _meter.CreateHistogram<double>(
            "ananke.federation.tokens_per_execution",
            unit: "tokens",
            description: "Tokens consumed per execution for a remote cell");

        _toolCallsPerExecHistogram ??= _meter.CreateHistogram<double>(
            "ananke.federation.tool_calls_per_execution",
            unit: "{calls}",
            description: "Tool calls per execution for a remote cell");

        _errorRateHistogram ??= _meter.CreateHistogram<double>(
            "ananke.federation.error_rate",
            unit: "1",
            description: "Error rate for a remote cell");

        var tags = new TagList { { "deployment_id", deploymentId } };

        _tokensPerExecHistogram.Record(sample.TokensPerExecution, tags);
        _toolCallsPerExecHistogram.Record(sample.ToolCallsPerExecution, tags);
        _errorRateHistogram.Record(sample.ErrorRate, tags);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _meter.Dispose();
    }

    /// <summary>Thread-safe sliding window of metric samples.</summary>
    private sealed class SlidingWindow(int maxSize)
    {
        private readonly List<MetricsSample> _samples = new(maxSize);
        private readonly Lock _lock = new();

        public int Count
        {
            get
            {
                lock (_lock) return _samples.Count;
            }
        }

        public void Add(MetricsSample sample)
        {
            lock (_lock)
            {
                if (_samples.Count >= maxSize)
                    _samples.RemoveAt(0);
                _samples.Add(sample);
            }
        }

        public IReadOnlyList<MetricsSample> GetSamples()
        {
            lock (_lock) return [.. _samples];
        }
    }
}
