using Ananke.Abstractions.Graph;
using Ananke.Learning.EmpiricalMemory;

namespace Ananke.Learning.Knowledge.Builders;

/// <summary>
/// Builds a bipartite <c>entry ↔ tag</c> graph with <c>co_occurs</c> edges
/// between tag pairs that appear together on the same <see cref="EmpiricalEntry"/>.
/// </summary>
/// <remarks>
/// <para>
/// Node ID conventions:
/// <list type="bullet">
///   <item><c>entry:{EmpiricalEntry.Id}</c></item>
///   <item><c>tag:{key}</c> — key is the raw tag key from <see cref="SemanticDescription.SemanticTags"/></item>
/// </list>
/// </para>
/// <para>
/// Edge conventions:
/// <list type="bullet">
///   <item><c>tagged</c> (entry → tag) — <see cref="EdgeProvenance.Extracted"/>; weight = tag weight on entry</item>
///   <item><c>co_occurs</c> (tag ↔ tag, both directions) — <see cref="EdgeProvenance.Inferred"/>; weight = geometric mean of the two tag weights</item>
/// </list>
/// </para>
/// </remarks>
public sealed class TagCoOccurrenceBuilder(IEmpiricalMemory memory)
{
    /// <summary>
    /// Scans all entries in <paramref name="graph"/>'s backing memory and upserts
    /// entry/tag nodes and co-occurrence edges into <paramref name="graph"/>.
    /// </summary>
    public async Task BuildAsync(IKnowledgeGraph graph, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var offset = 0;
        const int pageSize = 200;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await memory.BrowseAsync(offset, pageSize, ct: ct);
            if (page.Count == 0) break;

            foreach (var entry in page)
                await ProcessEntryAsync(graph, entry, ct);

            offset += page.Count;
            if (page.Count < pageSize) break;
        }
    }

    private static async Task ProcessEntryAsync(
        IKnowledgeGraph graph, EmpiricalEntry entry, CancellationToken ct)
    {
        // Upsert entry node.
        await graph.UpsertNodeAsync(new GraphNode
        {
            Id = EntryId(entry.Id),
            Kind = "entry",
            Properties = new Dictionary<string, string>
            {
                ["source"] = entry.Source,
                ["kind"] = entry.Kind.ToString().ToLowerInvariant(),
            },
        }, ct);

        var tags = entry.Description.SemanticTags;
        if (tags.Count == 0) return;

        var tagKeys = tags.Keys.ToList();

        // Upsert each tag node and the entry→tag edge.
        foreach (var (key, weight) in tags)
        {
            var tagNodeId = TagId(key);
            await graph.UpsertNodeAsync(new GraphNode { Id = tagNodeId, Kind = "tag" }, ct);

            await graph.UpsertEdgeAsync(new GraphEdge
            {
                FromId = EntryId(entry.Id),
                ToId = tagNodeId,
                Relation = "tagged",
                Provenance = EdgeProvenance.Extracted,
                Weight = weight,
            }, ct);
        }

        // Upsert co_occurs edges between every tag pair on this entry (both directions).
        for (var i = 0; i < tagKeys.Count; i++)
        {
            for (var j = i + 1; j < tagKeys.Count; j++)
            {
                var wA = tags[tagKeys[i]];
                var wB = tags[tagKeys[j]];
                var coWeight = MathF.Sqrt(wA * wB); // geometric mean

                var fromId = TagId(tagKeys[i]);
                var toId = TagId(tagKeys[j]);

                var edge = new GraphEdge
                {
                    FromId = fromId,
                    ToId = toId,
                    Relation = "co_occurs",
                    Provenance = EdgeProvenance.Inferred,
                    Weight = coWeight,
                };

                await graph.UpsertEdgeAsync(edge, ct);
                await graph.UpsertEdgeAsync(edge with { FromId = toId, ToId = fromId }, ct);
            }
        }
    }

    internal static string EntryId(string id) => $"entry:{id}";
    internal static string TagId(string key) => $"tag:{key}";
}
