using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Documents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Orchestration.Knowledge.Linking;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task SearchAsync_LinkedChunkUnlikeTheQuery_IsStillReturned()
    {
        var store = new InMemoryKnowledgeStore(new IdFilterCases.OppositeEmbedder());
        await store.UpsertAsync([
            new KnowledgeDocument { Id = "a", Text = "seed" },
            new KnowledgeDocument { Id = "b", Text = "opposite" }]);
        await _graph.AddLinkAsync("a", "b", "extends");
        var linked = new LinkedKnowledgeStore(store, _graph);

        var results = await linked.SearchAsync("query", new SearchOptions { TopK = 5 });

        results.Select(r => r.Id).ShouldBe(["a", "b"], ignoreOrder: true);
    }

    [Test]
    public async Task SearchAsync_OverAStoreThatLists_EmbedsTheQueryOnceHoweverManyLinksAreFollowed()
    {
        var embedder = new CountsEmbeddings(new InMemoryEmbedder());
        var store = new InMemoryKnowledgeStore(embedder);
        await store.UpsertAsync([
            new KnowledgeDocument { Id = "a", Text = "query" },
            new KnowledgeDocument { Id = "b", Text = "beta" },
            new KnowledgeDocument { Id = "c", Text = "gamma" },
            new KnowledgeDocument { Id = "d", Text = "delta" }]);
        await _graph.AddLinkAsync("a", "b", "extends");
        await _graph.AddLinkAsync("a", "c", "extends");
        await _graph.AddLinkAsync("a", "d", "extends");
        var linked = new LinkedKnowledgeStore(store, _graph);
        var before = embedder.Of("query");

        var results = await linked.SearchAsync("query", new SearchOptions { TopK = 4, ScoreThreshold = 0.999f });

        results.Select(r => r.Id).ShouldBe(["a", "b", "c", "d"], ignoreOrder: true);
        (embedder.Of("query") - before).ShouldBe(1);
    }

    [Test]
    public async Task SearchAsync_OverAStoreThatLists_ListsASeedsLinkedChunksOnce()
    {
        var store = new CountsListings(new InMemoryKnowledgeStore(new InMemoryEmbedder()));
        await store.UpsertAsync([
            new KnowledgeDocument { Id = "a", Text = "query" },
            new KnowledgeDocument { Id = "b", Text = "beta" },
            new KnowledgeDocument { Id = "c", Text = "gamma" }]);
        await _graph.AddLinkAsync("a", "b", "extends");
        await _graph.AddLinkAsync("a", "c", "extends");
        await _graph.AddLinkAsync("a", "missing", "extends");
        var linked = new LinkedKnowledgeStore(store, _graph);

        var results = await linked.SearchAsync("query", new SearchOptions { TopK = 4, ScoreThreshold = 0.999f });

        results.Select(r => r.Id).ShouldBe(["a", "b", "c"], ignoreOrder: true);
        store.Listings.ShouldBe(1);
    }

    [Test]
    public async Task SearchAsync_OverAStoreThatLists_ReadsALinkedChunkWithinTheCallersFilter()
    {
        var store = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        await store.UpsertAsync([
            new KnowledgeDocument { Id = "a", Text = "query", Metadata = new Dictionary<string, string> { ["tenant"] = "t1" } },
            new KnowledgeDocument { Id = "b", Text = "beta", Metadata = new Dictionary<string, string> { ["tenant"] = "t1" } },
            new KnowledgeDocument { Id = "c", Text = "gamma", Metadata = new Dictionary<string, string> { ["tenant"] = "t2" } }]);
        await _graph.AddLinkAsync("a", "b", "extends");
        await _graph.AddLinkAsync("a", "c", "extends");
        var linked = new LinkedKnowledgeStore(store, _graph);

        var results = await linked.SearchAsync("query", new SearchOptions { Filter = new KnowledgeFilter { ["tenant"] = "t1" } });

        results.Select(r => r.Id).ShouldBe(["a", "b"], ignoreOrder: true);
    }

