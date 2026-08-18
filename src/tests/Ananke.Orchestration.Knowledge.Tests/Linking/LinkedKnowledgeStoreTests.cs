using Ananke.Orchestration.Knowledge.Linking;
using Shouldly;

namespace Ananke.Orchestration.Knowledge.Tests.Linking;

[TestFixture]
public class LinkedKnowledgeStoreTests
{
    private FakeKnowledgeStore _inner = null!;
    private InMemoryDocumentLinkGraph _graph = null!;

    [SetUp]
    public void SetUp()
    {
        _inner = new FakeKnowledgeStore();
        _graph = new InMemoryDocumentLinkGraph();
    }

    private static KnowledgeChunk Chunk(string id, float score, string text = "text") => new()
    {
        Id = id,
        Text = text,
        Score = score,
        Metadata = new Dictionary<string, string>()
    };

    [Test]
    public void Constructor_NullInner_Throws() =>
        Should.Throw<ArgumentNullException>(() => new LinkedKnowledgeStore(null!, _graph));

    [Test]
    public void Constructor_NullGraph_Throws() =>
        Should.Throw<ArgumentNullException>(() => new LinkedKnowledgeStore(_inner, null!));

    [Test]
    public async Task SearchAsync_ExpandGraphDisabled_ReturnsInnerResultsUnchanged()
    {
        _inner.VectorResults = [Chunk("a", 0.9f)];
        var store = new LinkedKnowledgeStore(_inner, _graph, new LinkedSearchOptions { ExpandGraph = false });

        var results = await store.SearchAsync("query");

        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe("a");
    }

    [Test]
    public async Task SearchAsync_NoInnerResults_ReturnsEmptyWithoutGraphLookup()
    {
        _inner.VectorResults = [];
        var store = new LinkedKnowledgeStore(_inner, _graph);

        var results = await store.SearchAsync("query");

        results.ShouldBeEmpty();
    }

    [Test]
    public async Task SearchAsync_SeedWithNoLinks_ReturnsOnlyVectorResult()
    {
        _inner.VectorResults = [Chunk("a", 0.9f)];
        var store = new LinkedKnowledgeStore(_inner, _graph);

        var results = await store.SearchAsync("query");

        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe("a");
    }

    [Test]
    public async Task SearchAsync_SeedWithLink_MergesLinkedChunkWeightedByScoreAndLinkWeight()
    {
        _inner.VectorResults = [Chunk("a", 0.8f)];
        _inner.ById["b"] = Chunk("b", 999f); // stored score is irrelevant; graph score replaces it
        await _graph.AddLinkAsync("a", "b", "extends", weight: 0.5f);
        var store = new LinkedKnowledgeStore(_inner, _graph);

        var results = await store.SearchAsync("query");

        results.Count.ShouldBe(2);
        var linked = results.Single(r => r.Id == "b");
        // graphScore = seed.Score(0.8) * link.Weight(0.5) * GraphScoreDiscount(0.8 default) = 0.32
        linked.Score.ShouldBe(0.32f, tolerance: 0.001f);
    }

    [Test]
    public async Task SearchAsync_LinkedChunkAlreadyAmongVectorResults_IsNotDuplicated()
    {
        _inner.VectorResults = [Chunk("a", 0.9f), Chunk("b", 0.85f)];
        _inner.ById["b"] = Chunk("b", 0.85f);
        await _graph.AddLinkAsync("a", "b", "extends", weight: 0.9f);
        var store = new LinkedKnowledgeStore(_inner, _graph);

        var results = await store.SearchAsync("query");

        results.Count.ShouldBe(2);
        // "b" keeps its original vector score (0.85), not the graph-derived one.
        results.Single(r => r.Id == "b").Score.ShouldBe(0.85f);
    }

    [Test]
    public async Task SearchAsync_LinkedChunkNotFoundInInnerStore_IsSkippedGracefully()
    {
        _inner.VectorResults = [Chunk("a", 0.9f)];
        // No "missing" entry registered in _inner.ById.
        await _graph.AddLinkAsync("a", "missing", "extends", weight: 0.9f);
        var store = new LinkedKnowledgeStore(_inner, _graph);

        await Should.NotThrowAsync(async () => await store.SearchAsync("query"));
        var results = await store.SearchAsync("query");

        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe("a");
    }

