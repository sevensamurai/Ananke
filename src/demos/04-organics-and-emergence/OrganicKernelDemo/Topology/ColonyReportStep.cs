using Ananke.Abstractions.Graph;
using Ananke.Abstractions.Graph.Algorithms;
using Ananke.Organics.Kernel.Lineage;
using Ananke.Organics.Sensing;
using Ananke.Organics.Topology;
using Ananke.Organics.Topology.Centrality;
using Ananke.Organics.Topology.Reporting;
using static OrganicKernelDemo.DemoConsole;

namespace OrganicKernelDemo.Topology;

/// <summary>
/// Post-division topology analysis step.
/// Builds a colony graph from the live mesh state, detects god nodes via
/// degree centrality, and exports <c>colony.json</c> + <c>COLONY_REPORT.md</c>.
/// </summary>
internal static class ColonyReportStep
{
    /// <summary>
    /// Run the topology report after division.
    /// </summary>
    /// <param name="capabilityMap">Live capability landscape (cells + tools + domains).</param>
    /// <param name="lineageStore">Cell birth/death records populated by the demo.</param>
    /// <param name="affinityTracker">Optional routing-affinity tracker for <c>routed_to</c> edges.</param>
    /// <param name="outputDirectory">Directory where artefacts are written.</param>
    /// <param name="ct">Cancellation token.</param>
    internal static async Task RunAsync(
        ICapabilityMap capabilityMap,
        ILineageStore lineageStore,
        RoutingAffinityTracker? affinityTracker,
        string outputDirectory,
        CancellationToken ct = default)
    {
        Print("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━", ConsoleColor.DarkGray);
        Print("  🗺  Colony topology report", ConsoleColor.White);
        Console.WriteLine();

        // ── 1. Build graph ────────────────────────────────────────────────────

        var graph = new InMemoryKnowledgeGraph();
        var builder = new ColonyGraphBuilder(capabilityMap, lineageStore, affinityTracker);
        await builder.BuildAsync(graph, ct);

        var nodeCount = await graph.NodeCountAsync(ct);
        var edgeCount = await graph.EdgeCountAsync(ct);
        Print($"  ✅ Colony graph: {nodeCount} nodes, {edgeCount} edges", ConsoleColor.Green);

        // ── 2. Detect god nodes ───────────────────────────────────────────────

        var detector = new GodNodeDetector(new DegreeCentralityScorer()) { TopK = 3, Threshold = 0.4f };
        var godNodes = await detector.DetectAsync(graph, ct);

        if (godNodes.Count == 0)
        {
            Print("  ℹ  No god nodes above threshold (expected after clean division)", ConsoleColor.DarkYellow);
        }
        else
        {
            Print($"  ⚠  {godNodes.Count} god node(s) detected:", ConsoleColor.Yellow);
            foreach (var g in godNodes)
                Print($"     cell:{g.CellId}  score={g.CentralityScore:F3}", ConsoleColor.Yellow);
        }

        Console.WriteLine();

        // ── 3. Export report ──────────────────────────────────────────────────

        var outDir = Path.GetFullPath(outputDirectory);
        var exporter = new ColonyReportExporter();
        await exporter.ExportAsync(graph, godNodes, outDir, ct);

        var reportPath = Path.Combine(outDir, "COLONY_REPORT.md");
        var jsonPath = Path.Combine(outDir, "colony.json");

        Print($"  ✅ Files written to: {outDir}", ConsoleColor.Green);
        Print($"     • {Path.GetFileName(reportPath)}", ConsoleColor.DarkGray);
        Print($"     • {Path.GetFileName(jsonPath)}", ConsoleColor.DarkGray);
        Console.WriteLine();

        // Echo first 25 lines of the report
        Print("  ── COLONY_REPORT.md (first 25 lines) ──", ConsoleColor.DarkCyan);
        var lines = await File.ReadAllLinesAsync(reportPath, ct);
        foreach (var line in lines.Take(25))
            Print("  " + line, ConsoleColor.DarkGray);
        if (lines.Length > 25)
            Print($"  … ({lines.Length - 25} more lines)", ConsoleColor.DarkGray);

        Console.WriteLine();
    }
}
