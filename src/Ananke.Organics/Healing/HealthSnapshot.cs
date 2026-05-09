using Ananke.Organics.Division;

namespace Ananke.Organics.Healing;

/// <summary>
/// Operational fitness metrics for a workflow cell. Measures health
/// (error rate, latency/cost trends) — reactive signals for the healing
/// policy. Separate from <see cref="ComplexitySnapshot"/> which measures
/// structural pressure for the division policy.
/// </summary>
/// <remarks>
/// <para>
/// In biology, cells don't divide because they're sick — sick cells die
/// (apoptosis). <see cref="HealthSnapshot"/> detects sickness;
/// <see cref="ComplexitySnapshot"/> detects growth pressure. Different
/// signals, different responses.
/// </para>
/// </remarks>
public sealed record HealthSnapshot
{
    /// <summary>Name of the cell this snapshot describes.</summary>
    public required string WorkflowName { get; init; }

    /// <summary>
    /// Error rate over the sliding window (0.0–1.0). Fraction of executions
    /// that failed, regardless of origin. This is the total error rate.
    /// </summary>
    public required float ErrorRate { get; init; }

    /// <summary>
    /// Fraction of executions that failed due to workflow-internal errors
    /// (logic bugs, state mapping, missing tools). These indicate the
    /// workflow itself is broken — healing is warranted.
    /// </summary>
    public float WorkflowErrorRate { get; init; }

    /// <summary>
    /// Fraction of executions that failed due to upstream/external errors
    /// (API timeouts, rate limits, model refusals). These indicate a
    /// dependency is unhealthy — the workflow itself may be fine.
    /// </summary>
    public float UpstreamErrorRate { get; init; }

    /// <summary>
    /// Fraction of executions where the agent completed successfully but
    /// deflected — couldn't meaningfully serve the request because the
    /// tools or domain don't match the prompt. This is NOT a health signal
    /// (the cell is healthy) but a routing signal (the cell is mismatched).
    /// Feeds into <c>RoutingAffinityTracker</c> as negative affinity.
    /// </summary>
    public float CapabilityMismatchRate { get; init; }

    /// <summary>
    /// Latency trend slope: positive = getting slower over the window.
    /// Computed via linear regression over execution latencies.
    /// </summary>
    public required float LatencyTrendSlope { get; init; }

    /// <summary>
    /// Average routing outcome score from <c>RoutingAffinityTracker</c>
    /// (if available). Low scores indicate the cell is receiving misrouted
    /// prompts. Range: [-1.0, 1.0]. Default 0 when no routing data.
    /// </summary>
    public float AvgRoutingScore { get; init; }

    /// <summary>
    /// Cost trend slope: positive = getting more expensive per execution
    /// over the window. Computed via linear regression over execution costs.
    /// </summary>
    public required float CostTrendSlope { get; init; }

    /// <summary>Number of executions in the measurement window.</summary>
    public required int WindowSize { get; init; }

    /// <summary>When this snapshot was measured.</summary>
    public required DateTimeOffset MeasuredAt { get; init; }

    // ── Apoptosis fields (L4) ───────────────────────────────────────

    /// <summary>
    /// Timestamp of the most recent execution observed for this cell.
    /// <see langword="null"/> if no executions have been recorded.
    /// Used by <c>IdleCellPrunePolicy</c> to detect idle cells.
    /// </summary>
    public DateTimeOffset? LastRequestAt { get; init; }

    /// <summary>
    /// Timestamp when monitoring began for this cell (first execution recorded).
    /// Used as a fallback by <c>IdleCellPrunePolicy</c> when
    /// <see cref="LastRequestAt"/> is <see langword="null"/>.
    /// </summary>
    public DateTimeOffset ObservedSince { get; init; } = DateTimeOffset.UtcNow;
}