    [Test]
    public async Task SearchAsync_OnlyTopExpansionSeeds_AreTraversed()
    {
        _inner.VectorResults = [Chunk("a", 0.9f), Chunk("b", 0.8f), Chunk("c", 0.7f)];
        _inner.ById["from-c"] = Chunk("from-c", 0.5f);
        // Only "c" (the 3rd result) has a link; with ExpansionSeeds=2, "c" is never used as a seed.
        await _graph.AddLinkAsync("c", "from-c", "extends", weight: 0.9f);
        var store = new LinkedKnowledgeStore(_inner, _graph, new LinkedSearchOptions { ExpansionSeeds = 2 });

        var results = await store.SearchAsync("query");

        results.Select(r => r.Id).ShouldNotContain("from-c");
    }

    [Test]
    public async Task SearchAsync_ResultsAreOrderedByDescendingScoreAndTruncatedToTopK()
    {
        _inner.VectorResults = [Chunk("a", 0.5f)];
        _inner.ById["b"] = Chunk("b", 0.1f);
        _inner.ById["c"] = Chunk("c", 0.1f);
        await _graph.AddLinkAsync("a", "b", "extends", weight: 1.0f); // graphScore = 0.5*1.0*0.8 = 0.40
        await _graph.AddLinkAsync("a", "c", "extends", weight: 0.1f); // graphScore = 0.5*0.1*0.8 = 0.04
        var store = new LinkedKnowledgeStore(_inner, _graph);

        var results = await store.SearchAsync("query", new SearchOptions { TopK = 2 });

        results.Count.ShouldBe(2);
        results[0].Id.ShouldBe("a"); // 0.5
        results[1].Id.ShouldBe("b"); // 0.40 beats c's 0.04, so c is truncated away
    }

    [Test]
    public async Task UpsertAsync_DelegatesToInnerStore()
    {
        var store = new LinkedKnowledgeStore(_inner, _graph);
        var docs = new[] { new KnowledgeDocument { Id = "a", Text = "text" } };

        await store.UpsertAsync(docs);

        _inner.UpsertCalls.Count.ShouldBe(1);
    }

    [Test]
    public async Task DeleteAsync_WithIdFilter_RemovesLinksThenDeletesFromInner()
    {
        await _graph.AddLinkAsync("a", "b", "extends");
        var store = new LinkedKnowledgeStore(_inner, _graph);

        await store.DeleteAsync(new KnowledgeFilter { ["id"] = "a" });

        (await _graph.GetLinksAsync("a")).ShouldBeEmpty();
        _inner.DeleteCalls.Count.ShouldBe(1);
    }

    [Test]
    public async Task DeleteAsync_WithoutIdFilter_OnlyDeletesFromInner_LinksUntouched()
    {
        await _graph.AddLinkAsync("a", "b", "extends");
        var store = new LinkedKnowledgeStore(_inner, _graph);

        await store.DeleteAsync(new KnowledgeFilter { ["source"] = "doc-1" });

        // The link graph has no notion of "source", so nothing was removable — and nothing should
        // have been attempted, since no "id" key was present in the filter.
        (await _graph.GetLinksAsync("a")).Count.ShouldBe(1);
        _inner.DeleteCalls.Count.ShouldBe(1);
    }

    // ── fake ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Hand-written <see cref="IKnowledgeStore"/> fake. <see cref="LinkedKnowledgeStore"/> fetches
    /// a linked chunk by an <c>"id"</c> metadata filter, which <see cref="InMemoryKnowledgeStore"/>
    /// only matches when the caller has separately stored that value in chunk metadata — not what
    /// these tests are exercising, so a fake with direct by-ID lookup keeps the tests deterministic.
    /// </summary>
    private sealed class FakeKnowledgeStore : IKnowledgeStore
    {
        public IReadOnlyList<KnowledgeChunk> VectorResults { get; set; } = [];
        public Dictionary<string, KnowledgeChunk> ById { get; } = [];
        public List<KnowledgeFilter> DeleteCalls { get; } = [];
        public List<IReadOnlyList<KnowledgeDocument>> UpsertCalls { get; } = [];

        public Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(
            string query, SearchOptions? options = null, CancellationToken ct = default)
        {
            if (options?.Filter is { } filter && filter.TryGetValue("id", out var id))
            {
                return Task.FromResult<IReadOnlyList<KnowledgeChunk>>(
                    ById.TryGetValue(id, out var chunk) ? [chunk] : []);
            }

            return Task.FromResult(VectorResults);
        }

        public Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default)
        {
            UpsertCalls.Add(documents.ToList());
            return Task.CompletedTask;
        }

        public Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default)
        {
            DeleteCalls.Add(filter);
            return Task.CompletedTask;
        }
    }
}
