using Ananke.Abstractions.Graph;
using Neo4j.Driver;

namespace Ananke.Graph.Memgraph;

/// <summary>
/// <see cref="IKnowledgeGraph"/> backed by Memgraph via the Neo4j Bolt driver.
/// </summary>
/// <remarks>
/// <para>
/// Nodes are stored as Cypher nodes with label <c>:Node</c> and properties
/// <c>id</c>, <c>kind</c>, and one property per entry in
/// <see cref="GraphNode.Properties"/>.
/// </para>
/// <para>
/// Edges are stored as Cypher relationships whose type matches
/// <see cref="GraphEdge.Relation"/> (upper-cased).  Additional metadata
/// (<c>weight</c>, <c>provenance</c>, <c>observedAt</c>, and custom properties)
/// are stored as relationship properties.
/// </para>
/// <para>
/// On upsert, weight uses <c>max(existing, incoming)</c> semantics.
/// Provenance is only promoted, never demoted:
/// <see cref="EdgeProvenance.Inferred"/> → <see cref="EdgeProvenance.Extracted"/>
/// is allowed; the reverse is silently ignored.
/// </para>
/// </remarks>
public sealed class MemgraphKnowledgeGraph(MemgraphSessionFactory factory) : IKnowledgeGraph
{
    // ── Schema bootstrap ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates indexes required for efficient node lookup and edge traversal.
    /// Call once at application startup (idempotent).
    /// </summary>
    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var session = factory.OpenSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync("CREATE INDEX ON :Node(id);").ConfigureAwait(false);
            await tx.RunAsync("CREATE INDEX ON :Node(kind);").ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    // ── IKnowledgeGraph ───────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task UpsertNodeAsync(GraphNode node, CancellationToken ct = default)
    {
        await using var session = factory.OpenSession();
        var props = BuildNodeProps(node);

        await session.ExecuteWriteAsync(tx =>
            tx.RunAsync(
                "MERGE (n:Node {id: $id}) SET n += $props",
                new { id = node.Id, props })).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpsertEdgeAsync(GraphEdge edge, CancellationToken ct = default)
    {
        await using var session = factory.OpenSession();

        // Relation label must be a valid Cypher identifier — upper-case, replace spaces.
        var relation = edge.Relation.ToUpperInvariant().Replace(' ', '_');

        // Provenance ordinal: Inferred=0, Extracted=1.  We only allow promotion.
        var provOrdinal = (int)edge.Provenance;

        await session.ExecuteWriteAsync(tx =>
            tx.RunAsync(
                $$"""
                MATCH (a:Node {id: $fromId}), (b:Node {id: $toId})
                MERGE (a)-[r:{{relation}}]->(b)
                ON CREATE SET
                    r.weight      = $weight,
                    r.provenance  = $prov,
                    r.observedAt  = $observedAt,
                    r += $props
                ON MATCH SET
                    r.weight      = CASE WHEN $weight > r.weight THEN $weight ELSE r.weight END,
                    r.provenance  = CASE WHEN $prov  > r.provenance THEN $prov ELSE r.provenance END,
                    r += $props
                """,
                new
                {
                    fromId = edge.FromId,
                    toId = edge.ToId,
                    weight = (double)edge.Weight,
                    prov = provOrdinal,
                    observedAt = edge.ObservedAt.ToUnixTimeMilliseconds(),
                    props = BuildEdgeProps(edge),
                })).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<GraphNode?> GetNodeAsync(string id, CancellationToken ct = default)
    {
        await using var session = factory.OpenSession();

        return await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(
                "MATCH (n:Node {id: $id}) RETURN n",
                new { id }).ConfigureAwait(false);

            if (!await cursor.FetchAsync().ConfigureAwait(false))
                return null;

            return RecordToNode(cursor.Current["n"].As<INode>());
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphEdge>> NeighborsAsync(
        string nodeId,
        string? relation = null,
        CancellationToken ct = default)
    {
        await using var session = factory.OpenSession();

        return await session.ExecuteReadAsync(async tx =>
        {
            IResultCursor cursor;

            if (relation is null)
            {
                cursor = await tx.RunAsync(
                    "MATCH (n:Node {id: $id})-[r]-(m:Node) RETURN r, n, m",
                    new { id = nodeId }).ConfigureAwait(false);
            }
            else
            {
                var label = relation.ToUpperInvariant().Replace(' ', '_');
                cursor = await tx.RunAsync(
                    $"MATCH (n:Node {{id: $id}})-[r:{label}]-(m:Node) RETURN r, n, m",
                    new { id = nodeId }).ConfigureAwait(false);
            }

            var results = new List<GraphEdge>();
            while (await cursor.FetchAsync().ConfigureAwait(false))
            {
                var rel = cursor.Current["r"].As<IRelationship>();
                results.Add(RecordToEdge(rel));
            }

            return (IReadOnlyList<GraphEdge>)results;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uses variable-length path matching: <c>MATCH (seed)-[*1..k]-(m)</c>.
    /// When MAGE is available, replace this with
    /// <c>CALL bfs.get(seed, maxHops, ...) YIELD node</c> for better performance
    /// on large graphs.
    /// </remarks>
    public async Task<IReadOnlyList<GraphNode>> ExpandAsync(
        IReadOnlyList<string> seedNodeIds,
        int hops,
        int maxNodes,
        CancellationToken ct = default)
    {
        if (seedNodeIds.Count == 0)
            return [];

        await using var session = factory.OpenSession();

        return await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(
                $$"""
                UNWIND $seeds AS seedId
                MATCH (seed:Node {id: seedId})-[*1..{{hops}}]-(m:Node)
                RETURN DISTINCT m
                LIMIT $maxNodes
                """,
                new { seeds = (IList<string>)seedNodeIds, maxNodes }).ConfigureAwait(false);

            var results = new List<GraphNode>();
            while (await cursor.FetchAsync().ConfigureAwait(false))
                results.Add(RecordToNode(cursor.Current["m"].As<INode>()));

            return (IReadOnlyList<GraphNode>)results;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> NodeCountAsync(CancellationToken ct = default)
    {
        await using var session = factory.OpenSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync("MATCH (n:Node) RETURN count(n) AS c").ConfigureAwait(false);
            await cursor.FetchAsync().ConfigureAwait(false);
            return cursor.Current["c"].As<int>();
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> EdgeCountAsync(CancellationToken ct = default)
    {
        await using var session = factory.OpenSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync("MATCH ()-[r]->() RETURN count(r) AS c").ConfigureAwait(false);
            await cursor.FetchAsync().ConfigureAwait(false);
            return cursor.Current["c"].As<int>();
        }).ConfigureAwait(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildNodeProps(GraphNode node)
    {
        var d = new Dictionary<string, object?>
        {
            ["id"]   = node.Id,
            ["kind"] = node.Kind,
        };
        foreach (var (k, v) in node.Properties)
            d[k] = v;
        return d;
    }

    private static Dictionary<string, object?> BuildEdgeProps(GraphEdge edge)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in edge.Properties)
            d[k] = v;
        return d;
    }

    private static GraphNode RecordToNode(INode n)
    {
        var reserved = new HashSet<string>(StringComparer.Ordinal) { "id", "kind" };
        var props = n.Properties
            .Where(p => !reserved.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value?.ToString() ?? string.Empty);

        return new GraphNode
        {
            Id   = n["id"].As<string>(),
            Kind = n["kind"].As<string>(),
            Properties = props,
        };
    }

    private static GraphEdge RecordToEdge(IRelationship r)
    {
        var reserved = new HashSet<string>(StringComparer.Ordinal)
            { "weight", "provenance", "observedAt" };

        var props = r.Properties
            .Where(p => !reserved.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value?.ToString() ?? string.Empty);

        var prov = r.Properties.TryGetValue("provenance", out var provVal)
            ? (EdgeProvenance)provVal.As<int>()
            : EdgeProvenance.Inferred;

        var observedAt = r.Properties.TryGetValue("observedAt", out var oaVal)
            ? DateTimeOffset.FromUnixTimeMilliseconds(oaVal.As<long>())
            : DateTimeOffset.UtcNow;

        return new GraphEdge
        {
            FromId     = r.StartNodeElementId,
            ToId       = r.EndNodeElementId,
            Relation   = r.Type,
            Provenance = prov,
            Weight     = r.Properties.TryGetValue("weight", out var wVal)
                             ? (float)wVal.As<double>()
                             : 1f,
            ObservedAt = observedAt,
            Properties = props,
        };
    }
}