#pragma warning disable ANNKEEXP004 // SupersededResults and DocumentHeaderOptions are experimental
    [TestCase(SupersededResults.Mark)]
    [TestCase(SupersededResults.Exclude)]
    public async Task SearchAsync_OverACatalogAwareStore_MarksOrLeavesOutALinkedChunkSupersededInTheCatalog(SupersededResults superseded)
    {
        var embedder = new InMemoryEmbedder();
        var catalog = new InMemoryKnowledgeCatalog(embedder);
        var store = new CatalogAwareKnowledgeStore(new InMemoryKnowledgeStore(embedder), catalog) { SupersededResults = superseded };
        await store.UpsertAsync([
            new KnowledgeDocument { Id = "a", Text = "query", Metadata = new Dictionary<string, string> { ["source"] = "new.md" } },
            new KnowledgeDocument { Id = "b", Text = "beta", Metadata = new Dictionary<string, string> { ["source"] = "old.md" } }]);
        await catalog.IndexAsync((await catalog.GetAsync("old.md"))! with { SupersededBy = "new.md" });
        await _graph.AddLinkAsync("a", "b", "extends");
        var linked = new LinkedKnowledgeStore(store, _graph);

        var results = await linked.SearchAsync("query", new SearchOptions { ScoreThreshold = 0.999f });

        if (superseded == SupersededResults.Exclude)
            results.Select(r => r.Id).ShouldBe(["a"]);
        else
            results.Single(r => r.Id == "b").Metadata.ShouldContainKeyAndValue(DocumentHeaderOptions.SupersededByKey, "new.md");
    }
