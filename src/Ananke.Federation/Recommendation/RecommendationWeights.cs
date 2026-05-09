namespace Ananke.Federation.Recommendation;

/// <summary>
/// Tunable weights for the four scoring axes used by <see cref="IPlatformRecommender"/>.
/// Mirrors the <c>RoutingWeights</c> pattern from <c>CapabilityModelRouter</c>.
/// </summary>
public sealed record RecommendationWeights
{
    /// <summary>Weight applied to the capability-coverage axis. Default: 1.0.</summary>
    public double CapabilityWeight { get; init; } = 1.0;

    /// <summary>Weight applied to the strength-alignment axis. Default: 1.0.</summary>
    public double StrengthWeight { get; init; } = 1.0;

    /// <summary>Weight applied to the cost-and-latency axis. Default: 0.5.</summary>
    public double CostLatencyWeight { get; init; } = 0.5;

    /// <summary>
    /// Weight applied to the governance-fit axis. Default: 1.5 — governance failures
    /// hurt more than capability gaps.
    /// </summary>
    public double GovernanceWeight { get; init; } = 1.5;
}
