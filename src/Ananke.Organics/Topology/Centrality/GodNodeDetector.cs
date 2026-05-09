using Ananke.Abstractions.Graph;

namespace Ananke.Organics.Topology.Centrality;

/// <summary>
/// Identifies "god nodes" — cells whose degree centrality exceeds a threshold,
/// making them structural single-points-of-failure in the colony graph.
/// </summary>
/// <remarks>
/// A god node is a cell that disproportionately many domains or other cells
/// depend on. Detecting these nodes allows operators to proactively schedule
/// division or redundancy before a failure cascades.
/// </remarks>
/// <param name="scorer">Centrality scorer used to rank cells.</param>
public sealed class GodNodeDetector(ICentralityScorer scorer)
{
    /// <summary>
    /// Maximum number of cells to return, ranked by centrality descending.
    /// Default: 5.
    /// </summary>
    public int TopK { get; init; } = 5;

    /// <summary>
    /// Minimum normalised centrality score (0.0–1.0) a cell must reach to be
    /// considered a god node. Default: 0.4.
    /// </summary>
    public float Threshold { get; init; } = 0.4f;

    /// <summary>
    /// Detect god nodes in the colony graph.
    /// </summary>
    /// <param name="graph">Colony graph to analyse (typically produced by <see cref="ColonyGraphBuilder"/>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Ordered list of <see cref="GodNode"/> records, highest centrality first,
    /// filtered to those exceeding <see cref="Threshold"/>. Empty when no cell
    /// meets the threshold.
    /// </returns>
    public async Task<IReadOnlyList<GodNode>> DetectAsync(
        IKnowledgeGraph graph,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var scores = await scorer.ScoreAsync(graph, nodeKindFilter: "cell", ct);

        return scores
            .Where(kv => kv.Value >= Threshold)
            .OrderByDescending(kv => kv.Value)
            .Take(TopK)
            .Select(kv => new GodNode { NodeId = kv.Key, CentralityScore = kv.Value })
            .ToList();
    }
}

/// <summary>A cell identified as a god node by <see cref="GodNodeDetector"/>.</summary>
public sealed record GodNode
{
    /// <summary>Graph node ID (format: <c>cell:{CellId}</c>).</summary>
    public required string NodeId { get; init; }

    /// <summary>Normalised degree centrality score (0.0–1.0).</summary>
    public required float CentralityScore { get; init; }

    /// <summary>
    /// Returns the raw cell identifier by stripping the <c>cell:</c> prefix.
    /// </summary>
    public string CellId => NodeId.StartsWith("cell:", StringComparison.Ordinal)
        ? NodeId["cell:".Length..]
        : NodeId;
}
