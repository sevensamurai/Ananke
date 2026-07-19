namespace Ananke.Learning.EmpiricalMemory;

/// <summary>
/// Affect-driven scoring math shared by <see cref="InMemoryEmpiricalMemory"/> and
/// <c>QdrantEmpiricalMemory</c> (<c>Ananke.Qdrant</c>) — kept in one place so the two store
/// implementations can't drift on how <see cref="AffectOptions"/> gets applied.
/// </summary>
/// <remarks>
/// Both methods take <c>now</c> explicitly rather than reading
/// <see cref="DateTimeOffset.UtcNow"/> internally, so callers can inject a <see cref="TimeProvider"/>
/// for deterministic, sleep-free tests.
/// </remarks>
public static class EmpiricalScoring
{
    /// <summary>
    /// Applies affect-driven priority boosting to a composite recall score:
    /// <c>score × (1 + MaxPriorityBoost × intensity × decayed|valence|)</c>, where both the
    /// strength and valence components additionally fade via
    /// <see cref="AffectOptions.StrengthHalfLifeDays"/> / <see cref="AffectOptions.ValenceHalfLifeDays"/>
    /// when configured. A no-op multiplier (×1) when neither half-life is set and
    /// <see cref="AffectOptions.MaxPriorityBoost"/> contributes nothing beyond intensity×valence.
    /// </summary>
    public static float ApplyPriorityBoost(
        float compositeScore, float valence, float intensity, DateTimeOffset lastObserved,
        AffectOptions affectOptions, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(affectOptions);

        if (affectOptions.StrengthHalfLifeDays is { } shl)
        {
            var elapsedDays = (float)(now - lastObserved).TotalDays;
            compositeScore *= MathF.Pow(2f, -elapsedDays / shl);
        }

        var effectiveValence = MathF.Abs(valence);
        if (affectOptions.ValenceHalfLifeDays is { } vhl)
        {
            var elapsedDays = (float)(now - lastObserved).TotalDays;
            effectiveValence *= MathF.Pow(2f, -elapsedDays / vhl);
        }

        var priorityBoost = 1f + affectOptions.MaxPriorityBoost * intensity * effectiveValence;
        return compositeScore * priorityBoost;
    }

    /// <summary>
    /// Computes the reinforcement cooldown factor in <c>[0, 1]</c>: <c>0</c> immediately after
    /// the last observation, ramping linearly to <c>1</c> once
    /// <see cref="AffectOptions.ReinforcementCooldownHours"/> have elapsed.
    /// </summary>
    public static float ComputeReinforcementCooldown(
        DateTimeOffset lastObserved, float cooldownHours, DateTimeOffset now)
    {
        var hours = (float)(now - lastObserved).TotalHours;
        return MathF.Min(1f, hours / cooldownHours);
    }
}
