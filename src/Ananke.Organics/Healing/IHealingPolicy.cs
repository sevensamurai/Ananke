using Ananke.Organics.Division;

namespace Ananke.Organics.Healing;

/// <summary>
/// Evaluates whether a workflow cell needs healing based on its
/// <see cref="HealthSnapshot"/> and <see cref="ComplexitySnapshot"/>.
/// Returns <see langword="null"/> when the cell is healthy; returns a
/// <see cref="HealingPlan"/> when degradation warrants intervention.
/// </summary>
/// <remarks>
/// <para>
/// <b>Separation from division:</b> A cell with high error rate AND high
/// complexity should divide first (reduce load), then heal if errors persist.
/// A cell with high error rate and low complexity is genuinely sick —
/// healing is the correct response. Implementations receive both snapshots
/// to make this determination.
/// </para>
/// <para>
/// <b>Upstream blips vs workflow failure:</b> Transient upstream errors
/// (API timeouts, rate limits) should NOT trigger healing. Implementations
/// must distinguish sustained degradation from temporary spikes. The
/// <see cref="ThresholdHealingPolicy"/> uses consecutive failure windows
/// as the initial heuristic; more sophisticated implementations can use
/// error classification, correlation with other cells, or trend analysis.
/// </para>
/// </remarks>
public interface IHealingPolicy
{
    /// <summary>
    /// Evaluate whether a cell needs healing.
    /// </summary>
    /// <param name="health">Operational fitness metrics.</param>
    /// <param name="complexity">Structural complexity metrics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="HealingPlan"/> when healing is warranted;
    /// <see langword="null"/> when the cell is healthy or degradation
    /// is not yet confirmed (e.g., still within the transient tolerance).
    /// </returns>
    Task<HealingPlan?> EvaluateAsync(
        HealthSnapshot health,
        ComplexitySnapshot complexity,
        CancellationToken ct = default);
}
