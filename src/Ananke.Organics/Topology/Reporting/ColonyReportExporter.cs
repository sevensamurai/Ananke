using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ananke.Abstractions.Graph;
using Ananke.Organics.Topology.Centrality;

namespace Ananke.Organics.Topology.Reporting;

/// <summary>
/// Exports the colony graph to a JSON data file and a human-readable markdown
/// report.
/// </summary>
/// <remarks>
/// Two files are written to <c>outputDirectory</c>:
/// <list type="bullet">
///   <item><c>colony.json</c> — machine-readable graph snapshot.</item>
///   <item><c>COLONY_REPORT.md</c> — markdown summary for operators.</item>
/// </list>
/// </remarks>
public sealed class ColonyReportExporter
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Export the colony graph and detected god nodes to <paramref name="outputDirectory"/>.
    /// </summary>
    /// <param name="graph">Colony graph to export.</param>
    /// <param name="godNodes">God nodes detected by <see cref="GodNodeDetector"/>.</param>
    /// <param name="outputDirectory">Directory where files are written. Created if absent.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ExportAsync(
        IKnowledgeGraph graph,
        IReadOnlyList<GodNode> godNodes,
        string outputDirectory,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(godNodes);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        Directory.CreateDirectory(outputDirectory);

        var snapshot = await BuildSnapshotAsync(graph, godNodes, ct);

        await WriteJsonAsync(snapshot, outputDirectory, ct);
        await WriteMarkdownAsync(snapshot, outputDirectory, ct);
    }

    // ── Snapshot builder ─────────────────────────────────────────────

    private static async Task<ColonySnapshot> BuildSnapshotAsync(
        IKnowledgeGraph graph,
        IReadOnlyList<GodNode> godNodes,
        CancellationToken ct)
    {
        var nodeCount = await graph.NodeCountAsync(ct);
        var edgeCount = await graph.EdgeCountAsync(ct);

        // Collect cell nodes to compute lineage depth
        // We approximate tree depth by scanning descended_from edges
        var cells = new List<ColonyCellEntry>();

        // Collect all cell nodes via DiscoverAll on the graph isn't available —
        // use GodNodes + any lineage we can enumerate from the god nodes.
        // For a complete snapshot we enumerate all node kinds we know from the builder.

        // Build lineage depth map by traversing descended_from edges
        var maxDepth = 0;
        foreach (var god in godNodes)
        {
            var depth = await ComputeDepthAsync(graph, god.NodeId, ct);
            if (depth > maxDepth) maxDepth = depth;

            cells.Add(new ColonyCellEntry
            {
                NodeId = god.NodeId,
                CellId = god.CellId,
                CentralityScore = god.CentralityScore,
                IsGodNode = true,
                LineageDepth = depth
            });
        }

        // Routing edge provenance breakdown
        var routingEdges = await graph.NeighborsAsync("routing:observed", "routed_to", ct);
        var provenanceCounts = routingEdges
            .GroupBy(e => e.Provenance.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        return new ColonySnapshot
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            NodeCount = nodeCount,
            EdgeCount = edgeCount,
            GodNodes = godNodes.Select(g => new GodNodeEntry
            {
                NodeId = g.NodeId,
                CellId = g.CellId,
                CentralityScore = g.CentralityScore
            }).ToList(),
            MaxLineageTreeDepth = maxDepth,
            Cells = cells,
            RoutingEdgeProvenanceBreakdown = provenanceCounts
        };
    }

    private static async Task<int> ComputeDepthAsync(
        IKnowledgeGraph graph, string nodeId, CancellationToken ct)
    {
        // BFS over descended_from edges to find depth
        int depth = 0;
        var frontier = new Queue<string>();
        frontier.Enqueue(nodeId);
        var visited = new HashSet<string> { nodeId };

        while (frontier.Count > 0)
        {
            var levelSize = frontier.Count;
            bool expanded = false;
            for (int i = 0; i < levelSize; i++)
            {
                var current = frontier.Dequeue();
                var edges = await graph.NeighborsAsync(current, "descended_from", ct);
                foreach (var edge in edges)
                {
                    var next = edge.FromId == current ? edge.ToId : edge.FromId;
                    if (visited.Add(next))
                    {
                        frontier.Enqueue(next);
                        expanded = true;
                    }
                }
            }
            if (expanded) depth++;
        }

        return depth;
    }

    // ── Writers ──────────────────────────────────────────────────────

    private static async Task WriteJsonAsync(
        ColonySnapshot snapshot, string outputDirectory, CancellationToken ct)
    {
        var path = Path.Combine(outputDirectory, "colony.json");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, snapshot, _jsonOptions, ct);
    }

    private static async Task WriteMarkdownAsync(
        ColonySnapshot snapshot, string outputDirectory, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Colony Report");
        sb.AppendLine();
        sb.AppendLine($"Generated: {snapshot.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.AppendLine($"- **Cells (nodes):** {snapshot.NodeCount}");
        sb.AppendLine($"- **Relationships (edges):** {snapshot.EdgeCount}");
        sb.AppendLine($"- **Lineage tree depth:** {snapshot.MaxLineageTreeDepth}");
        sb.AppendLine();

        // God nodes section
        sb.AppendLine("## God nodes");
        sb.AppendLine();
        if (snapshot.GodNodes.Count == 0)
        {
            sb.AppendLine("_No god nodes detected above threshold._");
        }
        else
        {
            sb.AppendLine("| Cell | Centrality score |");
            sb.AppendLine("|---|---|");
            foreach (var g in snapshot.GodNodes.OrderByDescending(x => x.CentralityScore))
                sb.AppendLine($"| `{g.CellId}` | {g.CentralityScore:F3} |");
        }
        sb.AppendLine();

        // Lineage tree depth section
        sb.AppendLine("## Lineage tree depth");
        sb.AppendLine();
        sb.AppendLine($"Maximum observed depth: **{snapshot.MaxLineageTreeDepth}**");
        sb.AppendLine();

        // Routing edge provenance breakdown section
        sb.AppendLine("## Routing edge provenance breakdown");
        sb.AppendLine();
        if (snapshot.RoutingEdgeProvenanceBreakdown.Count == 0)
        {
            sb.AppendLine("_No routing edges recorded._");
        }
        else
        {
            sb.AppendLine("| Provenance | Edge count |");
            sb.AppendLine("|---|---|");
            foreach (var (prov, count) in snapshot.RoutingEdgeProvenanceBreakdown)
                sb.AppendLine($"| {prov} | {count} |");
        }

        var path = Path.Combine(outputDirectory, "COLONY_REPORT.md");
        await File.WriteAllTextAsync(path, sb.ToString(), ct);
    }

    // ── Internal snapshot types ──────────────────────────────────────

    private sealed class ColonySnapshot
    {
        public DateTimeOffset GeneratedAt { get; init; }
        public int NodeCount { get; init; }
        public int EdgeCount { get; init; }
        public IReadOnlyList<GodNodeEntry> GodNodes { get; init; } = [];
        public int MaxLineageTreeDepth { get; init; }
        public IReadOnlyList<ColonyCellEntry> Cells { get; init; } = [];
        public IReadOnlyDictionary<string, int> RoutingEdgeProvenanceBreakdown { get; init; }
            = new Dictionary<string, int>();
    }

    private sealed class GodNodeEntry
    {
        public string NodeId { get; init; } = string.Empty;
        public string CellId { get; init; } = string.Empty;
        public float CentralityScore { get; init; }
    }

    private sealed class ColonyCellEntry
    {
        public string NodeId { get; init; } = string.Empty;
        public string CellId { get; init; } = string.Empty;
        public float CentralityScore { get; init; }
        public bool IsGodNode { get; init; }
        public int LineageDepth { get; init; }
    }
}
