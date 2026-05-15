using Ananke.Abstractions.Graph;
using Neo4j.Driver;

namespace Ananke.Graph.Memgraph;

/// <summary>
/// <see cref="ICommunityDetector"/> that delegates to the MAGE
/// <c>community_detection.get()</c> procedure running inside Memgraph.
/// </summary>
/// <remarks>
/// Requires <see href="https://memgraph.com/mage">MAGE</see> to be installed on the
/// Memgraph instance (available in the <c>memgraph/memgraph-mage</c> Docker image).
/// </remarks>
public sealed class MemgraphCommunityDetector(MemgraphSessionFactory factory) : ICommunityDetector
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, int>> DetectAsync(
        IKnowledgeGraph graph,
        CancellationToken ct = default)
    {
        await using var session = factory.OpenSession();

        return await session.ExecuteReadAsync(async tx =>
        {
            // MAGE community_detection.get() yields (node, community_id).
            var cursor = await tx.RunAsync(
                "CALL community_detection.get() YIELD node, community_id RETURN node, community_id")
                .ConfigureAwait(false);

            var results = new Dictionary<string, int>(StringComparer.Ordinal);
            while (await cursor.FetchAsync().ConfigureAwait(false))
            {
                var node        = cursor.Current["node"].As<INode>();
                var communityId = cursor.Current["community_id"].As<int>();
                var id          = node["id"].As<string>();
                results[id]     = communityId;
            }

            return (IReadOnlyDictionary<string, int>)results;
        }).ConfigureAwait(false);
    }
}
