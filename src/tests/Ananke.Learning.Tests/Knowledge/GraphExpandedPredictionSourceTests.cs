using Ananke.Abstractions.Graph;
using Ananke.Abstractions.Agents;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Learning.Knowledge.Builders;
using Ananke.Learning.Knowledge.Retrieval;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

namespace Ananke.Learning.Tests.Knowledge;

[TestFixture]
public sealed class GraphExpandedPredictionSourceTests
{
    // ── Multi-hop scenario ───────────────────────────────────────────────────
    // Tags: topic/A, topic/B, topic/C form a chain: A co_occurs B, B co_occurs C.
    // Query entry is tagged only with topic/A.
    // Answer entry is tagged only with topic/C.
    // Pure tag-overlap cannot reach the answer; graph expansion (2 hops) can.

    [Test]
    public async Task PredictAsync_MultiHopEntry_IsReachableViaGraphExpansion()
    {
        var graph = new InMemoryKnowledgeGraph();
        var memory = new InMemoryEmpiricalMemory(new InMemoryEmbedder());

        // Populate graph: tag chain A → B → C.
        await graph.UpsertNodeAsync(new GraphNode { Id = "tag:topic/A", Kind = "tag" });
        await graph.UpsertNodeAsync(new GraphNode { Id = "tag:topic/B", Kind = "tag" });
        await graph.UpsertNodeAsync(new GraphNode { Id = "tag:topic/C", Kind = "tag" });

        await graph.UpsertEdgeAsync(new GraphEdge { FromId = "tag:topic/A", ToId = "tag:topic/B", Relation = "co_occurs", Provenance = EdgeProvenance.Inferred, Weight = 0.8f });
        await graph.UpsertEdgeAsync(new GraphEdge { FromId = "tag:topic/B", ToId = "tag:topic/A", Relation = "co_occurs", Provenance = EdgeProvenance.Inferred, Weight = 0.8f });
        await graph.UpsertEdgeAsync(new GraphEdge { FromId = "tag:topic/B", ToId = "tag:topic/C", Relation = "co_occurs", Provenance = EdgeProvenance.Inferred, Weight = 0.8f });
        await graph.UpsertEdgeAsync(new GraphEdge { FromId = "tag:topic/C", ToId = "tag:topic/B", Relation = "co_occurs", Provenance = EdgeProvenance.Inferred, Weight = 0.8f });

        // "Answer" entry — tagged only with topic/C.
        var answerEntry = MakeEntry("answer", ["topic/C"], [0.8f]);
        await graph.UpsertNodeAsync(new GraphNode { Id = "entry:answer", Kind = "entry" });
        await graph.UpsertEdgeAsync(new GraphEdge { FromId = "entry:answer", ToId = "tag:topic/C", Relation = "tagged", Provenance = EdgeProvenance.Extracted });
        var committed = await memory.CommitAsync(answerEntry);

        // Query entry — tagged only with topic/A.
        var queryEntry = MakeEntry("query", ["topic/A"], [0.9f]);
        var committedQuery = await memory.CommitAsync(queryEntry);

        var source = new GraphExpandedPredictionSource(graph, neighborCount: 5, hops: 3, maxExpandNodes: 50);
        var prediction = await source.PredictAsync(committedQuery, memory);

        // The graph-expanded source should produce a non-null prediction by reaching
        // the answer entry via: topic/A → topic/B → topic/C → entry:answer.
        prediction.ShouldNotBeNull();
        prediction!.Value.ShouldBeInRange(0f, 1f);
    }

    [Test]
    public async Task PredictAsync_EntryWithNoTags_ReturnsNull()
    {
        var graph = new InMemoryKnowledgeGraph();
        var memory = new InMemoryEmpiricalMemory(new InMemoryEmbedder());

        var entry = await memory.CommitAsync(MakeEntry("e1", [], []));
        var source = new GraphExpandedPredictionSource(graph);

        var prediction = await source.PredictAsync(entry, memory);
        prediction.ShouldBeNull();
    }

    [Test]
    public async Task PredictAsync_EmptyGraph_ReturnsNull()
    {
        var graph = new InMemoryKnowledgeGraph();
        var memory = new InMemoryEmpiricalMemory(new InMemoryEmbedder());

        var entry = await memory.CommitAsync(MakeEntry("e1", ["topic/A"], [0.9f]));
        var source = new GraphExpandedPredictionSource(graph);

        var prediction = await source.PredictAsync(entry, memory);
        prediction.ShouldBeNull();
    }

    // ── helper ──────────────────────────────────────────────────────────────

    private static EmpiricalEntry MakeEntry(
        string id, string[] tagKeys, float[] weights) => new()
        {
            Id = id,
            Kind = EmpiricalKind.Pattern,
            Tags = [],
            Source = "test",
            Confidence = 0.7f,
            ObservationCount = 1,
            Evidence = [],
            FirstObserved = DateTimeOffset.UtcNow,
            LastObserved = DateTimeOffset.UtcNow,
            Description = new SemanticDescription
            {
                Summary = id,
                SemanticTags = tagKeys.Zip(weights)
                .ToDictionary(t => t.First, t => t.Second),
            },
        };
}
