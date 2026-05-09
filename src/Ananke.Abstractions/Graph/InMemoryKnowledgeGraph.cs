using System.Collections.Concurrent;
using Ananke.Abstractions.Graph.Algorithms;

namespace Ananke.Abstractions.Graph;

/// <summary>
/// Thread-safe, in-memory implementation of <see cref="IKnowledgeGraph"/>.
/// Suitable for tests and single-process workloads; replace with a persistent
/// backend by implementing <see cref="IKnowledgeGraph"/> for the target store.
/// </summary>
public sealed class InMemoryKnowledgeGraph : IKnowledgeGraph
{
    private readonly ConcurrentDictionary<string, GraphNode> _nodes = new();

    // Forward adjacency: FromId -> edges
    private readonly ConcurrentDictionary<string, List<GraphEdge>> _outEdges = new();

    // Reverse adjacency: ToId -> edges (for symmetric NeighborsAsync)
    private readonly ConcurrentDictionary<string, List<GraphEdge>> _inEdges = new();

    // Edge dedup key -> canonical edge (for upsert semantics)
    private readonly ConcurrentDictionary<string, GraphEdge> _edgeIndex = new();

    private readonly object _edgeLock = new();

    /// <inheritdoc/>
    public Task UpsertNodeAsync(GraphNode node, CancellationToken ct = default)
    {
        _nodes[node.Id] = node;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UpsertEdgeAsync(GraphEdge edge, CancellationToken ct = default)
    {
        var key = EdgeKey(edge.FromId, edge.ToId, edge.Relation);

        lock (_edgeLock)
        {
            if (_edgeIndex.TryGetValue(key, out var existing))
            {
                var mergedWeight = Math.Max(existing.Weight, edge.Weight);
                var mergedProvenance = Promote(existing.Provenance, edge.Provenance);

                if (mergedWeight == existing.Weight && mergedProvenance == existing.Provenance)
                    return Task.CompletedTask;

                var merged = existing with { Weight = mergedWeight, Provenance = mergedProvenance };
                _edgeIndex[key] = merged;
                ReplaceInBucket(_outEdges, existing.FromId, existing, merged);
                ReplaceInBucket(_inEdges, existing.ToId, existing, merged);
            }
            else
            {
                _edgeIndex[key] = edge;
                AddToBucket(_outEdges, edge.FromId, edge);
                AddToBucket(_inEdges, edge.ToId, edge);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<GraphNode?> GetNodeAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(_nodes.TryGetValue(id, out var node) ? node : null);

    /// <inheritdoc/>
    public Task<IReadOnlyList<GraphEdge>> NeighborsAsync(
        string nodeId,
        string? relation = null,
        CancellationToken ct = default)
    {
        List<GraphEdge> results = [];

        if (_outEdges.TryGetValue(nodeId, out var outList))
        {
            lock (outList)
            {
                foreach (var e in outList)
                    if (relation is null || e.Relation == relation)
                        results.Add(e);
            }
        }

        if (_inEdges.TryGetValue(nodeId, out var inList))
        {
            lock (inList)
            {
                foreach (var e in inList)
                    if (relation is null || e.Relation == relation)
                        results.Add(e);
            }
        }

        return Task.FromResult<IReadOnlyList<GraphEdge>>(results);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<GraphNode>> ExpandAsync(
        IReadOnlyList<string> seedNodeIds,
        int hops,
        int maxNodes,
        CancellationToken ct = default) =>
        await BreadthFirstExpansion.ExpandAsync(this, seedNodeIds, hops, maxNodes, ct)
            .ConfigureAwait(false);

    /// <inheritdoc/>
    public Task<int> NodeCountAsync(CancellationToken ct = default) =>
        Task.FromResult(_nodes.Count);

    /// <inheritdoc/>
    public Task<int> EdgeCountAsync(CancellationToken ct = default) =>
        Task.FromResult(_edgeIndex.Count);

    /// <summary>
    /// Returns all node IDs currently held in this instance.
    /// Used by scorers that need to enumerate the full node set.
    /// </summary>
    public IEnumerable<string> AllNodeIds => _nodes.Keys;

    // ── helpers ─────────────────────────────────────────────────────────────

    private static string EdgeKey(string from, string to, string relation) =>
        $"{from}\0{to}\0{relation}";

    private static EdgeProvenance Promote(EdgeProvenance existing, EdgeProvenance incoming)
    {
        // Provenance promotion only; never demote Extracted to anything lower.
        if (existing == EdgeProvenance.Extracted)
            return EdgeProvenance.Extracted;
        if (incoming == EdgeProvenance.Extracted)
            return EdgeProvenance.Extracted;
        if (existing == EdgeProvenance.Inferred || incoming == EdgeProvenance.Inferred)
            return EdgeProvenance.Inferred;
        return EdgeProvenance.Ambiguous;
    }

    private static void AddToBucket(
        ConcurrentDictionary<string, List<GraphEdge>> dict,
        string key,
        GraphEdge edge)
    {
        var bucket = dict.GetOrAdd(key, _ => []);
        lock (bucket)
            bucket.Add(edge);
    }

    private static void ReplaceInBucket(
        ConcurrentDictionary<string, List<GraphEdge>> dict,
        string key,
        GraphEdge old,
        GraphEdge replacement)
    {
        if (!dict.TryGetValue(key, out var bucket))
            return;
        lock (bucket)
        {
            var idx = bucket.IndexOf(old);
            if (idx >= 0)
                bucket[idx] = replacement;
        }
    }
}
