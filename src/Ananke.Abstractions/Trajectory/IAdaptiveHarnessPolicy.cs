namespace Ananke.Abstractions.Trajectory;

/// <summary>
/// Reacts to completed trajectory snapshots and applies harness-level adaptations:
/// updating tool affinities, pruning empirical memory, and adjusting routing weights.
/// </summary>
public interface IAdaptiveHarnessPolicy
{
    /// <summary>
    /// Applies adaptations in response to the completed trajectory described by
    /// <paramref name="snapshot"/>.
    /// </summary>
    ValueTask AdaptAsync(TrajectorySnapshot snapshot, CancellationToken ct = default);
}

/// <summary>No-op default implementation. Satisfies the interface without side effects.</summary>
public sealed class NullAdaptiveHarnessPolicy : IAdaptiveHarnessPolicy
{
    /// <summary>Shared singleton — safe to use wherever a no-op policy is needed.</summary>
    public static readonly NullAdaptiveHarnessPolicy Instance = new();

    /// <inheritdoc />
    public ValueTask AdaptAsync(TrajectorySnapshot snapshot, CancellationToken ct = default)
        => ValueTask.CompletedTask;
}
