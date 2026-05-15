using Ananke.Abstractions.Graph;
using Neo4j.Driver;

namespace Ananke.Graph.Memgraph;

/// <summary>
/// <see cref="ICentralityScorer"/> that delegates to the MAGE
/// <c>pagerank.get()</c> procedure running inside Memgraph.
/// </summary>
/// <remarks>
/// Requires <see href="https://memgraph.com/mage">MAGE</see> to be installed on the
/// Memgraph instance (available in the <c>memgraph/memgraph-mage</c> Docker image).
/// When MAGE is unavailable, register
/// <c>PageRankCentralityScorer</c> from <c>Ananke.Abstractions</c> instead —
/// it computes an equivalent result in-process.
/// </remarks>
public sealed class MemgraphPageRankScorer(MemgraphSessionFactory factory) : ICentralityScorer
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, float>> ScoreAsync(
        IKnowledgeGraph graph,
        string? nodeKindFilter = null,
        CancellationToken ct = default)
    {
        await using var session = factory.OpenSession();

        return await session.ExecuteReadAsync(async tx =>
        {
            // MAGE pagerank.get() returns rows of (node, rank).
            var cursor = await tx.RunAsync(
                "CALL pagerank.get() YIELD node, rank RETURN node, rank")
                .ConfigureAwait(false);

            var results = new Dictionary<string, float>(StringComparer.Ordinal);
            while (await cursor.FetchAsync().ConfigureAwait(false))
            {
                var node  = cursor.Current["node"].As<INode>();
                var rank  = (float)cursor.Current["rank"].As<double>();
                var id    = node["id"].As<string>();
                var kind  = node["kind"].As<string>();

                if (nodeKindFilter is null || kind == nodeKindFilter)
                    results[id] = rank;
            }

            return (IReadOnlyDictionary<string, float>)results;
        }).ConfigureAwait(false);
    }
}
