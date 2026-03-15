namespace Ananke.Orchestration.Memory;

/// <summary>
/// Forms predictions by finding reinforced neighbors via semantic recall,
/// weighting their outcomes by <see cref="SemanticDescription.TagOverlap"/>,
/// and applying Bayesian shrinkage toward the neighbor consensus as the
/// entry accumulates its own observations.
/// </summary>
/// <remarks>
/// <para>
/// This breaks the confidence-as-prediction circularity: instead of using
/// the entry's own confidence to compute prediction error, the prediction
/// is formed from structurally similar entries that have already been
/// reinforced. Tag overlap provides domain-aware similarity that pure
/// vector distance may miss (e.g., two board positions with matching
/// structural features but different text descriptions).
/// </para>
/// <para>
/// <b>Bayesian shrinkage:</b> <c>priorWeight = 1 / (1 + observationCount)</c>.
/// New entries lean heavily on neighbor predictions; well-observed entries
/// rely more on their own confidence (which has stabilized through
/// repeated reinforcement). This prevents cold-start guessing while
/// converging toward self-knowledge as data accumulates.
/// </para>
/// </remarks>
/// <remarks>
/// Creates a tag-overlap prediction source.
/// </remarks>
/// <param name="neighborCount">
/// Maximum neighbors to consider for prediction.
/// Default is <c>5</c>.
/// </param>
public sealed class TagOverlapPredictionSource(int neighborCount = 5) : IPredictionSource
{
    private readonly int _neighborCount = neighborCount;

    /// <inheritdoc />
    public async Task<float?> PredictAsync(
        EmpiricalEntry entry,
        IEmpiricalMemory memory,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(memory);

        // Recall neighbors using the entry's own description
        var neighbors = await memory.RecallAsync(
            entry.Description.ToEmbeddingText(),
            new RecallOptions { TopK = _neighborCount, MinConfidence = 0f },
            ct);

        // Filter to reinforced entries (observed more than once) that aren't self
        float weightSum = 0f;
        float valueSum = 0f;

        foreach (var neighbor in neighbors)
        {
            if (neighbor.Entry.Id == entry.Id)
                continue;
            if (neighbor.Entry.ObservationCount <= 1)
                continue;

            var overlap = entry.Description.TagOverlap(neighbor.Entry.Description);
            var weight = overlap * neighbor.Entry.Confidence;
            if (weight <= 0f)
                continue;

            valueSum += weight * neighbor.Entry.Confidence;
            weightSum += weight;
        }

        if (weightSum <= 0f)
            return null; // no basis for prediction — caller falls back to confidence

        var neighborPrediction = valueSum / weightSum;

        // Bayesian shrinkage: trust neighbors early, own confidence later
        var priorWeight = 1f / (1f + entry.ObservationCount);
        var prediction = priorWeight * neighborPrediction
                       + (1f - priorWeight) * entry.Confidence;

        return Math.Clamp(prediction, 0f, 1f);
    }
}