#pragma warning restore ANNKEEXP004

    [Test]
    public async Task SearchAsync_OverAStoreThatCantList_LooksUpALinkedChunkWithTheCallersMode()
    {
        _inner.VectorResults = [Chunk("a", 0.9f)];
        _inner.ById["b"] = Chunk("b", 0.1f);
        await _graph.AddLinkAsync("a", "b", "extends");
        var store = new LinkedKnowledgeStore(_inner, _graph);

        var results = await store.SearchAsync("query", new SearchOptions { Mode = SearchMode.Hybrid, KeywordWeight = 2 });

        results.Select(r => r.Id).ShouldBe(["a", "b"]);
        var lookup = _inner.Searches.Single(o => o?.Filter?.ContainsKey("id") == true)!;
        lookup.Mode.ShouldBe(SearchMode.Hybrid);
        lookup.KeywordWeight.ShouldBe(2);
        lookup.TopK.ShouldBe(1);
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
    public async Task DeleteAsync_WithSeveralIds_RemovesEachOnesLinksAndPassesTheFilterOn()
    {
        await _graph.AddLinkAsync("a", "b", "extends");
        await _graph.AddLinkAsync("c", "b", "extends");
        await _graph.AddLinkAsync("d", "b", "extends");
        var store = new LinkedKnowledgeStore(_inner, _graph);
#pragma warning disable ANNKEEXP004 // KnowledgeFilter.AnyOf is experimental
        var filter = new KnowledgeFilter { AnyOf = { ["id"] = ["a", "c"] } };
#pragma warning restore ANNKEEXP004

        await store.DeleteAsync(filter);

        (await _graph.GetLinksAsync("a")).ShouldBeEmpty();
        (await _graph.GetLinksAsync("c")).ShouldBeEmpty();
        (await _graph.GetLinksAsync("d")).Count.ShouldBe(1);
        _inner.DeleteCalls.ShouldBe([filter]);
    }

    [Test]
    public async Task DeleteAsync_WithoutIdFilter_OnlyDeletesFromInner_LinksUntouched()
    {
        await _graph.AddLinkAsync("a", "b", "extends");
        var store = new LinkedKnowledgeStore(_inner, _graph);

        await store.DeleteAsync(new KnowledgeFilter { ["source"] = "doc-1" });

        // The fake can't list, so the chunks the delete removed are unknown and only ids the filter names lose links.
        (await _graph.GetLinksAsync("a")).Count.ShouldBe(1);
        _inner.DeleteCalls.Count.ShouldBe(1);
    }

    [Test]
    public async Task DeleteAsync_IdsNarrowedByAnotherKey_RemovesLinksOnlyOfTheChunksDeleted()
    {
        var listable = await ListableWithLinksAsync();
        var store = new LinkedKnowledgeStore(listable, _graph);
#pragma warning disable ANNKEEXP004 // KnowledgeFilter.AnyOf is experimental
        var filter = new KnowledgeFilter { ["page"] = "3", AnyOf = { ["id"] = ["c1", "c2"] } };
#pragma warning restore ANNKEEXP004

        await store.DeleteAsync(filter);

        listable.Count.ShouldBe(2);
        (await _graph.GetLinksAsync("c1")).Count.ShouldBe(1);
        (await _graph.GetLinksAsync("c2")).ShouldBeEmpty();
        (await _graph.GetLinksAsync("c3")).Count.ShouldBe(1);
    }

    [Test]
    public async Task DeleteAsync_FilterNamingNoId_RemovesLinksOfTheChunksDeleted()
    {
        var listable = await ListableWithLinksAsync();
        var store = new LinkedKnowledgeStore(listable, _graph);

        await store.DeleteAsync(new KnowledgeFilter { ["source"] = "doc-1" });

        listable.Count.ShouldBe(1);
        (await _graph.GetLinksAsync("c1")).ShouldBeEmpty();
        (await _graph.GetLinksAsync("c2")).ShouldBeEmpty();
        (await _graph.GetLinksAsync("c3")).Count.ShouldBe(1);
    }

    [Test]
    public async Task DeleteAsync_OverAWrapperOfAStoreThatCantList_RemovesLinksOfTheNamedIdsAsBefore()
    {
        await _graph.AddLinkAsync("a", "b", "extends");
        var catalogAware = new CatalogAwareKnowledgeStore(_inner, new InMemoryKnowledgeCatalog(new InMemoryEmbedder()));
        var store = new LinkedKnowledgeStore(catalogAware, _graph);

        await store.DeleteAsync(new KnowledgeFilter { ["id"] = "a" });

        (await _graph.GetLinksAsync("a")).ShouldBeEmpty();
        _inner.DeleteCalls.Count.ShouldBe(1);
    }

    [Test]
    public async Task DeleteAsync_FilterNamingNoId_ListsTheMatchingChunksOnce()
    {
        var counting = new CountsListings(await ListableWithLinksAsync());
        var store = new LinkedKnowledgeStore(counting, _graph);

        await store.DeleteAsync(new KnowledgeFilter { ["source"] = "doc-1" });

        counting.Listings.ShouldBe(1);
        counting.Count.ShouldBe(1);
        (await _graph.GetLinksAsync("c1")).ShouldBeEmpty();
        (await _graph.GetLinksAsync("c2")).ShouldBeEmpty();
    }

    [Test]
    public async Task DeleteAsync_FilterNamingOnlyIds_DoesNotList()
    {
        var counting = new CountsListings(await ListableWithLinksAsync());
        var store = new LinkedKnowledgeStore(counting, _graph);

        await store.DeleteAsync(new KnowledgeFilter { ["id"] = "c1" });

        counting.Listings.ShouldBe(0);
        counting.Count.ShouldBe(2);
        (await _graph.GetLinksAsync("c1")).ShouldBeEmpty();
        (await _graph.GetLinksAsync("c2")).Count.ShouldBe(1);
    }

    [Test]
    public async Task DeleteAsync_ByIdOfAChunkAlreadyGone_RemovesItsLinks()
    {
        var listable = await ListableWithLinksAsync();
        await _graph.AddLinkAsync("gone", "x", "references");
        var store = new LinkedKnowledgeStore(listable, _graph);

        await store.DeleteAsync(new KnowledgeFilter { ["id"] = "gone" });

        (await _graph.GetLinksAsync("gone")).ShouldBeEmpty();
        listable.Count.ShouldBe(3);
    }

    [Test]
    public async Task DeleteAsync_RemovingLinksFails_LeavesTheChunksForARetry()
    {
        var listable = await ListableWithLinksAsync();
        var store = new LinkedKnowledgeStore(listable, new FailsToRemoveLinks());

        await Should.ThrowAsync<InvalidOperationException>(() => store.DeleteAsync(new KnowledgeFilter { ["source"] = "doc-1" }));

        listable.Count.ShouldBe(3);
    }

    [Test]
    public async Task ListAsync_OverAStoreThatLists_ListsThroughIt()
    {
        var store = new LinkedKnowledgeStore(await ListableWithLinksAsync(), _graph);

        var listed = await store.ListAsync(new KnowledgeFilter { ["page"] = "3" });

        listed.Select(c => c.Id).ShouldBe(["c2", "c3"], ignoreOrder: true);
    }

    [Test]
    public async Task ListAsync_OverAStoreThatCantList_Throws()
    {
        var store = new LinkedKnowledgeStore(_inner, _graph);

        await Should.ThrowAsync<NotSupportedException>(() => store.ListAsync(new KnowledgeFilter { ["source"] = "doc-1" }));
    }

    [Test]
    public async Task AddKnowledgeLinking_OverAStoreThatLists_ResolvesAStoreThatLists()
    {
        var inner = await ListableWithLinksAsync();
        var services = new ServiceCollection();
        services.AddSingleton<IKnowledgeStore>(inner);
        services.AddKnowledgeLinking(o => o.AutoLinkOnIngest = false);
        using var provider = services.BuildServiceProvider();

        var linked = provider.GetRequiredService<IKnowledgeStore>().ShouldBeOfType<LinkedKnowledgeStore>();
        var listed = await linked.ListAsync(new KnowledgeFilter { ["source"] = "doc-2" });

        listed.Select(c => c.Id).ShouldBe(["c3"]);
    }

    [Test]
    public async Task DeleteAsync_EmptyFilter_ThrowsAndKeepsEveryLink()
    {
        var inner = await ListableWithLinksAsync();

        await EmptyFilterDeleteCases.Delete_AnEmptyFilter_ThrowsAndDeletesNothing(new LinkedKnowledgeStore(inner, _graph), inner);

        foreach (var id in new[] { "c1", "c2", "c3" })
            (await _graph.GetLinksAsync(id)).Count.ShouldBe(1);
    }

    [Test]
    public async Task DeleteAsync_EmptyFilterOverAStoreThatAcceptsIt_Throws()
    {
        var store = new LinkedKnowledgeStore(new IgnoresDeletes(await ListableWithLinksAsync()), _graph);

        await Should.ThrowAsync<ArgumentException>(() => store.DeleteAsync(new KnowledgeFilter()));
    }

    /// <summary>
    /// A listable store holding c1 (doc-1, page 1), c2 (doc-1, page 3) and c3 (doc-2, page 3), each linking to x.
    /// </summary>
    private async Task<InMemoryKnowledgeStore> ListableWithLinksAsync()
    {
        var store = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        await store.UpsertAsync([Doc("c1", "doc-1", 1), Doc("c2", "doc-1", 3), Doc("c3", "doc-2", 3)]).ConfigureAwait(false);
        foreach (var id in new[] { "c1", "c2", "c3" })
            await _graph.AddLinkAsync(id, "x", "references").ConfigureAwait(false);
        return store;

        static KnowledgeDocument Doc(string id, string source, int page) => new()
        {
            Id = id,
            Text = $"text of {id}",
            Metadata = new Dictionary<string, string> { ["source"] = source, ["page"] = $"{page}" },
        };
    }

    // ── fake ─────────────────────────────────────────────────────────────────

    /// <summary>Counts how many times each text is embedded.</summary>
    private sealed class CountsEmbeddings(IEmbeddingModel inner) : IEmbeddingModel
    {
        private readonly Dictionary<string, int> _counts = [];

        public int Of(string text) => _counts.GetValueOrDefault(text);

        public Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default)
        {
            _counts[text] = Of(text) + 1;
            return inner.EmbedAsync(text, ct);
        }

        public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
            IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            var all = new List<ReadOnlyMemory<float>>();
            foreach (var t in texts)
                all.Add(await EmbedAsync(t, ct));
            return all;
        }
    }

    /// <summary>A listable store that counts its listings.</summary>
