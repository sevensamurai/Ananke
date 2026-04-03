using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Linking;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class InMemoryDocumentLinkGraphTests
{
    private InMemoryDocumentLinkGraph _graph = null!;

    [SetUp]
    public void SetUp()
    {
        _graph = new InMemoryDocumentLinkGraph();
    }

    // ── AddLink ──────────────────────────────────────────────────

    [Test]
    public async Task AddLink_ThenGetLinks_ReturnsLink()
    {
        await _graph.AddLinkAsync("a", "b", "references", 0.9f);

        var links = await _graph.GetLinksAsync("a");

        links.Count.ShouldBe(1);
        links[0].SourceId.ShouldBe("a");
        links[0].TargetId.ShouldBe("b");
        links[0].Relationship.ShouldBe("references");
        links[0].Weight.ShouldBe(0.9f);
    }

    [Test]
    public async Task AddLink_SameSourceTarget_OverwritesPrevious()
    {
        await _graph.AddLinkAsync("a", "b", "references", 0.5f);
        await _graph.AddLinkAsync("a", "b", "extends", 0.8f);

        var links = await _graph.GetLinksAsync("a");

        links.Count.ShouldBe(1);
        links[0].Relationship.ShouldBe("extends");
        links[0].Weight.ShouldBe(0.8f);
    }

    [Test]
    public async Task AddLink_ClampsWeightToUnitRange()
    {
        await _graph.AddLinkAsync("a", "b", "references", 1.5f);

        var links = await _graph.GetLinksAsync("a");
        links[0].Weight.ShouldBe(1.0f);
    }

    [Test]
    public async Task AddLink_MultipleTargets_ReturnsAll()
    {
        await _graph.AddLinkAsync("a", "b", "references");
        await _graph.AddLinkAsync("a", "c", "extends");

        var links = await _graph.GetLinksAsync("a");

        links.Count.ShouldBe(2);
        links.ShouldContain(l => l.TargetId == "b");
        links.ShouldContain(l => l.TargetId == "c");
    }

    // ── GetLinks with hops ──────────────────────────────────────

    [Test]
    public async Task GetLinks_SingleHop_DoesNotTraverseDeeper()
    {
        await _graph.AddLinkAsync("a", "b", "references");
        await _graph.AddLinkAsync("b", "c", "extends");

        var links = await _graph.GetLinksAsync("a", maxHops: 1);

        links.Count.ShouldBe(1);
        links[0].TargetId.ShouldBe("b");
    }

    [Test]
    public async Task GetLinks_TwoHops_TraversesTransitively()
    {
        await _graph.AddLinkAsync("a", "b", "references");
        await _graph.AddLinkAsync("b", "c", "extends");

        var links = await _graph.GetLinksAsync("a", maxHops: 2);

        links.Count.ShouldBe(2);
        links.ShouldContain(l => l.TargetId == "b");
        links.ShouldContain(l => l.TargetId == "c");
    }

    [Test]
    public async Task GetLinks_CycleDoesNotCauseInfiniteLoop()
    {
        await _graph.AddLinkAsync("a", "b", "references");
        await _graph.AddLinkAsync("b", "a", "references");

        var links = await _graph.GetLinksAsync("a", maxHops: 3);

        // Should find a→b and b→a but not loop
        links.Count.ShouldBe(2);
    }

    [Test]
    public async Task GetLinks_NoLinks_ReturnsEmpty()
    {
        var links = await _graph.GetLinksAsync("nonexistent");

        links.ShouldBeEmpty();
    }

    // ── RemoveLinks ──────────────────────────────────────────────

    [Test]
    public async Task RemoveLinks_RemovesOutbound()
    {
        await _graph.AddLinkAsync("a", "b", "references");
        await _graph.AddLinkAsync("a", "c", "extends");

        await _graph.RemoveLinksAsync("a");

        var links = await _graph.GetLinksAsync("a");
        links.ShouldBeEmpty();
    }

    [Test]
    public async Task RemoveLinks_RemovesInbound()
    {
        await _graph.AddLinkAsync("a", "target", "references");
        await _graph.AddLinkAsync("b", "target", "extends");

        await _graph.RemoveLinksAsync("target");

        // Links from a and b to target should be gone
        var linksA = await _graph.GetLinksAsync("a");
        var linksB = await _graph.GetLinksAsync("b");

        linksA.ShouldBeEmpty();
        linksB.ShouldBeEmpty();
    }

    [Test]
    public async Task RemoveLinks_PreservesUnrelatedLinks()
    {
        await _graph.AddLinkAsync("a", "b", "references");
        await _graph.AddLinkAsync("c", "d", "extends");

        await _graph.RemoveLinksAsync("a");

        var links = await _graph.GetLinksAsync("c");
        links.Count.ShouldBe(1);
        links[0].TargetId.ShouldBe("d");
    }

    // ── LinkCount ────────────────────────────────────────────────

    [Test]
    public async Task LinkCount_ReflectsStoredLinks()
    {
        _graph.LinkCount.ShouldBe(0);

        await _graph.AddLinkAsync("a", "b", "references");
        await _graph.AddLinkAsync("a", "c", "extends");

        _graph.LinkCount.ShouldBe(2);
    }

    // ── Validation ───────────────────────────────────────────────

    [Test]
    public void AddLink_NullSourceId_Throws()
    {
        Should.ThrowAsync<ArgumentException>(() =>
            _graph.AddLinkAsync(null!, "b", "references"));
    }

    [Test]
    public void AddLink_NullTargetId_Throws()
    {
        Should.ThrowAsync<ArgumentException>(() =>
            _graph.AddLinkAsync("a", null!, "references"));
    }

    [Test]
    public void AddLink_NullRelationship_Throws()
    {
        Should.ThrowAsync<ArgumentException>(() =>
            _graph.AddLinkAsync("a", "b", null!));
    }

    [Test]
    public void GetLinks_NullChunkId_Throws()
    {
        Should.ThrowAsync<ArgumentException>(() =>
            _graph.GetLinksAsync(null!));
    }

    [Test]
    public void GetLinks_ZeroHops_Throws()
    {
        Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            _graph.GetLinksAsync("a", maxHops: 0));
    }
}
