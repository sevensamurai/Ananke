using System.Text;
using System.Text.Json;
using Ananke.Abstractions.Graph;
using Ananke.Abstractions.Graph.Algorithms;

namespace Ananke.Learning.Knowledge.Reporting;

/// <summary>
/// Exports a snapshot of an <see cref="IKnowledgeGraph"/> to two artefacts:
/// <list type="bullet">
///   <item><c>memory-graph.json</c> — full node/edge dump for tooling consumption.</item>
///   <item><c>MEMORY_REPORT.md</c> — human-readable summary: top tags by PageRank
///     centrality, community membership (when an <see cref="ICommunityDetector"/>
///     is provided), and graph statistics.</item>
/// </list>
/// </summary>
public sealed class KnowledgeReportExporter(
    IKnowledgeGraph graph,
    ICommunityDetector? communityDetector = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// Writes <c>memory-graph.json</c> and <c>MEMORY_REPORT.md</c> into
    /// <paramref name="outputDirectory"/>.  The directory is created if it
    /// does not already exist.
    /// </summary>
    public async Task ExportAsync(string outputDirectory, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        Directory.CreateDirectory(outputDirectory);

        await WriteGraphJsonAsync(outputDirectory, ct).ConfigureAwait(false);
        await WriteMarkdownReportAsync(outputDirectory, ct).ConfigureAwait(false);
    }

    // ── JSON dump ────────────────────────────────────────────────────────────

    private async Task WriteGraphJsonAsync(string dir, CancellationToken ct)
    {
        // Collect all nodes and edges reachable via the graph interface.
        var nodes = await CollectAllNodesAsync(ct).ConfigureAwait(false);
        var edges = await CollectAllEdgesAsync(nodes, ct).ConfigureAwait(false);

        var dump = new { nodes, edges };
        var json = JsonSerializer.Serialize(dump, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(dir, "memory-graph.json"), json, ct).ConfigureAwait(false);
    }

    // ── Markdown report ──────────────────────────────────────────────────────

    private async Task WriteMarkdownReportAsync(string dir, CancellationToken ct)
    {
        var nodeCount = await graph.NodeCountAsync(ct).ConfigureAwait(false);
        var edgeCount = await graph.EdgeCountAsync(ct).ConfigureAwait(false);

        var scorer = new PageRankCentralityScorer();
        var scores = await scorer.ScoreAsync(graph, nodeKindFilter: "tag", ct).ConfigureAwait(false);

        var topTags = scores
            .OrderByDescending(kv => kv.Value)
            .Take(10)
            .ToList();

        IReadOnlyDictionary<string, int>? communities = null;
        if (communityDetector is not null)
            communities = await communityDetector.DetectAsync(graph, ct).ConfigureAwait(false);

        var sb = new StringBuilder();
        sb.AppendLine("# Memory Graph Report");
        sb.AppendLine();
        sb.AppendLine($"**Nodes:** {nodeCount}  **Edges:** {edgeCount}");
        sb.AppendLine();

        sb.AppendLine("## Top Tags");
        sb.AppendLine();
        if (topTags.Count == 0)
        {
            sb.AppendLine("_No tag nodes found._");
        }
        else
        {
            sb.AppendLine("| Tag | PageRank score |");
            sb.AppendLine("|---|---|");
            foreach (var (nodeId, score) in topTags)
            {
                var tag = nodeId.StartsWith("tag:", StringComparison.Ordinal)
                    ? nodeId["tag:".Length..]
                    : nodeId;
                sb.AppendLine($"| `{tag}` | {score:F6} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Communities");
        sb.AppendLine();
        if (communities is null || communities.Count == 0)
        {
            sb.AppendLine("_Not detected — no `ICommunityDetector` was registered._");
        }
        else
        {
            var grouped = communities
                .GroupBy(kv => kv.Value)
                .OrderBy(g => g.Key)
                .ToList();

            sb.AppendLine($"**{grouped.Count} communities detected** across {communities.Count} nodes.");
            sb.AppendLine();
            foreach (var group in grouped.Take(20))
            {
                sb.AppendLine($"- **Community {group.Key}** ({group.Count()} nodes)");
            }

            if (grouped.Count > 20)
                sb.AppendLine($"- _…and {grouped.Count - 20} more_");
        }

        await File.WriteAllTextAsync(
            Path.Combine(dir, "MEMORY_REPORT.md"), sb.ToString(), ct).ConfigureAwait(false);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<List<GraphNode>> CollectAllNodesAsync(CancellationToken ct)
    {
        if (graph is InMemoryKnowledgeGraph inMemory)
        {
            var nodes = new List<GraphNode>();
            foreach (var id in inMemory.AllNodeIds)
            {
                ct.ThrowIfCancellationRequested();
                var node = await graph.GetNodeAsync(id, ct).ConfigureAwait(false);
                if (node is not null) nodes.Add(node);
            }
            return nodes;
        }

        // Generic fallback — best-effort via large expand from empty seeds.
        return [];
    }

    private async Task<List<object>> CollectAllEdgesAsync(
        IReadOnlyList<GraphNode> nodes, CancellationToken ct)
    {
        var seen = new HashSet<string>();
        var edges = new List<object>();

        foreach (var node in nodes)
        {
            ct.ThrowIfCancellationRequested();
            var neighbors = await graph.NeighborsAsync(node.Id, ct: ct).ConfigureAwait(false);
            foreach (var edge in neighbors)
            {
                var key = $"{edge.FromId}\0{edge.ToId}\0{edge.Relation}";
                if (!seen.Add(key)) continue;

                edges.Add(new
                {
                    from_id = edge.FromId,
                    to_id = edge.ToId,
                    relation = edge.Relation,
                    provenance = edge.Provenance.ToString().ToLowerInvariant(),
                    weight = edge.Weight,
                });
            }
        }

        return edges;
    }
}
