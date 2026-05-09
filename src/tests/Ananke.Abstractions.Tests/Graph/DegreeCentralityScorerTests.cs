using Ananke.Abstractions.Graph;
using Ananke.Abstractions.Graph.Algorithms;
using Shouldly;

namespace Ananke.Abstractions.Tests.Graph;

[TestFixture]
public sealed class DegreeCentralityScorerTests
{
    // Reference graph:
    //   a <-> b  (out a=1, in a=1 from b)
    //   a -> c
    //   b -> c
    //   b -> d
    //   c -> e
    // Node degrees (count of in+out edges via NeighborsAsync):
    //   a: 3 (out:a->b, a->c; in: b->a)  -> but edges are directed, NeighborsAsync is symmetric
    // Rebuild for clarity.  We use a simple 5-node, 5-edge directed graph:
    //   a -> b, a -> c, b -> c, b -> d, c -> e
    // NeighborsAsync(id) returns all edges where FromId==id OR ToId==id.
    // Degrees: a=2 out; b=1 in + 2 out = 3; c=2 in + 1 out = 3; d=1 in; e=1 in
    // Normalised (max=3): a=2/3, b=1, c=1, d=1/3, e=1/3

    private static readonly (string From, string To)[] Edges =
    [
        ("a", "b"), ("a", "c"), ("b", "c"), ("b", "d"), ("c", "e"),
    ];

    private InMemoryKnowledgeGraph _graph = null!;
    private DegreeCentralityScorer _scorer = null!;

    [SetUp]
    public async Task SetUp()
    {
        _graph = new InMemoryKnowledgeGraph();
        _scorer = new DegreeCentralityScorer();

        foreach (var id in new[] { "a", "b", "c", "d", "e" })
            await _graph.UpsertNodeAsync(new GraphNode { Id = id, Kind = "tag" });

        foreach (var (from, to) in Edges)
            await _graph.UpsertEdgeAsync(new GraphEdge
            {
                FromId     = from,
                ToId       = to,
                Relation   = "co_occurs",
                Provenance = EdgeProvenance.Inferred,
            });
    }

    [Test]
    public async Task ScoreAsync_HandComputedGraph_HighestDegreeNodeScoresOne()
    {
        var scores = await _scorer.ScoreAsync(_graph);

        // b and c both have degree 3 (max); normalised score = 1.
        scores["b"].ShouldBe(1f, tolerance: 1e-6f);
        scores["c"].ShouldBe(1f, tolerance: 1e-6f);
    }

    [Test]
    public async Task ScoreAsync_HandComputedGraph_LowDegreeNodesScore()
    {
        var scores = await _scorer.ScoreAsync(_graph);

        scores["a"].ShouldBe(2f / 3f, tolerance: 1e-6f);
        scores["d"].ShouldBe(1f / 3f, tolerance: 1e-6f);
        scores["e"].ShouldBe(1f / 3f, tolerance: 1e-6f);
    }

    [Test]
    public async Task ScoreAsync_NodeKindFilter_ExcludesOtherKinds()
    {
        // Add a node of a different kind.
        await _graph.UpsertNodeAsync(new GraphNode { Id = "x", Kind = "entry" });
        await _graph.UpsertEdgeAsync(new GraphEdge
        {
            FromId = "x", ToId = "a", Relation = "tagged", Provenance = EdgeProvenance.Extracted,
        });

        var scores = await _scorer.ScoreAsync(_graph, nodeKindFilter: "tag");

        scores.ContainsKey("x").ShouldBeFalse();
        scores.ContainsKey("a").ShouldBeTrue();
    }

    [Test]
    public async Task ScoreAsync_EmptyGraph_ReturnsEmptyDictionary()
    {
        var empty = new InMemoryKnowledgeGraph();
        var scores = await _scorer.ScoreAsync(empty);
        scores.ShouldBeEmpty();
    }
}
