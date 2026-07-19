using Ananke.Abstractions.Graph;
using Shouldly;

namespace Ananke.Abstractions.Tests.Graph;

[TestFixture]
public sealed class InMemoryKnowledgeGraphTests
{
    private InMemoryKnowledgeGraph _graph = null!;

    [SetUp]
    public void SetUp() => _graph = new InMemoryKnowledgeGraph();

    // ── node upsert idempotency ──────────────────────────────────────────────

    [Test]
    public async Task UpsertNodeAsync_Idempotent_ReturnsLatestVersion()
    {
        var n1 = new GraphNode { Id = "a", Kind = "tag" };
        var n2 = new GraphNode { Id = "a", Kind = "entry" };

        await _graph.UpsertNodeAsync(n1);
        await _graph.UpsertNodeAsync(n2);

        var result = await _graph.GetNodeAsync("a");
        result.ShouldNotBeNull();
        result!.Kind.ShouldBe("entry");
        (await _graph.NodeCountAsync()).ShouldBe(1);
    }

    // ── edge upsert idempotency ──────────────────────────────────────────────

    [Test]
    public async Task UpsertEdgeAsync_Idempotent_DoesNotDuplicateEdge()
    {
        var edge = MakeEdge("a", "b", "x", EdgeProvenance.Extracted, 1f);
        await _graph.UpsertEdgeAsync(edge);
        await _graph.UpsertEdgeAsync(edge);

        (await _graph.EdgeCountAsync()).ShouldBe(1);
    }

    // ── edge weight collision ────────────────────────────────────────────────

    [Test]
    public async Task UpsertEdgeAsync_WeightCollision_TakesMax()
    {
        await _graph.UpsertEdgeAsync(MakeEdge("a", "b", "x", EdgeProvenance.Extracted, 0.4f));
        await _graph.UpsertEdgeAsync(MakeEdge("a", "b", "x", EdgeProvenance.Extracted, 0.9f));
        await _graph.UpsertEdgeAsync(MakeEdge("a", "b", "x", EdgeProvenance.Extracted, 0.2f));

        var neighbors = await _graph.NeighborsAsync("a");
        neighbors.Count.ShouldBe(1);
        neighbors[0].Weight.ShouldBe(0.9f);
    }

    // ── provenance promotion ─────────────────────────────────────────────────

    [Test]
    public async Task UpsertEdgeAsync_ProvenancePromotion_InferredBecomesExtracted()
    {
        await _graph.UpsertEdgeAsync(MakeEdge("a", "b", "x", EdgeProvenance.Inferred, 1f));
        await _graph.UpsertEdgeAsync(MakeEdge("a", "b", "x", EdgeProvenance.Extracted, 1f));

        var neighbors = await _graph.NeighborsAsync("a");
        neighbors[0].Provenance.ShouldBe(EdgeProvenance.Extracted);
    }

    [Test]
    public async Task UpsertEdgeAsync_ProvenanceNoDemotion_ExtractedStaysExtracted()
    {
        await _graph.UpsertEdgeAsync(MakeEdge("a", "b", "x", EdgeProvenance.Extracted, 1f));
        await _graph.UpsertEdgeAsync(MakeEdge("a", "b", "x", EdgeProvenance.Inferred, 1f));

        var neighbors = await _graph.NeighborsAsync("a");
        neighbors[0].Provenance.ShouldBe(EdgeProvenance.Extracted);
    }

    [Test]
    public async Task UpsertEdgeAsync_ProvenanceNoDemotion_ExtractedNotDemotedToAmbiguous()
    {
        await _graph.UpsertEdgeAsync(MakeEdge("a", "b", "x", EdgeProvenance.Extracted, 1f));
        await _graph.UpsertEdgeAsync(MakeEdge("a", "b", "x", EdgeProvenance.Ambiguous, 1f));

        var neighbors = await _graph.NeighborsAsync("a");
        neighbors[0].Provenance.ShouldBe(EdgeProvenance.Extracted);
    }

    // ── NeighborsAsync — both in- and out-edges ──────────────────────────────

    [Test]
    public async Task NeighborsAsync_NoRelationFilter_ReturnsBothInAndOutEdges()
    {
        // a -> b and c -> a
        await _graph.UpsertEdgeAsync(MakeEdge("a", "b", "r", EdgeProvenance.Extracted));
        await _graph.UpsertEdgeAsync(MakeEdge("c", "a", "r", EdgeProvenance.Extracted));

        var neighbors = await _graph.NeighborsAsync("a");
        neighbors.Count.ShouldBe(2);
    }

    // ── NeighborsAsync — relation filter ────────────────────────────────────

    [Test]
    public async Task NeighborsAsync_WithRelationFilter_ExcludesOtherRelations()
    {
        await _graph.UpsertEdgeAsync(MakeEdge("a", "b", "follows", EdgeProvenance.Extracted));
        await _graph.UpsertEdgeAsync(MakeEdge("a", "c", "tagged", EdgeProvenance.Extracted));

        var neighbors = await _graph.NeighborsAsync("a", relation: "follows");
        neighbors.Count.ShouldBe(1);
        neighbors[0].ToId.ShouldBe("b");
    }

    // ── GetNodeAsync — missing node ──────────────────────────────────────────

    [Test]
    public async Task GetNodeAsync_UnknownId_ReturnsNull()
    {
        var result = await _graph.GetNodeAsync("nonexistent");
        result.ShouldBeNull();
    }

    // ── GraphNode.EffectiveLabels ─────────────────────────────────────────────

    [Test]
    public void EffectiveLabels_NoLabelsSet_ReturnsJustKind()
    {
        var node = new GraphNode { Id = "a", Kind = "tag" };
        node.EffectiveLabels.ShouldBe(["tag"]);
    }

    [Test]
    public void EffectiveLabels_LabelsRepeatsKind_KindIsFirstWithoutDuplicate()
    {
        var node = new GraphNode { Id = "a", Kind = "Service", Labels = ["Component", "Service"] };
        node.EffectiveLabels.ShouldBe(["Service", "Component"]);
    }

    [Test]
    public void EffectiveLabels_LabelsExcludeKind_KindPrependedAndOthersPreserved()
    {
        var node = new GraphNode { Id = "a", Kind = "Service", Labels = ["Component"] };
        node.EffectiveLabels.ShouldBe(["Service", "Component"]);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static GraphEdge MakeEdge(
        string from, string to, string relation,
        EdgeProvenance provenance, float weight = 1f) =>
        new()
        {
            FromId = from,
            ToId = to,
            Relation = relation,
            Provenance = provenance,
            Weight = weight,
        };
}