#pragma warning disable ANNKEEXP004 // implements the experimental listing
    private sealed class CountsListings(InMemoryKnowledgeStore inner) : IListableKnowledgeStore
    {
        public int Listings { get; private set; }

        public int Count => inner.Count;

        public Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(string query, SearchOptions? options = null, CancellationToken ct = default) =>
            inner.SearchAsync(query, options, ct);

        public Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default) => inner.UpsertAsync(documents, ct);

        public Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default) => inner.DeleteAsync(filter, ct);

        public Task<IReadOnlyList<KnowledgeChunk>> ListAsync(
            KnowledgeFilter filter, int limit = 1000, KnowledgeDateOrder? order = null, CancellationToken ct = default)
        {
            Listings++;
            return inner.ListAsync(filter, limit, order, ct);
        }

        public Task UpdateMetadataAsync(KnowledgeChunk listed, IReadOnlyDictionary<string, string> metadata, CancellationToken ct = default) =>
            inner.UpdateMetadataAsync(listed, metadata, ct);
    }
#pragma warning restore ANNKEEXP004

    /// <summary>A store that lists but ignores every delete.</summary>
#pragma warning disable ANNKEEXP004 // implements the experimental listing
    private sealed class IgnoresDeletes(InMemoryKnowledgeStore inner) : IListableKnowledgeStore
    {
        public Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(string query, SearchOptions? options = null, CancellationToken ct = default) =>
            inner.SearchAsync(query, options, ct);

        public Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default) => inner.UpsertAsync(documents, ct);

        public Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<KnowledgeChunk>> ListAsync(
            KnowledgeFilter filter, int limit = 1000, KnowledgeDateOrder? order = null, CancellationToken ct = default) =>
            inner.ListAsync(filter, limit, order, ct);

        public Task UpdateMetadataAsync(KnowledgeChunk listed, IReadOnlyDictionary<string, string> metadata, CancellationToken ct = default) =>
            inner.UpdateMetadataAsync(listed, metadata, ct);
    }
