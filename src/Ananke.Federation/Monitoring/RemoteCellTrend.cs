namespace Ananke.Federation.Monitoring;

/// <summary>
/// Computed trend for a remote cell's execution metrics. Indicates whether
/// per-execution cost (tokens, tool calls) is increasing, stable, or
/// decreasing relative to a baseline window.
/// </summary>
/// <remarks>
/// <para>
/// Trend direction is expressed as a normalised slope: positive means the
/// metric is increasing over time (generalist struggling), negative means
/// improving (specialist succeeding). Magnitude is unitless — it represents
/// relative change per sample interval.
/// </para>
/// <para>
/// A rising <see cref="TokensPerExecutionSlope"/> combined with rising
/// <see cref="ToolCallsPerExecutionSlope"/> strongly suggests the agent is
/// picking wrong tools and retrying. This is the primary signal for
/// federation-level division.
/// </para>
/// </remarks>
public sealed record RemoteCellTrend
{
    /// <summary>Deployment identifier.</summary>
    public required string DeploymentId { get; init; }

    /// <summary>
    /// Normalised slope of tokens-per-execution over the sample window.
    /// Positive = increasing (worse), negative = decreasing (better).
    /// </summary>
    public required double TokensPerExecutionSlope { get; init; }

    /// <summary>
    /// Normalised slope of tool-calls-per-execution over the sample window.
    /// Positive = increasing (worse), negative = decreasing (better).
    /// </summary>
    public required double ToolCallsPerExecutionSlope { get; init; }

    /// <summary>
    /// Normalised slope of error rate over the sample window.
    /// Positive = increasing (worse), negative = decreasing (better).
    /// </summary>
    public required double ErrorRateSlope { get; init; }

    /// <summary>Number of samples used to compute this trend.</summary>
    public required int SampleCount { get; init; }

    /// <summary>When this trend was computed.</summary>
    public required DateTimeOffset ComputedAt { get; init; }

    /// <summary>
    /// Whether the trend indicates a struggling generalist: both tokens and
    /// tool calls per execution are increasing.
    /// </summary>
    public bool IsStrugglingGeneralist =>
        TokensPerExecutionSlope > 0.05 && ToolCallsPerExecutionSlope > 0.05;

    /// <summary>
    /// Whether the trend is stable (slopes near zero within tolerance).
    /// </summary>
    public bool IsStable =>
        Math.Abs(TokensPerExecutionSlope) <= 0.05 &&
        Math.Abs(ToolCallsPerExecutionSlope) <= 0.05;
}
