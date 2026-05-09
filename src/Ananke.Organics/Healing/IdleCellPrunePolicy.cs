using Ananke.Organics.Division;

namespace Ananke.Organics.Healing;

/// <summary>
/// Prunes cells that have been idle (received no executions) for longer than
/// a configurable threshold. Implements the apoptosis loop (L4).
/// </summary>
/// <remarks>
/// The policy reads the last-active time from <see cref="HealthSnapshot.LastRequestAt"/>.
/// When <see cref="HealthSnapshot.LastRequestAt"/> is <see langword="null"/> and the
/// cell has been alive for longer than <paramref name="idleThreshold"/>, it is
/// also pruned (never received any traffic).
/// </remarks>
public sealed class IdleCellPrunePolicy(TimeSpan idleThreshold, TimeProvider? clock = null) : IHealingPolicy
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <inheritdoc />
    public Task<HealingPlan?> EvaluateAsync(
        HealthSnapshot health,
        ComplexitySnapshot complexity,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(complexity);

        var now = _clock.GetUtcNow();
        var lastActivity = health.LastRequestAt ?? health.ObservedSince;
        var idle = now - lastActivity;

        if (idle < idleThreshold)
            return Task.FromResult<HealingPlan?>(null);

        return Task.FromResult<HealingPlan?>(new HealingPlan
        {
            WorkflowName = health.WorkflowName,
            Strategy = HealingStrategy.Prune,
            Reason = $"Cell idle for {idle.TotalSeconds:F0}s (threshold: {idleThreshold.TotalSeconds:F0}s)",
            TriggeringHealth = health
        });
    }
}