#pragma warning restore ANNKEEXP004

    /// <summary>A link graph whose every removal fails.</summary>
    private sealed class FailsToRemoveLinks : IDocumentLinkGraph
    {
        public Task AddLinkAsync(string sourceChunkId, string targetChunkId, string relationship, float weight = 1.0f, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<DocumentLink>> GetLinksAsync(string chunkId, int maxHops = 1, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DocumentLink>>([]);

        public Task RemoveLinksAsync(string chunkId, CancellationToken ct = default) =>
            throw new InvalidOperationException("The link graph is unavailable.");
    }

    /// <summary>
    /// Hand-written <see cref="IKnowledgeStore"/> fake: a search with an <c>id</c> filter returns the chunk set up under
    /// that id in <see cref="ById"/>, any other search <see cref="VectorResults"/>, so each test sets both apart.
    /// </summary>
    private sealed class FakeKnowledgeStore : IKnowledgeStore
    {
        public IReadOnlyList<KnowledgeChunk> VectorResults { get; set; } = [];
        public Dictionary<string, KnowledgeChunk> ById { get; } = [];
        public List<KnowledgeFilter> DeleteCalls { get; } = [];
        public List<IReadOnlyList<KnowledgeDocument>> UpsertCalls { get; } = [];
        public List<SearchOptions?> Searches { get; } = [];

        public Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(
            string query, SearchOptions? options = null, CancellationToken ct = default)
        {
            Searches.Add(options);
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
