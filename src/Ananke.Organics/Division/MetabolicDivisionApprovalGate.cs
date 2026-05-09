using Ananke.Organics.Division.Approval;

namespace Ananke.Organics.Division;

/// <summary>
/// Decorator over any <see cref="IDivisionApprovalGate"/> that enforces metabolic
/// guardrails before forwarding to the inner gate.
/// </summary>
/// <remarks>
/// <list type="table">
///   <listheader><term>Signal</term><description>Behaviour</description></listheader>
///   <item><term><see cref="MetabolicSignal.Healthy"/></term><description>Delegates to inner gate unchanged.</description></item>
///   <item><term><see cref="MetabolicSignal.Stressed"/></term><description>Forces a rejection with a metabolic reason — auto-approval gates are overridden.</description></item>
///   <item><term><see cref="MetabolicSignal.Starved"/></term><description>Rejects the plan outright, bypassing the inner gate entirely.</description></item>
/// </list>
/// </remarks>
public sealed class MetabolicDivisionApprovalGate(
    IDivisionApprovalGate inner,
    MetabolicThresholds? thresholds = null) : IDivisionApprovalGate
{
    private readonly MetabolicThresholds _thresholds = thresholds ?? MetabolicThresholds.Default;

    /// <inheritdoc />
    public Task<DivisionApproval> ReviewAsync(
        DivisionPlan plan,
        ComplexitySnapshot snapshot,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(snapshot);

        var signal = _thresholds.Classify(snapshot);

        return signal switch
        {
            MetabolicSignal.Starved =>
                Task.FromResult(DivisionApproval.Reject(
                    reason: $"Division blocked: cell is Starved " +
                            $"(errorRate={snapshot.ErrorRate:P1}, latencyP95={snapshot.LatencyP95Ms:F0}ms). " +
                            "Restore metabolic health before dividing.")),

            MetabolicSignal.Stressed =>
                Task.FromResult(DivisionApproval.Reject(
                    reason: $"Division blocked: cell is Stressed " +
                            $"(errorRate={snapshot.ErrorRate:P1}, latencyP95={snapshot.LatencyP95Ms:F0}ms). " +
                            "Heal the cell before dividing, or override with a non-metabolic gate.")),

            _ => inner.ReviewAsync(plan, snapshot, ct)
        };
    }
}
