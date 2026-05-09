using Ananke.Learning.EmpiricalMemory;

namespace Ananke.Learning.Episodes;

/// <summary>
/// Distributes terminal rewards backward through episode trajectories so
/// early decisions receive credit for outcomes they influenced.
/// </summary>
public interface IRewardPropagator
{
    /// <summary>
    /// Propagates rewards from the given <paramref name="episode"/> back to
    /// the empirical entries referenced by each step.
    /// </summary>
    /// <returns>The number of entries that were successfully reinforced.</returns>
    Task<int> PropagateAsync(
        Episode episode,
        IEmpiricalMemory memory,
        CancellationToken ct = default);
}

/// <summary>
/// Configuration for <see cref="IRewardPropagator"/> implementations.
/// </summary>
public sealed record RewardPropagationOptions
{
    /// <summary>Discount factor (γ) applied per step. Default: 0.95.</summary>
    public float DiscountFactor { get; init; } = 0.95f;

    /// <summary>Whether to include per-step intermediate rewards in the return calculation.</summary>
    public bool IncludeIntermediateRewards { get; init; } = true;

    /// <summary>Evidence source tag recorded on each reinforcement. Default: <c>"reward-propagation"</c>.</summary>
    public string EvidenceSource { get; init; } = "reward-propagation";
}
