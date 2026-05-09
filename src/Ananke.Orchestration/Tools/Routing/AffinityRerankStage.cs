using Ananke.Abstractions.Tools.Routing;
using Ananke.Orchestration.Tools.Gating;

namespace Ananke.Orchestration.Tools.Routing;

/// <summary>
/// Routing stage that re-orders candidates by their UCB-derived affinity score
/// recorded in a <see cref="ToolAffinityTracker"/>.
/// </summary>
/// <remarks>
/// <para>
/// This stage does not drop any candidates — it only re-orders them by mean
/// reward. Untried tools (no recorded selections) sort to the front so
/// exploration is favoured.
/// </para>
/// <para>
/// Returns <see cref="RoutingConfidence.Medium"/> because affinity scores are
/// heuristic reinforcement signals, not authoritative semantic relevance.
/// The list is capped at <see cref="ToolRoutingRequest.MaxSelected"/>.
/// </para>
/// </remarks>
public sealed class AffinityRerankStage : ISmartToolRouter
{
    private readonly ToolAffinityTracker _tracker;

    /// <summary>Creates the stage backed by the given tracker.</summary>
    /// <param name="tracker">Affinity tracker whose scores drive re-ordering.</param>
    public AffinityRerankStage(ToolAffinityTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        _tracker = tracker;
    }

    /// <inheritdoc />
    public Task<ToolRoutingDecision> RouteAsync(
        ToolRoutingRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var affinities = _tracker.GetAffinities();

        var ranked = request.Candidates
            .OrderByDescending(e =>
            {
                var key = $"{e.KitName}::{e.ToolName}";
                // Untried tools → MaxValue so they are always explored first
                return affinities.TryGetValue(key, out var aff) && aff.Selections > 0
                    ? aff.MeanReward
                    : float.MaxValue;
            })
            .Take(request.MaxSelected)
            .ToList();

        return Task.FromResult(new ToolRoutingDecision
        {
            UseTools = true,
            SelectedTools = ranked,
            Confidence = RoutingConfidence.Medium,
        });
    }
}
