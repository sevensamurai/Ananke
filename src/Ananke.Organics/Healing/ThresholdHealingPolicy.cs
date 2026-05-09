using System.Collections.Concurrent;
using Ananke.Organics.Division;

namespace Ananke.Organics.Healing;

/// <summary>
/// Threshold-based <see cref="IHealingPolicy"/> that triggers healing when a
/// cell's error rate exceeds a threshold for N consecutive evaluation windows.
/// </summary>
/// <remarks>
/// <para>
/// <b>Upstream blip tolerance:</b> A single spike in error rate does NOT
/// trigger healing. The policy requires <see cref="ConsecutiveFailureWindows"/>
/// consecutive evaluations where the error rate exceeds the threshold. This
/// filters out transient upstream failures (API blips, rate limits, network
/// partitions) that self-resolve.
/// </para>
/// <para>
/// <b>Complexity gate:</b> When <see cref="MaxComplexityForHealing"/> is set,
/// cells with complexity above that threshold are NOT healed — they should
/// divide first. Healing a structurally overloaded cell is futile; the
/// overload itself causes errors.
/// </para>
/// <para>
/// <b>Strategy selection:</b>
/// <list type="bullet">
///   <item>Error rate high + latency trend positive → <see cref="HealingStrategy.Restart"/>
///         (context bloat suspected)</item>
///   <item>Error rate high + latency trend flat/negative → <see cref="HealingStrategy.Rollback"/>
///         (configuration issue, post-division regression)</item>
/// </list>
/// </para>
/// <para>
/// This is the initial heuristic implementation. Replace with a more
/// sophisticated policy (ML-based, correlation-aware) as the system matures.
/// </para>
/// </remarks>
public sealed class ThresholdHealingPolicy : IHealingPolicy
{
    private readonly ConcurrentDictionary<string, int> _consecutiveFailures = new();

    /// <summary>
    /// Error rate threshold (0.0–1.0) above which a cell is considered degraded.
    /// Applied to <see cref="HealthSnapshot.WorkflowErrorRate"/> when available
    /// (classified errors), falling back to <see cref="HealthSnapshot.ErrorRate"/>
    /// (total errors) when all errors are unclassified.
    /// Default: 0.3 (30% of executions failing).
    /// </summary>
    public float ErrorRateThreshold { get; init; } = 0.3f;

    /// <summary>
    /// Number of consecutive evaluation windows where error rate must exceed
    /// <see cref="ErrorRateThreshold"/> before healing triggers. This is the
    /// primary mechanism for filtering upstream blips.
    /// Default: 3 (at the default 5-execution evaluation interval, this means
    /// 15 executions of sustained failure before healing).
    /// </summary>
    public int ConsecutiveFailureWindows { get; init; } = 3;

    /// <summary>
    /// Maximum tool count for healing eligibility. Cells with more tools than
    /// this should divide, not heal — their errors are likely caused by
    /// structural overload. <see langword="null"/> disables this gate.
    /// Default: null (no complexity gate).
    /// </summary>
    public int? MaxComplexityForHealing { get; init; }

    /// <summary>
    /// Latency slope threshold above which the policy suspects context bloat
    /// and recommends <see cref="HealingStrategy.Restart"/> instead of
    /// <see cref="HealingStrategy.Rollback"/>. Default: 5.0 (5ms increase
    /// per execution in the window).
    /// </summary>
    public float LatencySlopeRestartThreshold { get; init; } = 5.0f;

    /// <inheritdoc />
    public Task<HealingPlan?> EvaluateAsync(
        HealthSnapshot health,
        ComplexitySnapshot complexity,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(complexity);

        // Complexity gate: if cell is structurally overloaded, division is
        // the correct response, not healing
        if (MaxComplexityForHealing.HasValue && complexity.ToolCount > MaxComplexityForHealing.Value)
        {
            _consecutiveFailures.TryRemove(health.WorkflowName, out _);
            return Task.FromResult<HealingPlan?>(null);
        }

        // Determine the effective error rate:
        // - If we have classified errors, use WorkflowErrorRate (excludes upstream blips
        //   AND capability mismatches — neither should trigger healing)
        // - If all errors are unclassified (WorkflowErrorRate + UpstreamErrorRate == 0
        //   but ErrorRate > 0), fall back to total ErrorRate
        var hasClassification = health.WorkflowErrorRate > 0 || health.UpstreamErrorRate > 0
                                || health.CapabilityMismatchRate > 0;
        var effectiveErrorRate = hasClassification ? health.WorkflowErrorRate : health.ErrorRate;

        // Check if effective error rate exceeds threshold
        if (effectiveErrorRate < ErrorRateThreshold)
        {
            // Cell recovered (or errors are purely upstream) — reset counter
            _consecutiveFailures.TryRemove(health.WorkflowName, out _);
            return Task.FromResult<HealingPlan?>(null);
        }

        // Error rate exceeded — increment consecutive failure counter
        var count = _consecutiveFailures.AddOrUpdate(
            health.WorkflowName, 1, (_, c) => c + 1);

        if (count < ConsecutiveFailureWindows)
        {
            // Not yet sustained — could be an upstream blip
            return Task.FromResult<HealingPlan?>(null);
        }

        // Sustained degradation confirmed — determine strategy
        var strategy = health.LatencyTrendSlope > LatencySlopeRestartThreshold
            ? HealingStrategy.Restart
            : HealingStrategy.Rollback;

        var effectivePct   = effectiveErrorRate.ToString("P0", System.Globalization.CultureInfo.InvariantCulture);
        var thresholdPct   = ErrorRateThreshold.ToString("P0", System.Globalization.CultureInfo.InvariantCulture);
        var upstreamPct    = health.UpstreamErrorRate.ToString("P0", System.Globalization.CultureInfo.InvariantCulture);

        var reason = $"Workflow error rate {Pct(effectiveErrorRate)} exceeded threshold {Pct(ErrorRateThreshold)} " +
                     $"for {count} consecutive evaluation windows" +
                     (health.UpstreamErrorRate > 0 ? $" (upstream errors excluded: {Pct(health.UpstreamErrorRate)})" : "") +
                     ". " +
                     (strategy == HealingStrategy.Restart
                         ? $"Latency trend slope ({health.LatencyTrendSlope:F1}ms/exec) suggests context bloat — restarting."
                         : "Stable latency suggests configuration issue — rolling back.");

        // Reset counter (healing will be attempted)
        _consecutiveFailures.TryRemove(health.WorkflowName, out _);

        return Task.FromResult<HealingPlan?>(new HealingPlan
        {
            WorkflowName = health.WorkflowName,
            Strategy = strategy,
            Reason = reason,
            TriggeringHealth = health
        });
    }

    /// <summary>Resets the consecutive failure tracking for a cell. Call this after
    /// a successful heal to allow the cell a fresh evaluation window.
    /// </summary>
    /// <param name="workflowName">Cell to reset.</param>
    public void Reset(string workflowName) =>
        _consecutiveFailures.TryRemove(workflowName, out _);

    private static string Pct(float value) => $"{value * 100:F0}%";
}
