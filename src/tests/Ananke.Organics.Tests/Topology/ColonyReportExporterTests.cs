using System.Text.Json;
using Ananke.Abstractions.Graph;
using Ananke.Organics.Sensing;
using Ananke.Organics.Topology;
using Ananke.Organics.Topology.Centrality;
using Ananke.Organics.Topology.Reporting;
using Ananke.Organics.Kernel.Lineage;
using Shouldly;

namespace Ananke.Organics.Tests.Topology;

[TestFixture]
public class ColonyReportExporterTests
{
    private string _outputDir = null!;

    [SetUp]
    public void SetUp()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), $"colony-test-{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, recursive: true);
    }

    private static async Task<IKnowledgeGraph> BuildSmallGraphAsync()
    {
        var capabilityMap = new InMemoryCapabilityMap(signalTimeout: TimeSpan.FromMinutes(5));
        capabilityMap.Register(new WorkflowSignal
        {
            WorkflowName = "hub",
            Domain = "search",
            Capabilities = ["search/query"],
            Timestamp = DateTimeOffset.UtcNow
        });
        capabilityMap.Register(new WorkflowSignal
        {
            WorkflowName = "leaf",
            Domain = "catalog",
            Capabilities = ["catalog/browse"],
            Timestamp = DateTimeOffset.UtcNow
        });

        var lineageStore = new InMemoryLineageStore();
        await lineageStore.RecordBirthAsync(new CellLineage
        {
            CellId = "hub",
            WorkflowName = "hub",
            Generation = 0,
            BornAt = DateTimeOffset.UtcNow
        });
        await lineageStore.RecordBirthAsync(new CellLineage
        {
            CellId = "leaf",
            WorkflowName = "leaf",
            ParentCellId = "hub",
            Generation = 1,
            BornAt = DateTimeOffset.UtcNow,
            DivisionReason = "load"
        });

        var graph = new InMemoryKnowledgeGraph();
        await new ColonyGraphBuilder(capabilityMap, lineageStore).BuildAsync(graph);
        return graph;
    }

    // ── JSON round-trip ──────────────────────────────────────────────

    [Test]
    public async Task ExportAsync_WritesColonyJson_ThatDeserializesCleanly()
    {
        var graph = await BuildSmallGraphAsync();
        var godNodes = new List<GodNode>
        {
            new() { NodeId = "cell:hub", CentralityScore = 0.75f }
        };

        var exporter = new ColonyReportExporter();
        await exporter.ExportAsync(graph, godNodes, _outputDir);

        var jsonPath = Path.Combine(_outputDir, "colony.json");
        File.Exists(jsonPath).ShouldBeTrue();

        var json = await File.ReadAllTextAsync(jsonPath);
        var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("NodeCount", out _).ShouldBeTrue();
        doc.RootElement.TryGetProperty("EdgeCount", out _).ShouldBeTrue();
        doc.RootElement.TryGetProperty("GodNodes", out _).ShouldBeTrue();
    }

    // ── Markdown sections ────────────────────────────────────────────

    [Test]
    public async Task ExportAsync_MarkdownContainsGodNodesSection()
    {
        var graph = await BuildSmallGraphAsync();
        var godNodes = new List<GodNode>
        {
            new() { NodeId = "cell:hub", CentralityScore = 0.75f }
        };

        await new ColonyReportExporter().ExportAsync(graph, godNodes, _outputDir);

        var md = await File.ReadAllTextAsync(Path.Combine(_outputDir, "COLONY_REPORT.md"));
        md.ShouldContain("God nodes");
        md.ShouldContain("hub");
    }

    [Test]
    public async Task ExportAsync_MarkdownContainsLineageTreeDepthSection()
    {
        var graph = await BuildSmallGraphAsync();

        await new ColonyReportExporter().ExportAsync(graph, [], _outputDir);

        var md = await File.ReadAllTextAsync(Path.Combine(_outputDir, "COLONY_REPORT.md"));
        md.ShouldContain("Lineage tree depth");
    }

    [Test]
    public async Task ExportAsync_MarkdownContainsRoutingEdgeProvenanceBreakdownSection()
    {
        var graph = await BuildSmallGraphAsync();

        await new ColonyReportExporter().ExportAsync(graph, [], _outputDir);

        var md = await File.ReadAllTextAsync(Path.Combine(_outputDir, "COLONY_REPORT.md"));
        md.ShouldContain("Routing edge provenance breakdown");
    }

    [Test]
    public async Task ExportAsync_NoGodNodes_MarkdownSaysNoneDetected()
    {
        var graph = await BuildSmallGraphAsync();

        await new ColonyReportExporter().ExportAsync(graph, [], _outputDir);

        var md = await File.ReadAllTextAsync(Path.Combine(_outputDir, "COLONY_REPORT.md"));
        md.ShouldContain("No god nodes detected above threshold");
    }

    [Test]
    public async Task ExportAsync_CreatesOutputDirectory_IfAbsent()
    {
        var nested = Path.Combine(_outputDir, "deep", "nested");
        var graph = await BuildSmallGraphAsync();

        await new ColonyReportExporter().ExportAsync(graph, [], nested);

        Directory.Exists(nested).ShouldBeTrue();
        File.Exists(Path.Combine(nested, "COLONY_REPORT.md")).ShouldBeTrue();
    }
}
