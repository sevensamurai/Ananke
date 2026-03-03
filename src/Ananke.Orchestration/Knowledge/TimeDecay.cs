namespace Ananke.Orchestration.Knowledge;

/// <summary>
/// Computes time-decay weights for catalog-aware search result reranking.
/// </summary>
/// <remarks>
/// Timestamps don't replace vector similarity — they modulate it.
/// A highly relevant old document still beats an irrelevant new one,
/// but between two equally relevant documents, freshness wins.
/// The <see cref="TimeDecayOptions.FloorWeight"/> ensures old-but-unique knowledge
/// never disappears entirely.
/// </remarks>
public static class TimeDecay
{
    /// <summary>
    /// Computes a decay weight in <c>[FloorWeight, 1.0]</c> based on how old
    /// <paramref name="indexedAt"/> is relative to now.
    /// </summary>
    public static float ComputeWeight(DateTimeOffset indexedAt, TimeDecayOptions options) =>
        ComputeWeight(indexedAt, DateTimeOffset.UtcNow, options);

    /// <summary>
    /// Computes a decay weight in <c>[FloorWeight, 1.0]</c> based on the age
    /// between <paramref name="indexedAt"/> and <paramref name="now"/>.
    /// </summary>
    public static float ComputeWeight(
        DateTimeOffset indexedAt, DateTimeOffset now, TimeDecayOptions options)
    {
        var ageDays = Math.Max(0, (now - indexedAt).TotalDays);

        var raw = options.Function switch
        {
            TimeDecayFunction.Exponential => ExponentialDecay(ageDays, options.HalfLifeDays),
            TimeDecayFunction.Linear => LinearDecay(ageDays, options.HalfLifeDays),
            _ => 1.0
        };

        return Math.Max(options.FloorWeight, (float)raw);
    }

    /// <summary>
    /// Applies time-decay to a similarity <paramref name="score"/>:
    /// <c>finalScore = score × decayWeight</c>.
    /// </summary>
    public static float Apply(float score, DateTimeOffset indexedAt, TimeDecayOptions options) =>
        score * ComputeWeight(indexedAt, options);

    // ln(2) ≈ 0.693147; weight = e^(-ln2 × age / halfLife) → 0.5 at halfLife days.
    private static double ExponentialDecay(double ageDays, double halfLifeDays) =>
        Math.Exp(-0.693147 * ageDays / halfLifeDays);

    // Reaches zero at 2× halfLife days.
    private static double LinearDecay(double ageDays, double halfLifeDays)
    {
        var maxDays = halfLifeDays * 2;
        return Math.Max(0, 1.0 - ageDays / maxDays);
    }
}
