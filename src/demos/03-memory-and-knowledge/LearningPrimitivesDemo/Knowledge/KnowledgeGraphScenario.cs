using Ananke.Abstractions.Graph;
using Ananke.Abstractions.Graph.Algorithms;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Learning.Features;
using Ananke.Learning.Knowledge.Builders;
using Ananke.Learning.Knowledge.Analytics;
using Ananke.Learning.Knowledge.Reporting;
using Ananke.Learning.Knowledge.Retrieval;
using LearningPrimitivesDemo.Routing;

namespace LearningPrimitivesDemo.Knowledge;

// ═══════════════════════════════════════════════════════════════════════
//  Knowledge graph scenario — knowledge-graph substrate demo
//
//  Runs entirely offline: no API keys, no Qdrant, no external services.
//
//  Steps
//  ─────
//  1. Seed InMemoryEmpiricalMemory with 31 fixture entries across 3 topics
//     plus one bridge entry that links Topic A and Topic C.
//  2. Build the tag co-occurrence graph via TagCoOccurrenceBuilder.
//  3. Compare multi-hop graph-expanded retrieval against tag-overlap
//     baseline — the bridge entry is only recoverable via graph expansion.
//  4. Compare PageRank tag importance against frequency-based importance —
//     the bridge tag ranks high in PageRank despite low frequency.
//  5. Export MEMORY_REPORT.md and memory-graph.json.
// ═══════════════════════════════════════════════════════════════════════

internal static class KnowledgeGraphScenario
{
    internal static async Task RunAsync()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        PrintHeader("🧬 Knowledge Graph Demo — tag co-occurrence, multi-hop retrieval & PageRank");
        Console.WriteLine();

        // ── 1. Seed empirical memory ──────────────────────────────────────────

        Section(1, "Seeding empirical memory");

        var embedder = new FakeEmbeddingModel();
        // Use a threshold > 1.0 to disable semantic dedup — fixture entries are
        // intentionally distinct and must all be stored as separate entries.
        var memory = new InMemoryEmpiricalMemory(embedder, dedupThreshold: 1.1f);

        var entries = TopicFixture.CreateAll();
        foreach (var entry in entries)
            await memory.CommitAsync(entry);

        var total = await memory.CountAsync();
        Print($"  ✅ {total} entries committed  (3 topics × 10 + 1 bridge)", ConsoleColor.Green);
        Console.WriteLine();

        // ── 2. Build tag co-occurrence graph ──────────────────────────────────

        Section(2, "Building tag co-occurrence graph");

        var graph = new InMemoryKnowledgeGraph();
        var builder = new TagCoOccurrenceBuilder(memory);
        await builder.BuildAsync(graph);

        var nodeCount = await graph.NodeCountAsync();
        var edgeCount = await graph.EdgeCountAsync();
        Print($"  ✅ Graph built — {nodeCount} nodes, {edgeCount} edges", ConsoleColor.Green);
        Console.WriteLine();

        // ── 3. Multi-hop retrieval ────────────────────────────────────────────

        Section(3, "Multi-hop retrieval: graph-expanded vs. tag-overlap baseline");

        // Probe entry: tagged only with bridge tag + Topic A root tag.
        // The answer entry we want to surface is 'net-00', which is tagged only
        // with Topic C tags — reachable via TagGcPause → TagHighLatency → TagNicReset.
        var probeEntry = new EmpiricalEntry
        {
            Id = "probe-query",
            Kind = EmpiricalKind.Pattern,
            Tags = [TopicFixture.TagGcPause, TopicFixture.TagHighLatency],
            Source = "demo",
            Description = new SemanticDescription
            {
                Summary = "Investigating high-latency events linked to GC pressure",
                SemanticTags = new Dictionary<string, float>
                {
                    [TopicFixture.TagGcPause] = 1.0f,
                    [TopicFixture.TagHighLatency] = 0.8f,
                },
            },
            Confidence = 0.5f,
            ObservationCount = 1,
            Evidence = [],
            FirstObserved = DateTimeOffset.UtcNow,
            LastObserved = DateTimeOffset.UtcNow,
        };

        // Baseline: TagOverlapPredictionSource (no graph)
        var baseline = new TagOverlapPredictionSource(neighborCount: 5);
        var baselinePrediction = await baseline.PredictAsync(probeEntry, memory);

        // Graph-expanded source (2 hops)
        var graphSource = new GraphExpandedPredictionSource(graph, neighborCount: 5, hops: 2, maxExpandNodes: 50);
        var graphPrediction = await graphSource.PredictAsync(probeEntry, memory);

        var baselineStr = baselinePrediction is null ? "null (no basis)" : $"{baselinePrediction:F4}";
        var graphStr = graphPrediction is null ? "null (no basis)" : $"{graphPrediction:F4}";

