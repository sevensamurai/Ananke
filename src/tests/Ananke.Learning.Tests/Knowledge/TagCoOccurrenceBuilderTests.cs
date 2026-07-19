using Ananke.Abstractions.Graph;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Learning.Knowledge.Builders;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

namespace Ananke.Learning.Tests.Knowledge;

[TestFixture]
public sealed class TagCoOccurrenceBuilderTests
{
    private InMemoryKnowledgeGraph _graph = null!;
    private InMemoryEmpiricalMemory _memory = null!;
    private TagCoOccurrenceBuilder _builder = null!;

    [SetUp]
    public void SetUp()
    {
        _graph = new InMemoryKnowledgeGraph();
        // dedupThreshold>1.0 disables semantic dedup so each CommitAsync stores a fresh entry.
        _memory = new InMemoryEmpiricalMemory(new InMemoryEmbedder(), dedupThreshold: 1.1f);
        _builder = new TagCoOccurrenceBuilder(_memory);
    }

    [Test]
    public async Task BuildAsync_ProducesEntryAndTagNodes()
    {
        await _memory.CommitAsync(MakeEntry("e1", ["cause/gc", "effect/oom"], [0.8f, 0.6f]));

        await _builder.BuildAsync(_graph);

        var entryNode = await _graph.GetNodeAsync("entry:e1");
        var tagNode = await _graph.GetNodeAsync("tag:cause/gc");

        entryNode.ShouldNotBeNull();
        entryNode!.Kind.ShouldBe("entry");
        tagNode.ShouldNotBeNull();
        tagNode!.Kind.ShouldBe("tag");
    }

    [Test]
    public async Task BuildAsync_ProducesTaggedEdges_WithCorrectWeight()
    {
        await _memory.CommitAsync(MakeEntry("e1", ["cause/gc"], [0.75f]));

        await _builder.BuildAsync(_graph);

        var edges = await _graph.NeighborsAsync("entry:e1", relation: "tagged");
        edges.Count.ShouldBe(1);
        edges[0].Weight.ShouldBe(0.75f, tolerance: 1e-6f);
        edges[0].Provenance.ShouldBe(EdgeProvenance.Extracted);
    }

    [Test]
    public async Task BuildAsync_ProducesCoOccursEdges_BothDirections()
    {
        // Entry has 2 tags → 1 co-occurrence pair → 2 directed edges.
        await _memory.CommitAsync(MakeEntry("e1", ["cause/gc", "effect/oom"], [0.8f, 0.5f]));

        await _builder.BuildAsync(_graph);

        var forward = await _graph.NeighborsAsync("tag:cause/gc", relation: "co_occurs");
        var backward = await _graph.NeighborsAsync("tag:effect/oom", relation: "co_occurs");

        forward.ShouldContain(e => e.ToId == "tag:effect/oom");
        backward.ShouldContain(e => e.ToId == "tag:cause/gc");
    }

    [Test]
    public async Task BuildAsync_CoOccursWeight_IsGeometricMean()
    {
        // weight_A=0.8, weight_B=0.5  → geometric mean = sqrt(0.4) ≈ 0.6325
        await _memory.CommitAsync(MakeEntry("e1", ["a", "b"], [0.8f, 0.5f]));

        await _builder.BuildAsync(_graph);

        var edges = (await _graph.NeighborsAsync("tag:a", relation: "co_occurs"))
            .Where(e => e.ToId == "tag:b")
            .ToList();

        edges.Count.ShouldBe(1);
        edges[0].Weight.ShouldBe(MathF.Sqrt(0.8f * 0.5f), tolerance: 1e-5f);
    }

    [Test]
    public async Task BuildAsync_EntryWithNoTags_ProducesOnlyEntryNode()
    {
        await _memory.CommitAsync(MakeEntry("e1", [], []));

        await _builder.BuildAsync(_graph);

        var entryNode = await _graph.GetNodeAsync("entry:e1");
        entryNode.ShouldNotBeNull();
        (await _graph.EdgeCountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task BuildAsync_MultipleEntries_AccumulatesCoOccurrenceWeights()
    {
        // Two entries share the same tag pair → co_occurs edge weight = max of the two.
        await _memory.CommitAsync(MakeEntry("e1", ["a", "b"], [0.6f, 0.6f]));
        await _memory.CommitAsync(MakeEntry("e2", ["a", "b"], [0.9f, 0.9f]));

        await _builder.BuildAsync(_graph);

        var edges = (await _graph.NeighborsAsync("tag:a", relation: "co_occurs"))
            .Where(e => e.ToId == "tag:b")
            .ToList();

        edges.Count.ShouldBe(1);
        edges[0].Weight.ShouldBe(MathF.Sqrt(0.9f * 0.9f), tolerance: 1e-5f);
    }

    // ── helper ──────────────────────────────────────────────────────────────

    private static EmpiricalEntry MakeEntry(
        string id, string[] tagKeys, float[] weights) => new()
        {
            Id = id,
            Kind = EmpiricalKind.Pattern,
            Tags = [],
            Source = "test",
            Confidence = 0.5f,
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
