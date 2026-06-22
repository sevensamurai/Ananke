using Ananke.Abstractions.Graph;
using Ananke.Abstractions.Graph.Algorithms;
using Shouldly;

namespace Ananke.Abstractions.Tests.Graph;

[TestFixture]
public sealed class PageRankCentralityScorerTests
{
    private PageRankCentralityScorer _scorer = null!;

    [SetUp]
    public void SetUp() => _scorer = new PageRankCentralityScorer();

    // ── 3-node chain: a -> b -> c ────────────────────────────────────────────
    // PageRank (d=0.85) for a->b->c (no back-edges, dangling node c):
    // Steady-state roughly: a ≈ 0.052, b ≈ 0.099, c ≈ 0.849 (sink absorbs rank).
    // We verify ordering (a < b < c) and that scores sum to ≈1.

    [Test]
    public async Task ScoreAsync_ThreeNodeChain_OrderingAndSumCorrect()
    {
        var graph = new InMemoryKnowledgeGraph();
        await graph.UpsertNodeAsync(new GraphNode { Id = "a", Kind = "tag" });
        await graph.UpsertNodeAsync(new GraphNode { Id = "b", Kind = "tag" });
        await graph.UpsertNodeAsync(new GraphNode { Id = "c", Kind = "tag" });

        await graph.UpsertEdgeAsync(Edge("a", "b"));
        await graph.UpsertEdgeAsync(Edge("b", "c"));

        var scores = await _scorer.ScoreAsync(graph);

        scores.Count.ShouldBe(3);

        // Scores sum to ≈ 1 (within floating point tolerance).
        var sum = scores.Values.Sum();
        sum.ShouldBe(1f, tolerance: 1e-4f);

        // Ordering: sink (c) has highest rank; source (a) has lowest.
        scores["c"].ShouldBeGreaterThan(scores["b"]);
        scores["b"].ShouldBeGreaterThan(scores["a"]);
    }

    // ── convergence on 100-node random-ish graph ─────────────────────────────

    [Test]
    public async Task ScoreAsync_LargeGraph_ConvergesWithinMaxIterations()
    {
        var graph = new InMemoryKnowledgeGraph();
        const int n = 100;

        for (var i = 0; i < n; i++)
            await graph.UpsertNodeAsync(new GraphNode { Id = $"n{i}", Kind = "node" });

        // Ring + some skip edges.
        for (var i = 0; i < n; i++)
        {
            await graph.UpsertEdgeAsync(Edge($"n{i}", $"n{(i + 1) % n}"));
            await graph.UpsertEdgeAsync(Edge($"n{i}", $"n{(i + 7) % n}"));
        }

        var scores = await _scorer.ScoreAsync(graph);

        scores.Count.ShouldBe(n);
        scores.Values.Sum().ShouldBe(1f, tolerance: 1e-3f);
    }

    // ── nodeKindFilter ───────────────────────────────────────────────────────

    [Test]
    public async Task ScoreAsync_NodeKindFilter_OnlyScoreMatchingKind()
    {
        var graph = new InMemoryKnowledgeGraph();
        await graph.UpsertNodeAsync(new GraphNode { Id = "t1", Kind = "tag" });
        await graph.UpsertNodeAsync(new GraphNode { Id = "t2", Kind = "tag" });
        await graph.UpsertNodeAsync(new GraphNode { Id = "e1", Kind = "entry" });

        await graph.UpsertEdgeAsync(Edge("t1", "t2"));

        var scores = await _scorer.ScoreAsync(graph, nodeKindFilter: "tag");

        scores.Count.ShouldBe(2);
        scores.ContainsKey("e1").ShouldBeFalse();
    }

    [Test]
    public async Task ScoreAsync_NodeKindFilterMatchesSecondaryLabel_IncludesNode()
    {
        var graph = new InMemoryKnowledgeGraph();
        await graph.UpsertNodeAsync(new GraphNode { Id = "s1", Kind = "Service", Labels = ["Component"] });
        await graph.UpsertNodeAsync(new GraphNode { Id = "s2", Kind = "Service", Labels = ["Component"] });
        await graph.UpsertNodeAsync(new GraphNode { Id = "e1", Kind = "Entity" });

        await graph.UpsertEdgeAsync(Edge("s1", "s2"));

        var scores = await _scorer.ScoreAsync(graph, nodeKindFilter: "Component");

        scores.Count.ShouldBe(2);
        scores.ContainsKey("e1").ShouldBeFalse();
    }

    private static GraphEdge Edge(string from, string to) => new()
    {
        FromId     = from,
        ToId       = to,
        Relation   = "co_occurs",
        Provenance = EdgeProvenance.Inferred,
    };
}