        Print("  ┌────────────────────────────────────┬──────────────────┐", ConsoleColor.DarkCyan);
        Print("  │ Source                             │ Prediction       │", ConsoleColor.DarkCyan);
        Print("  ├────────────────────────────────────┼──────────────────┤", ConsoleColor.DarkCyan);
        Print($"  │ TagOverlapPredictionSource (flat)  │ {baselineStr,-16} │",
            baselinePrediction is null ? ConsoleColor.Yellow : ConsoleColor.White);
        Print($"  │ GraphExpandedPredictionSource      │ {graphStr,-16} │",
            graphPrediction is not null ? ConsoleColor.Green : ConsoleColor.Red);
        Print("  └────────────────────────────────────┴──────────────────┘", ConsoleColor.DarkCyan);

        if (graphPrediction is not null)
            Print("  ✅ Graph-expanded source recovered a multi-hop prediction", ConsoleColor.Green);
        else
            Print("  ⚠  Graph-expanded source returned null (graph may be too sparse)", ConsoleColor.Yellow);

        Console.WriteLine();

        // ── 4. PageRank vs. frequency importance ─────────────────────────────

        Section(4, "Tag importance: PageRank vs. frequency");

        var graphTracker = new GraphTagImportanceTracker(graph, new TagImportanceOptions { MinSampleSize = 5 });
        var frequencyTracker = new TagImportanceTracker(new TagImportanceOptions { MinSampleSize = 5 });

        var pageRankMap = await graphTracker.ComputeAsync(memory);
        var frequencyMap = await frequencyTracker.ComputeAsync(memory);

        var prTop5 = pageRankMap?.Importances
            .OrderByDescending(kv => kv.Value).Take(5).ToList()
            ?? [];
        var frTop5 = frequencyMap?.Importances
            .OrderByDescending(kv => kv.Value).Take(5).ToList()
            ?? [];

        Print("  PageRank top-5 (bridge tag promoted by topology):", ConsoleColor.Cyan);
        foreach (var (tag, score) in prTop5)
        {
            var isBridge = tag == TopicFixture.TagHighLatency;
            Print($"    {(isBridge ? "★ " : "  ")}{tag,-34} {score:F4}", isBridge ? ConsoleColor.Yellow : ConsoleColor.White);
        }

        Console.WriteLine();
        Print("  Frequency top-5 (bridge tag buried by low occurrence):", ConsoleColor.Cyan);
        foreach (var (tag, score) in frTop5)
        {
            var isBridge = tag == TopicFixture.TagHighLatency;
            Print($"    {(isBridge ? "★ " : "  ")}{tag,-34} {score:F4}", isBridge ? ConsoleColor.Yellow : ConsoleColor.White);
        }

        var bridgePrRank = prTop5.FindIndex(kv => kv.Key == TopicFixture.TagHighLatency);
        if (bridgePrRank >= 0)
            Print($"  ✅ Bridge tag '{TopicFixture.TagHighLatency}' appears at PageRank rank #{bridgePrRank + 1}", ConsoleColor.Green);
        else
            Print($"  ℹ  Bridge tag '{TopicFixture.TagHighLatency}' outside top-5 in PageRank", ConsoleColor.DarkYellow);

        Console.WriteLine();

        // ── 5. Export report ──────────────────────────────────────────────────

        Section(5, "Exporting MEMORY_REPORT.md + memory-graph.json");

        var outDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "out", "learning-knowledge-graph"));
        var exporter = new KnowledgeReportExporter(graph);
        await exporter.ExportAsync(outDir);

        var reportPath = Path.Combine(outDir, "MEMORY_REPORT.md");
        var graphPath = Path.Combine(outDir, "memory-graph.json");

        Print($"  ✅ Files written to: {outDir}", ConsoleColor.Green);
        Print($"     • {Path.GetFileName(reportPath)}", ConsoleColor.DarkGray);
        Print($"     • {Path.GetFileName(graphPath)}", ConsoleColor.DarkGray);
        Console.WriteLine();

        // Echo first 20 lines of the report
        Print("  ── MEMORY_REPORT.md (first 20 lines) ──", ConsoleColor.DarkCyan);
        var reportLines = await File.ReadAllLinesAsync(reportPath);
        foreach (var line in reportLines.Take(20))
            Print("  " + line, ConsoleColor.DarkGray);
        if (reportLines.Length > 20)
            Print($"  … ({reportLines.Length - 20} more lines)", ConsoleColor.DarkGray);

        Console.WriteLine();
        PrintFooter();
    }

    // ── Console helpers ───────────────────────────────────────────────────────

    private static void PrintHeader(string title)
    {
        Print("═══════════════════════════════════════════════════════════════", ConsoleColor.DarkCyan);
        Print($"  {title}", ConsoleColor.Cyan);
        Print("═══════════════════════════════════════════════════════════════", ConsoleColor.DarkCyan);
    }

    private static void PrintFooter()
    {
        Print("═══════════════════════════════════════════════════════════════", ConsoleColor.DarkCyan);
        Print("  Knowledge Graph Demo complete.", ConsoleColor.Cyan);
        Print("═══════════════════════════════════════════════════════════════", ConsoleColor.DarkCyan);
    }

    private static void Section(int n, string title)
    {
        Print("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━", ConsoleColor.DarkGray);
        Print($"  Step {n}: {title}", ConsoleColor.White);
        Console.WriteLine();
    }

    private static void Print(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }
}
