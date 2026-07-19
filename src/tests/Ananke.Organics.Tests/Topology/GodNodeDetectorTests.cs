using Ananke.Abstractions.Graph;
using Ananke.Abstractions.Graph.Algorithms;
using Ananke.Organics.Sensing;
using Ananke.Organics.Topology;
using Ananke.Organics.Topology.Centrality;
using Ananke.Organics.Kernel.Lineage;
using Shouldly;

namespace Ananke.Organics.Tests.Topology;

[TestFixture]
public class GodNodeDetectorTests
{
    // ── Star topology: 1 hub connected to many leaves ────────────────

    [Test]
    public async Task StarTopology_HubIsGodNode()
    {
        // hub serves 4 domains; each leaf serves only 1 domain
        var capabilityMap = new InMemoryCapabilityMap(signalTimeout: TimeSpan.FromMinutes(5));
        capabilityMap.Register(Signal("hub", "search"));
        capabilityMap.Register(Signal("hub", "catalog"));
        capabilityMap.Register(Signal("leaf-a", "payments"));
        capabilityMap.Register(Signal("leaf-b", "shipping"));
        capabilityMap.Register(Signal("leaf-c", "returns"));

        // Add extra serves edges to make hub a true hub
        var graph = new InMemoryKnowledgeGraph();
        var lineageStore = new InMemoryLineageStore();
        await new ColonyGraphBuilder(capabilityMap, lineageStore).BuildAsync(graph);

        // Manually add extra edges to give hub high degree
        await graph.UpsertEdgeAsync(new GraphEdge
        {
            FromId = "cell:hub",
            ToId = "domain:catalog",
            Relation = "serves",
            Provenance = EdgeProvenance.Extracted,
            Properties = new Dictionary<string, string> { ["source"] = "test" }
        });
        await graph.UpsertEdgeAsync(new GraphEdge
        {
            FromId = "cell:hub",
            ToId = "domain:extra1",
            Relation = "serves",
            Provenance = EdgeProvenance.Extracted,
            Properties = new Dictionary<string, string> { ["source"] = "test" }
        });
        await graph.UpsertEdgeAsync(new GraphEdge
        {
            FromId = "cell:hub",
            ToId = "domain:extra2",
            Relation = "serves",
            Provenance = EdgeProvenance.Extracted,
            Properties = new Dictionary<string, string> { ["source"] = "test" }
        });

        var detector = new GodNodeDetector(new DegreeCentralityScorer())
        {
            Threshold = 0.3f,
            TopK = 3
        };

        var gods = await detector.DetectAsync(graph);

        gods.ShouldNotBeEmpty();
        gods.ShouldContain(g => g.CellId == "hub");
        gods[0].CellId.ShouldBe("hub"); // highest centrality first
    }

    [Test]
    public async Task ChainTopology_InteriorNodesAreDetected_NotJustEndpoints()
    {
        // A → B → C → D → E: interior nodes (B, C, D) have degree 2 while
        // endpoints (A, E) have degree 1. With low threshold all high-degree
        // nodes qualify; with TopK=1 the detector picks a middle node.
        var graph = new InMemoryKnowledgeGraph();
        await AddCellNode(graph, "a");
        await AddCellNode(graph, "b");
        await AddCellNode(graph, "c");
        await AddCellNode(graph, "d");
        await AddCellNode(graph, "e");

        await graph.UpsertEdgeAsync(Edge("cell:a", "cell:b"));
        await graph.UpsertEdgeAsync(Edge("cell:b", "cell:c"));
        await graph.UpsertEdgeAsync(Edge("cell:c", "cell:d"));
        await graph.UpsertEdgeAsync(Edge("cell:d", "cell:e"));

        var detector = new GodNodeDetector(new DegreeCentralityScorer())
        {
            Threshold = 0.9f,
            TopK = 1
        };

        var gods = await detector.DetectAsync(graph);

        // Interior nodes (b, c, d) are most central; endpoints never appear as
        // top-1 because their degree (1) is half the max degree (2).
        gods.ShouldNotBeEmpty();
        gods[0].CellId.ShouldBeOneOf("b", "c", "d");
    }

    [Test]
    public async Task TopK_LimitsResults()
    {
        var graph = new InMemoryKnowledgeGraph();
        // Create 5 cells each connected to a hub so all have non-zero centrality
        await AddCellNode(graph, "hub");
        for (int i = 1; i <= 5; i++)
        {
            await AddCellNode(graph, $"leaf-{i}");
            await graph.UpsertEdgeAsync(Edge($"cell:hub", $"cell:leaf-{i}"));
        }

        var detector = new GodNodeDetector(new DegreeCentralityScorer())
        {
            Threshold = 0.0f,
            TopK = 2
        };

        var gods = await detector.DetectAsync(graph);

        gods.Count.ShouldBeLessThanOrEqualTo(2);
    }

    [Test]
    public void GodNode_CellId_StripsCellPrefix()
    {
        var g = new GodNode { NodeId = "cell:my-workflow", CentralityScore = 0.7f };
        g.CellId.ShouldBe("my-workflow");
    }

    [Test]
    public void GodNode_CellId_NoPrefixPassthrough()
    {
        var g = new GodNode { NodeId = "my-workflow", CentralityScore = 0.7f };
        g.CellId.ShouldBe("my-workflow");
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static WorkflowSignal Signal(string name, string domain) => new()
    {
        WorkflowName = name,
        Domain = domain,
        Capabilities = [],
        Timestamp = DateTimeOffset.UtcNow
    };

    private static Task AddCellNode(IKnowledgeGraph graph, string name) =>
        graph.UpsertNodeAsync(new GraphNode
        {
            Id = $"cell:{name}",
            Kind = "cell",
            Properties = new Dictionary<string, string> { ["name"] = name }
        });

    private static GraphEdge Edge(string from, string to) => new()
    {
        FromId = from,
        ToId = to,
        Relation = "serves",
        Provenance = EdgeProvenance.Extracted,
        Properties = new Dictionary<string, string> { ["source"] = "test" }
    };
}
