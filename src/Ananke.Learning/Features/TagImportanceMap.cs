namespace Ananke.Learning.Features;

/// <summary>
/// Learned importance weights for semantic tags, computed by analyzing which
/// tags correlate with positive vs. negative outcomes in empirical memory.
/// Applied as a recall-time boost to prioritize discriminating tags.
/// </summary>
public sealed record TagImportanceMap
{
    /// <summary>Tag → importance score in [0.0, 1.0].</summary>
    public required IReadOnlyDictionary<string, float> Importances { get; init; }

    /// <summary>Number of entries analyzed to produce this map.</summary>
    public required int EntriesAnalyzed { get; init; }

    /// <summary>When this map was computed.</summary>
    public required DateTimeOffset ComputedAt { get; init; }

    /// <summary>
    /// Returns the importance of a tag. Tags not in the map default to
    /// <c>1.0</c> (neutral — full pass-through weight).
    /// </summary>
    public float GetImportance(string tag) =>
        Importances.TryGetValue(tag, out var importance) ? importance : 1.0f;
}
