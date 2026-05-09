namespace Ananke.Abstractions.Graph;

/// <summary>
/// Computes per-node centrality scores over a <see cref="IKnowledgeGraph"/>.
/// </summary>
public interface ICentralityScorer
{
    /// <summary>
    /// Returns a score for every node in <paramref name="graph"/>.
    /// When <paramref name="nodeKindFilter"/> is non-null only nodes whose
    /// <see cref="GraphNode.Kind"/> matches are scored; all others are excluded
    /// from the returned dictionary.
    /// Higher scores indicate greater centrality.
    /// </summary>
    Task<IReadOnlyDictionary<string, float>> ScoreAsync(
        IKnowledgeGraph graph,
        string? nodeKindFilter = null,
        CancellationToken ct = default);
}
