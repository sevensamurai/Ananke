using Ananke.Abstractions.Graph;
using System.Text.Json;
using Ananke.Learning.Knowledge.Reporting;
using Shouldly;

namespace Ananke.Learning.Tests.Knowledge;

[TestFixture]
public sealed class KnowledgeReportExporterTests
{
    private string _outputDir = null!;

    [SetUp]
    public void SetUp()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), $"ananke-report-{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, recursive: true);
    }

    // ── JSON round-trip ──────────────────────────────────────────────────────

    [Test]
    public async Task ExportAsync_ProducesValidJson()
    {
        var graph = BuildSimpleGraph();
        var exporter = new KnowledgeReportExporter(graph);

        await exporter.ExportAsync(_outputDir);

        var jsonPath = Path.Combine(_outputDir, "memory-graph.json");
        File.Exists(jsonPath).ShouldBeTrue();

        // Must deserialize without throwing.
        var json = await File.ReadAllTextAsync(jsonPath);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("nodes", out _).ShouldBeTrue();
        doc.RootElement.TryGetProperty("edges", out _).ShouldBeTrue();
    }

    // ── Markdown structure ───────────────────────────────────────────────────

    [Test]
    public async Task ExportAsync_MarkdownContainsTopTagsSection()
    {
        var graph = BuildSimpleGraph();
        var exporter = new KnowledgeReportExporter(graph);

        await exporter.ExportAsync(_outputDir);

        var md = await File.ReadAllTextAsync(Path.Combine(_outputDir, "MEMORY_REPORT.md"));
        md.ShouldContain("## Top Tags");
    }

    [Test]
    public async Task ExportAsync_MarkdownContainsCommunitiesSection()
    {
        var graph = BuildSimpleGraph();
        var exporter = new KnowledgeReportExporter(graph);

        await exporter.ExportAsync(_outputDir);

        var md = await File.ReadAllTextAsync(Path.Combine(_outputDir, "MEMORY_REPORT.md"));
        md.ShouldContain("## Communities");
    }

    [Test]
    public async Task ExportAsync_NoCommunityDetector_MarkdownStatesNotDetected()
    {
        var graph = BuildSimpleGraph();
        var exporter = new KnowledgeReportExporter(graph);

        await exporter.ExportAsync(_outputDir);

        var md = await File.ReadAllTextAsync(Path.Combine(_outputDir, "MEMORY_REPORT.md"));
        md.ShouldContain("Not detected");
    }

    [Test]
    public async Task ExportAsync_WithCommunityDetector_MarkdownShowsDetectedCount()
    {
        var graph = BuildSimpleGraph();
        var exporter = new KnowledgeReportExporter(graph, new StubCommunityDetector());

        await exporter.ExportAsync(_outputDir);

        var md = await File.ReadAllTextAsync(Path.Combine(_outputDir, "MEMORY_REPORT.md"));
        md.ShouldContain("communities detected");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static InMemoryKnowledgeGraph BuildSimpleGraph()
    {
        var graph = new InMemoryKnowledgeGraph();

        graph.UpsertNodeAsync(new GraphNode { Id = "tag:cause/gc", Kind = "tag" }).GetAwaiter().GetResult();
        graph.UpsertNodeAsync(new GraphNode { Id = "tag:effect/oom", Kind = "tag" }).GetAwaiter().GetResult();
        graph.UpsertEdgeAsync(new GraphEdge
        {
            FromId = "tag:cause/gc", ToId = "tag:effect/oom",
            Relation = "co_occurs", Provenance = EdgeProvenance.Inferred,
        }).GetAwaiter().GetResult();

        return graph;
    }

    private sealed class StubCommunityDetector : ICommunityDetector
    {
        public Task<IReadOnlyDictionary<string, int>> DetectAsync(
            IKnowledgeGraph graph, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>
            {
                ["tag:cause/gc"]  = 0,
                ["tag:effect/oom"] = 0,
            });
    }
}
