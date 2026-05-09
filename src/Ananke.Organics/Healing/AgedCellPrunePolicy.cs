using Ananke.Organics.Division;
using Ananke.Organics.Kernel.Lineage;

namespace Ananke.Organics.Healing;

/// <summary>
/// Prunes cells that are older than a configurable maximum age and whose
/// utility (executions per unit of age) has fallen below a minimum threshold.
/// Implements the apoptosis loop (L4).
/// </summary>
/// <remarks>
/// Both conditions must be satisfied: the cell must be old enough <b>and</b>
/// under-utilised. An old cell that is still heavily used is not pruned.
/// </remarks>
public sealed class AgedCellPrunePolicy(
    TimeSpan maxAge,
    double minUtilityScore,
    ILineageStore lineage,
    TimeProvider? clock = null) : IHealingPolicy
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <inheritdoc />
    public async Task<HealingPlan?> EvaluateAsync(
        HealthSnapshot health,
        ComplexitySnapshot complexity,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(complexity);

        var record = await lineage.GetAsync(health.WorkflowName, ct);
        if (record is null)
            return null;

        var age = _clock.GetUtcNow() - record.BornAt;
        if (age < maxAge)
            return null;

        // utility = executions per day alive
        var ageDays = age.TotalDays;
        var utility = ageDays > 0 ? health.WindowSize / ageDays : 0;

        if (utility >= minUtilityScore)
            return null;

        return new HealingPlan
        {
            WorkflowName = health.WorkflowName,
            Strategy = HealingStrategy.Prune,
            Reason = $"Cell age {age.TotalDays:F1}d exceeds max {maxAge.TotalDays:F1}d " +
                     $"with utility {utility:F2} < minimum {minUtilityScore:F2}",
            TriggeringHealth = health
        };
    }
}
