namespace Ananke.Abstractions.Graph.Algorithms;

/// <summary>
/// <see cref="ICentralityScorer"/> that ranks nodes by normalised degree
/// (in-degree + out-degree / max possible degree).
/// This is the default scorer; swap in <see cref="PageRankCentralityScorer"/>
/// for a propagation-based alternative.
/// </summary>
public sealed class DegreeCentralityScorer : ICentralityScorer
{
    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, float>> ScoreAsync(
        IKnowledgeGraph graph,
        string? nodeKindFilter = null,
        CancellationToken ct = default)
    {
        var nodeCount = await graph.NodeCountAsync(ct).ConfigureAwait(false);
        if (nodeCount == 0)
            return new Dictionary<string, float>();

        // Collect all edges to tally degree per node id.
        // We iterate via NeighborsAsync on every node — efficient for InMemory;
        // a persistent backend should override with a projection query.
        var degrees = new Dictionary<string, int>();

        // Enumerate nodes — we rely on ExpandAsync with depth=0 from all seeds.
        // Since we don't have a direct "all nodes" iterator on the interface we
        // use the edge index: gather all node IDs referenced by edges, then add
        // any isolated nodes by expanding with hops=0.
        var edgeCount = await graph.EdgeCountAsync(ct).ConfigureAwait(false);

        // Build degree map by collecting edges for all reachable nodes.
        // Strategy: seed with every node we discover via NeighborsAsync traversal.
        var allNodeIds = new HashSet<string>();

        // First pass: collect known node IDs via a large expand from an empty seed.
        // For InMemoryKnowledgeGraph the full list is in _nodes; for other backends
        // the caller should implement an override.  Here we use the internal cast.
        if (graph is InMemoryKnowledgeGraph inMemory)
        {
            await CollectDegreesFromInMemory(inMemory, degrees, allNodeIds, nodeKindFilter, ct)
                .ConfigureAwait(false);
        }
        else
        {
            // Generic path: no way to enumerate all nodes without a dedicated method;
            // return empty — callers on custom backends should implement their own scorer.
            return new Dictionary<string, float>();
        }

        if (degrees.Count == 0)
            return new Dictionary<string, float>();

        // Normalise: max degree across all scored nodes.
        var maxDegree = 0;
        foreach (var d in degrees.Values)
            if (d > maxDegree) maxDegree = d;

        var result = new Dictionary<string, float>(degrees.Count);
        foreach (var (id, deg) in degrees)
            result[id] = maxDegree > 0 ? deg / (float)maxDegree : 0f;

        return result;
    }

    private static async Task CollectDegreesFromInMemory(
        InMemoryKnowledgeGraph graph,
        Dictionary<string, int> degrees,
        HashSet<string> allNodeIds,
        string? nodeKindFilter,
        CancellationToken ct)
    {
        // Collect all node IDs — we expand with hops=0 to get nothing, but we
        // need actual node enumeration.  Expose a helper via ExpandAsync(seeds=all, hops=0).
        // Instead, gather node IDs from NodeCountAsync and rely on the fact that
        // InMemoryKnowledgeGraph.ExpandAsync(seeds, 0, int.MaxValue) returns the seeds.
        // We build the seed list by collecting all node IDs referenced by edges, then
        // use NeighborsAsync per node to count degree.

        // Simpler approach: enumerate the internal dictionary via reflection-free cast.
        // InMemoryKnowledgeGraph exposes AllNodeIds for this purpose.
        foreach (var nodeId in graph.AllNodeIds)
        {
            ct.ThrowIfCancellationRequested();

            var node = await graph.GetNodeAsync(nodeId, ct).ConfigureAwait(false);
            if (node is null) continue;
            if (nodeKindFilter is not null && node.Kind != nodeKindFilter) continue;

            allNodeIds.Add(nodeId);

            var edges = await graph.NeighborsAsync(nodeId, ct: ct).ConfigureAwait(false);
            degrees[nodeId] = edges.Count;
        }
    }
}
