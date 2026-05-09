namespace Ananke.Abstractions.Graph;

/// <summary>
/// A typed, provenance-aware graph that supports node/edge upsert, neighbour
/// traversal, and bounded k-hop expansion.
/// </summary>
public interface IKnowledgeGraph
{
    /// <summary>Inserts or replaces a node by its <see cref="GraphNode.Id"/>.</summary>
    Task UpsertNodeAsync(GraphNode node, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates an edge keyed by the <c>(FromId, ToId, Relation)</c> triple.
    /// On collision the weight is set to <c>max(existing, incoming)</c> and provenance
    /// is only promoted (never demoted): <see cref="EdgeProvenance.Inferred"/> →
    /// <see cref="EdgeProvenance.Extracted"/> is allowed;
    /// <see cref="EdgeProvenance.Extracted"/> → <see cref="EdgeProvenance.Inferred"/>
    /// is silently ignored.
    /// </summary>
    Task UpsertEdgeAsync(GraphEdge edge, CancellationToken ct = default);

    /// <summary>Returns the node with the given <paramref name="id"/>, or <c>null</c>.</summary>
    Task<GraphNode?> GetNodeAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Returns all edges whose <see cref="GraphEdge.FromId"/> or
    /// <see cref="GraphEdge.ToId"/> equals <paramref name="nodeId"/>.
    /// When <paramref name="relation"/> is non-null only edges with a matching
    /// <see cref="GraphEdge.Relation"/> are included.
    /// </summary>
    Task<IReadOnlyList<GraphEdge>> NeighborsAsync(
        string nodeId,
        string? relation = null,
        CancellationToken ct = default);

    /// <summary>
    /// BFS expansion from <paramref name="seedNodeIds"/> up to <paramref name="hops"/>
    /// hops deep, returning at most <paramref name="maxNodes"/> nodes in discovery order,
    /// deduplicated.
    /// </summary>
    Task<IReadOnlyList<GraphNode>> ExpandAsync(
        IReadOnlyList<string> seedNodeIds,
        int hops,
        int maxNodes,
        CancellationToken ct = default);

    /// <summary>Returns the total number of nodes currently in the graph.</summary>
    Task<int> NodeCountAsync(CancellationToken ct = default);

    /// <summary>Returns the total number of edges currently in the graph.</summary>
    Task<int> EdgeCountAsync(CancellationToken ct = default);
}
