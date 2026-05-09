namespace Ananke.Abstractions.Graph.Algorithms;

/// <summary>
/// BFS-based k-hop expansion over an <see cref="IKnowledgeGraph"/>.
/// </summary>
internal static class BreadthFirstExpansion
{
    /// <summary>
    /// Expands outward from <paramref name="seedNodeIds"/> up to <paramref name="hops"/>
    /// hops, returning at most <paramref name="maxNodes"/> nodes in discovery order,
    /// deduplicated.  Nodes that do not exist in the graph are silently skipped.
    /// </summary>
    internal static async Task<IReadOnlyList<GraphNode>> ExpandAsync(
        IKnowledgeGraph graph,
        IReadOnlyList<string> seedNodeIds,
        int hops,
        int maxNodes,
        CancellationToken ct = default)
    {
        var visited = new HashSet<string>(seedNodeIds.Count + 16);
        var result = new List<GraphNode>(Math.Min(maxNodes, 64));
        var frontier = new Queue<(string Id, int Depth)>();

        foreach (var id in seedNodeIds)
        {
            if (visited.Add(id))
                frontier.Enqueue((id, 0));
        }

        while (frontier.Count > 0 && result.Count < maxNodes)
        {
            ct.ThrowIfCancellationRequested();
            var (id, depth) = frontier.Dequeue();

            var node = await graph.GetNodeAsync(id, ct).ConfigureAwait(false);
            if (node is not null)
                result.Add(node);

            if (depth >= hops || result.Count >= maxNodes)
                continue;

            var edges = await graph.NeighborsAsync(id, ct: ct).ConfigureAwait(false);
            foreach (var edge in edges)
            {
                var neighbor = edge.FromId == id ? edge.ToId : edge.FromId;
                if (visited.Add(neighbor))
                    frontier.Enqueue((neighbor, depth + 1));
            }
        }

        return result;
    }
}
