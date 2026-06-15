using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Graph;
using Ananke.Abstractions.Graph.Algorithms;
using Ananke.Organics.Division;
using Ananke.Organics.Kernel.Lineage;
using Ananke.Organics.Sensing;
using Ananke.Organics.Topology;
using Shouldly;

namespace Ananke.Organics.Tests.Topology;

[TestFixture]
public class ColonyGraphBuilderTests
{
    private InMemoryCapabilityMap _capabilityMap = null!;
    private InMemoryLineageStore _lineageStore = null!;

    [SetUp]
    public void SetUp()
    {
        _capabilityMap = new InMemoryCapabilityMap(signalTimeout: TimeSpan.FromMinutes(1));
        _lineageStore = new InMemoryLineageStore();
    }

    private static WorkflowSignal MakeSignal(
        string name, string domain, params string[] capabilities) => new()
    {
        WorkflowName = name,
        Domain = domain,
        Capabilities = capabilities.Length > 0 ? capabilities : ["tool-a"],
        Timestamp = DateTimeOffset.UtcNow
    };

    // ── Cell nodes ───────────────────────────────────────────────────

    [Test]
    public async Task BuildAsync_RegistersAliveCell_AsCellNode()
    {
        _capabilityMap.Register(MakeSignal("alpha", "search"));
        var graph = new InMemoryKnowledgeGraph();

        await new ColonyGraphBuilder(_capabilityMap, _lineageStore).BuildAsync(graph);

        var node = await graph.GetNodeAsync("cell:alpha");
        node.ShouldNotBeNull();
        node!.Kind.ShouldBe("cell");
        node.Properties["name"].ShouldBe("alpha");
    }

    [Test]
    public async Task BuildAsync_RegistersDomainNode_AndServesEdge()
    {
        _capabilityMap.Register(MakeSignal("alpha", "search"));
        var graph = new InMemoryKnowledgeGraph();

        await new ColonyGraphBuilder(_capabilityMap, _lineageStore).BuildAsync(graph);

        var domainNode = await graph.GetNodeAsync("domain:search");
        domainNode.ShouldNotBeNull();
        domainNode!.Kind.ShouldBe("domain");

        var edges = await graph.NeighborsAsync("cell:alpha", "serves");
        edges.ShouldContain(e => e.ToId == "domain:search");
    }

    [Test]
    public async Task BuildAsync_RegistersToolNodes_ForCapabilities()
    {
        _capabilityMap.Register(MakeSignal("alpha", "search", "payments/checkout", "catalog/search"));
        var graph = new InMemoryKnowledgeGraph();

        await new ColonyGraphBuilder(_capabilityMap, _lineageStore).BuildAsync(graph);

        var checkoutTool = await graph.GetNodeAsync("tool:payments/checkout");
        checkoutTool.ShouldNotBeNull();
        checkoutTool!.Kind.ShouldBe("tool");

        var searchTool = await graph.GetNodeAsync("tool:catalog/search");
        searchTool.ShouldNotBeNull();
    }

    [Test]
    public async Task BuildAsync_MultipleCapabilities_UnprefixedTool_UsesUnknownKit()
    {
        _capabilityMap.Register(MakeSignal("alpha", "search", "my-tool"));
        var graph = new InMemoryKnowledgeGraph();

        await new ColonyGraphBuilder(_capabilityMap, _lineageStore).BuildAsync(graph);

        var node = await graph.GetNodeAsync("tool:unknown/my-tool");
        node.ShouldNotBeNull();
        node!.Properties["kit"].ShouldBe("unknown");
        node.Properties["name"].ShouldBe("my-tool");
    }

    // ── Lineage edges ────────────────────────────────────────────────

    [Test]
    public async Task BuildAsync_Records_DescendedFrom_Edge()
    {
        await _lineageStore.RecordBirthAsync(new CellLineage
        {
            CellId = "parent-1",
            WorkflowName = "parent",
            Generation = 0,
            BornAt = DateTimeOffset.UtcNow
        });

        await _lineageStore.RecordBirthAsync(new CellLineage
        {
            CellId = "child-1",
            WorkflowName = "child",
            ParentCellId = "parent-1",
            Generation = 1,
            BornAt = DateTimeOffset.UtcNow,
            DivisionReason = "overload"
        });

        var graph = new InMemoryKnowledgeGraph();
        await new ColonyGraphBuilder(_capabilityMap, _lineageStore).BuildAsync(graph);

        var edges = await graph.NeighborsAsync("cell:child-1", "descended_from");
        edges.ShouldContain(e => e.ToId == "cell:parent-1");
        edges.First(e => e.ToId == "cell:parent-1")
             .Properties["divisionReason"].ShouldBe("overload");
    }

    [Test]
    public async Task BuildAsync_FounderCell_HasNoDivisionEdge()
    {
        await _lineageStore.RecordBirthAsync(new CellLineage
        {
            CellId = "founder",
            WorkflowName = "root",
            Generation = 0,
            BornAt = DateTimeOffset.UtcNow
        });

        var graph = new InMemoryKnowledgeGraph();
        await new ColonyGraphBuilder(_capabilityMap, _lineageStore).BuildAsync(graph);

        var edges = await graph.NeighborsAsync("cell:founder", "descended_from");
        edges.ShouldBeEmpty();
    }

    // ── Routing affinity edges ───────────────────────────────────────

    [Test]
    public async Task BuildAsync_WithAffinityTracker_AddsRoutedToEdges()
    {
        _capabilityMap.Register(MakeSignal("alpha", "search"));
        _capabilityMap.Register(MakeSignal("beta", "catalog"));

        var inner = new StubDomainRouter("alpha");
        var tracker = new RoutingAffinityTracker(inner, new StubExploration());
        await tracker.IndexAsync(
            [
                new ChildSpec { Name = "alpha", Domain = "search", Tools = [], Jobs = [] },
                new ChildSpec { Name = "beta",  Domain = "catalog", Tools = [], Jobs = [] }
            ],
            new Dictionary<string, string>());

        tracker.RecordOutcome("alpha", 0.8f);
        tracker.RecordOutcome("beta", 0.5f);

        var graph = new InMemoryKnowledgeGraph();
        await new ColonyGraphBuilder(_capabilityMap, _lineageStore, tracker).BuildAsync(graph);

        var edges = await graph.NeighborsAsync("routing:observed", "routed_to");
        edges.ShouldContain(e => e.ToId == "cell:alpha");
        edges.ShouldContain(e => e.ToId == "cell:beta");
    }

    // ── Stub helpers ─────────────────────────────────────────────────

    private sealed class StubDomainRouter(string always) : IDomainRouter
    {
        public Task<string> RouteAsync(string userMessage, CancellationToken ct = default)
            => Task.FromResult(always);

        public Task IndexAsync(
            IReadOnlyList<ChildSpec> children,
            IReadOnlyDictionary<string, string> toolDescriptions,
            CancellationToken ct = default)
            => Task.CompletedTask;
    }
    private sealed class StubExploration : Ananke.Learning.Exploration.IExplorationStrategy
    {
        public int SelectAction(
            IReadOnlyList<Ananke.Learning.Exploration.ActionCandidate> candidates,
            int totalSelections) => 0;
    }
}
