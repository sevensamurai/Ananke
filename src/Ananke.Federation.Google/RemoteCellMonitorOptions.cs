namespace Ananke.Federation.Google;

/// <summary>
/// Configuration options for <see cref="VertexAIRemoteCellMonitor"/>.
/// </summary>
public sealed record RemoteCellMonitorOptions
{
    /// <summary>
    /// How many minutes of history to query for health and metrics calculations.
    /// Defaults to 15 minutes.
    /// </summary>
    public int LookBackMinutes { get; init; } = 15;

    /// <summary>
    /// Error-rate fraction above which the cell is considered unhealthy (0.0–1.0).
    /// Defaults to 0.10 (10%).
    /// </summary>
    public double ErrorRateThreshold { get; init; } = 0.10;

    /// <summary>
    /// Average latency (ms) above which the cell is considered unhealthy.
    /// Defaults to 10 000 ms (10 s).
    /// </summary>
    public double LatencyThresholdMs { get; init; } = 10_000;
}
