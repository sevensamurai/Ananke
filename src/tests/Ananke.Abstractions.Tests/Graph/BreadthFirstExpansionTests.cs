using Ananke.Abstractions.Graph;
using Ananke.Abstractions.Graph.Algorithms;
using Shouldly;

namespace Ananke.Abstractions.Tests.Graph;

[TestFixture]
public sealed class BreadthFirstExpansionTests
{
    private InMemoryKnowledgeGraph _graph = null!;

    [SetUp]
    public void SetUp() => _graph = new InMemoryKnowledgeGraph();

    // ── hop=0 returns seeds only ─────────────────────────────────────────────

    [Test]
    public async Task ExpandAsync_ZeroHops_ReturnsSeedsOnly()
    {
        await UpsertChain("a", "b", "c");

        var result = await _graph.ExpandAsync(["a"], hops: 0, maxNodes: 100);

        result.Count.ShouldBe(1);
        result[0].Id.ShouldBe("a");
    }

    // ── hop=2 on a 3-node chain returns all three ────────────────────────────

    [Test]
    public async Task ExpandAsync_TwoHops_ReturnsAllThreeNodes()
    {
        // a -> b -> c
        await UpsertChain("a", "b", "c");

        var result = await _graph.ExpandAsync(["a"], hops: 2, maxNodes: 100);

        result.Count.ShouldBe(3);
        result.Select(n => n.Id).ShouldBe(["a", "b", "c"], ignoreOrder: false);
    }

    // ── maxNodes budget is honoured ──────────────────────────────────────────

    [Test]
    public async Task ExpandAsync_MaxNodesBudget_StopsEarly()
    {
        await UpsertChain("a", "b", "c", "d", "e");

        var result = await _graph.ExpandAsync(["a"], hops: 10, maxNodes: 3);

        result.Count.ShouldBe(3);
    }

    // ── deduplicated ─────────────────────────────────────────────────────────

    [Test]
    public async Task ExpandAsync_DuplicateSeeds_DeduplicatesResult()
    {
        await UpsertChain("a", "b");

        var result = await _graph.ExpandAsync(["a", "a"], hops: 2, maxNodes: 100);

        result.Count(n => n.Id == "a").ShouldBe(1);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task UpsertChain(params string[] ids)
    {
        foreach (var id in ids)
            await _graph.UpsertNodeAsync(new GraphNode { Id = id, Kind = "entry" });

        for (var i = 0; i < ids.Length - 1; i++)
            await _graph.UpsertEdgeAsync(new GraphEdge
            {
                FromId     = ids[i],
                ToId       = ids[i + 1],
                Relation   = "follows",
                Provenance = EdgeProvenance.Extracted,
            });
    }
}
