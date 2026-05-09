using Ananke.Learning.EmpiricalMemory;

namespace Ananke.Organics.Division;

/// <summary>
/// Structural complexity metrics for a workflow cell. These measure surface
/// tension (structural pressure to divide), not health (failure/latency).
/// Division is triggered proactively from complexity, not reactively from failure.
/// </summary>
/// <remarks>
/// <para>
/// In biology, cells don't divide because they're sick — sick cells die
/// (apoptosis). Cells divide because they've grown too large for their
/// surface-area-to-volume ratio to sustain internal processes. The same
/// principle applies: a workflow divides while it is still healthy and capable
/// of performing the division cleanly.
/// </para>
/// <para>
/// Structural metrics (<see cref="ToolCount"/>, <see cref="TagClusterCount"/>,
/// <see cref="ResourceSpan"/>, <see cref="ContextUtilization"/>) can be
/// computed from the manifest and tool definitions without execution history.
/// Telemetry metrics (<see cref="RoutingEntropy"/>) require recorded executions.
/// </para>
/// </remarks>
public sealed record ComplexitySnapshot
{
    /// <summary>Name of the cell this snapshot describes.</summary>
    public required string WorkflowName { get; init; }

    // ── Surface tension (structural) ────────────────────────────────

    /// <summary>Total tools bound across all agent jobs.</summary>
    public required int ToolCount { get; init; }

    /// <summary>Number of jobs in the workflow topology.</summary>
    public required int JobCount { get; init; }

    /// <summary>
    /// Number of distinct tag clusters detected by co-occurrence analysis.
    /// High values (≥2) suggest natural domain boundaries for division.
    /// Derived from <c>ToolDefinition.Tags</c> and <c>EmpiricalEntry.Tags</c>.
    /// </summary>
    public required int TagClusterCount { get; init; }

    /// <summary>
    /// Shannon entropy of agent routing decisions across tools (0.0–1.0).
    /// High entropy means the agent spreads decisions evenly across many tools
    /// (generalist struggling). Low entropy means focused usage (specialist).
    /// </summary>
    public required float RoutingEntropy { get; init; }

    /// <summary>
    /// Count of distinct external backends/APIs/data sources that the cell's
    /// tools reach. High resource span = high membrane permeability = pressure
    /// to specialize.
    /// </summary>
    public required int ResourceSpan { get; init; }

    /// <summary>
    /// Fraction of the LLM's effective context window consumed by tool
    /// definitions alone (0.0–1.0). High values leave less room for user
    /// conversation and reasoning.
    /// </summary>
    public required float ContextUtilization { get; init; }

    // ── Observational (post-execution, secondary) ───────────────────

    /// <summary>Average latency per execution (ms). Secondary signal, not a division trigger.</summary>
    public float AvgLatencyMs { get; init; }

    /// <summary>Average cost per execution. Secondary signal, not a division trigger.</summary>
    public decimal AvgCostPerExecution { get; init; }

    // ── Metabolic (L3 — nullable so existing producers compile unchanged) ──

    /// <summary>
    /// Average tokens consumed per execution across the sample window.
    /// <see langword="null"/> when telemetry is unavailable.
    /// </summary>
    public double? TokensPerExecution { get; init; }

    /// <summary>
    /// 95th-percentile latency in milliseconds.
    /// <see langword="null"/> when telemetry is unavailable.
    /// </summary>
    public double? LatencyP95Ms { get; init; }

    /// <summary>
    /// Fraction of executions that resulted in a workflow-classified error (0–1).
    /// <see langword="null"/> when telemetry is unavailable.
    /// </summary>
    public double? ErrorRate { get; init; }

    /// <summary>
    /// High-level metabolic health derived from the three nullable fields above.
    /// Defaults to <see cref="MetabolicSignal.Healthy"/> when telemetry is absent.
    /// </summary>
    public MetabolicSignal Metabolism { get; init; } = MetabolicSignal.Healthy;

    /// <summary>When this snapshot was measured.</summary>
    public required DateTimeOffset MeasuredAt { get; init; }
}
