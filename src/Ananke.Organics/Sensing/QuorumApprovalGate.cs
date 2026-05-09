using Ananke.Organics.Division;
using Ananke.Organics.Division.Approval;

namespace Ananke.Organics.Sensing;

/// <summary>
/// Decorator over any <see cref="IDivisionApprovalGate"/> that enforces
/// mesh-wide quorum constraints before forwarding to the inner gate.
/// </summary>
/// <remarks>
/// When the mesh stress ratio is at or above <paramref name="stressRatioThreshold"/>
/// the proposal is rejected regardless of what the inner gate would decide.
/// This prevents runaway division during mesh-wide stress events.
/// </remarks>
public sealed class QuorumApprovalGate(
    IDivisionApprovalGate inner,
    IMeshAggregator aggregator,
    double stressRatioThreshold = 0.5) : IDivisionApprovalGate
{
    /// <inheritdoc />
    public Task<DivisionApproval> ReviewAsync(
        DivisionPlan plan,
        ComplexitySnapshot snapshot,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var signal = aggregator.CurrentSignal();

        if (signal.StressRatio >= stressRatioThreshold)
        {
            var stressPct    = $"{signal.StressRatio * 100:F0}%";
            var thresholdPct = $"{stressRatioThreshold * 100:F0}%";
            return Task.FromResult(DivisionApproval.Reject(
                reason: $"Division blocked by quorum gate: {signal.StressedCells}/{signal.TotalCells} cells stressed " +
                        $"({stressPct} ≥ threshold {thresholdPct}). Reduce mesh stress before dividing."));
        }

        return inner.ReviewAsync(plan, snapshot, ct);
    }
}
