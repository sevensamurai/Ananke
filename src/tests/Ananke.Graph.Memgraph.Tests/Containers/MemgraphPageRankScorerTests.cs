using Ananke.Abstractions.Graph;
using Ananke.Graph.Abstractions;
using Ananke.Graph.Memgraph;
using Shouldly;

namespace Ananke.Graph.Memgraph.Tests.Containers;

/// <summary>
/// Integration tests for <see cref="MemgraphPageRankScorer"/> (which needs MAGE), against the Memgraph that <see cref="MemgraphServer"/> starts.
/// </summary>
[TestFixture]
[Category("Containers")]
[Explicit("Starts a container. Run with: bash scripts/ci.sh --containers")]
public class MemgraphPageRankScorerTests
{
    private MemgraphKnowledgeGraph _graph = null!;
    private MemgraphPageRankScorer _scorer = null!;

    [SetUp]
    public void SetUp()
    {
        var factory = MemgraphServer.Require();
        _graph = new MemgraphKnowledgeGraph(factory);
        _scorer = new MemgraphPageRankScorer(factory);
    }

    [Test]
    public async Task ScoreAsync_NodeKindFilterMatchesSecondaryLabel_IncludesNode()
    {
        var a = MakeNode("Service", "Component");
        var b = MakeNode("Service", "Component");
        var other = MakeNode("Entity");

        await _graph.UpsertNodeAsync(a);
        await _graph.UpsertNodeAsync(b);
        await _graph.UpsertNodeAsync(other);
        await _graph.UpsertEdgeAsync(new GraphEdge
        {
            FromId = a.Id,
            ToId = b.Id,
            Relation = "CALLS",
            Provenance = EdgeProvenance.Extracted,
        });

        var scores = await _scorer.ScoreAsync(_graph, nodeKindFilter: "Component");

        scores.ContainsKey(a.Id).ShouldBeTrue();
        scores.ContainsKey(b.Id).ShouldBeTrue();
        scores.ContainsKey(other.Id).ShouldBeFalse();
    }

    private static GraphNode MakeNode(string kind, params string[] labels) =>
        new() { Id = $"test-{Guid.NewGuid():N}", Kind = kind, Labels = labels };
}
