namespace Ananke.Federation.Monitoring;

/// <summary>
/// A point-in-time sample of per-execution averages for a remote cell.
/// Computed from <see cref="RemoteCellMetrics"/> by normalising totals
/// against execution count.
/// </summary>
public sealed record MetricsSample
{
    /// <summary>Average tokens consumed per execution.</summary>
    public required double TokensPerExecution { get; init; }

    /// <summary>Average tool calls per execution.</summary>
    public required double ToolCallsPerExecution { get; init; }

    /// <summary>Error rate as a fraction (0.0–1.0).</summary>
    public required double ErrorRate { get; init; }

    /// <summary>Total executions at the time of sampling.</summary>
    public required long ExecutionCount { get; init; }

    /// <summary>When this sample was taken.</summary>
    public required DateTimeOffset SampledAt { get; init; }

    /// <summary>
    /// Creates a <see cref="MetricsSample"/> from raw platform metrics.
    /// Returns <see langword="null"/> if execution count is zero (no data).
    /// </summary>
    public static MetricsSample? FromMetrics(RemoteCellMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        if (metrics.ExecutionCount == 0)
            return null;

        return new MetricsSample
        {
            TokensPerExecution = (double)metrics.TotalTokens / metrics.ExecutionCount,
            ToolCallsPerExecution = (double)metrics.ToolCallCount / metrics.ExecutionCount,
            ErrorRate = metrics.ErrorRate,
            ExecutionCount = metrics.ExecutionCount,
            SampledAt = metrics.MeasuredAt
        };
    }
}
