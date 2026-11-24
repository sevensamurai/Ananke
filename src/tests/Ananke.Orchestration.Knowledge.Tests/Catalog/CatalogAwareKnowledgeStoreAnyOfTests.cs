using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

#pragma warning disable ANNKEEXP004 // KnowledgeFilter.AnyOf and listing are experimental

namespace Ananke.Orchestration.Knowledge.Tests.Catalog;

/// <summary>
/// <see cref="CatalogAwareKnowledgeStore"/> passes a filter's <see cref="KnowledgeFilter.AnyOf"/> to the store it
/// wraps, and a delete of several sources removes each one's catalog entry, unless another key narrowed the delete and
/// chunks of the source remain.
/// </summary>
[TestFixture]
public class CatalogAwareKnowledgeStoreAnyOfTests
{
    private InMemoryKnowledgeStore _inner = null!;
    private InMemoryKnowledgeCatalog _catalog = null!;
    private CatalogAwareKnowledgeStore _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        var embedder = new InMemoryEmbedder();
        _inner = new InMemoryKnowledgeStore(embedder);
        _catalog = new InMemoryKnowledgeCatalog(embedder);
        _store = new CatalogAwareKnowledgeStore(_inner, _catalog);
        await _store.UpsertAsync([Doc("doc-a"), Doc("doc-b"), Doc("doc-c")]);
    }

    private static KnowledgeDocument Doc(string source) => new()
    {
        Id = $"{source}-1",
        Text = $"release plan in {source}",
        Metadata = new Dictionary<string, string> { ["source"] = source },
    };

    private static KnowledgeFilter TwoSources() => new() { AnyOf = { ["source"] = ["doc-a", "doc-b"] } };

    [Test]
    public async Task SearchAsync_SeveralValuesForOneKey_PassesThemThrough()
    {
        var results = await _store.SearchAsync("release plan", new SearchOptions { Mode = SearchMode.Sparse, TopK = 10, Filter = TwoSources() });

        results.Select(r => r.Id).Order().ShouldBe(["doc-a-1", "doc-b-1"]);
    }

    [Test]
    public async Task ListAsync_SeveralValuesForOneKey_PassesThemThrough()
    {
        (await _store.ListAsync(TwoSources())).Select(c => c.Id).ShouldBe(["doc-a-1", "doc-b-1"]);
    }

    [Test]
    public async Task DeleteAsync_SeveralSources_RemovesTheirChunksAndCatalogEntries()
    {
        await _store.DeleteAsync(TwoSources());

        (await _inner.ListAsync(new KnowledgeFilter())).Select(c => c.Id).ShouldBe(["doc-c-1"]);
        (await _catalog.GetAsync("doc-a")).ShouldBeNull();
        (await _catalog.GetAsync("doc-b")).ShouldBeNull();
        (await _catalog.GetAsync("doc-c")).ShouldNotBeNull();
    }

    [Test]
    public async Task DeleteAsync_ASourceAndSeveralSources_RemovesOnlyTheEntryBothName()
    {
        var filter = TwoSources();
        filter["source"] = "doc-b";

        await _store.DeleteAsync(filter);

        (await _catalog.GetAsync("doc-a")).ShouldNotBeNull();
        (await _catalog.GetAsync("doc-b")).ShouldBeNull();
    }

    [Test]
    public async Task DeleteAsync_SeveralSourcesNarrowedByAnotherKey_KeepsTheEntriesOfSourcesWithChunksLeft()
    {
        var inner = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        var store = new CatalogAwareKnowledgeStore(inner, _catalog);
        await store.UpsertAsync([Page("doc-d", 1), Page("doc-d", 3), Page("doc-e", 3)]);
        var filter = new KnowledgeFilter { ["page"] = "3", AnyOf = { ["source"] = ["doc-d", "doc-e"] } };

        await store.DeleteAsync(filter);

        (await inner.ListAsync(new KnowledgeFilter())).Select(c => c.Id).ShouldBe(["doc-d-p1"]);
        (await _catalog.GetAsync("doc-d")).ShouldNotBeNull();
        (await _catalog.GetAsync("doc-e")).ShouldBeNull();
    }

    [Test]
    public async Task DeleteAsync_NarrowedOverAStoreThatCantList_RemovesTheEntriesAsBefore()
    {
        var inner = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        var store = new CatalogAwareKnowledgeStore(new SearchOnly(inner), _catalog);
        await store.UpsertAsync([Page("doc-d", 1), Page("doc-d", 3)]);

        await store.DeleteAsync(new KnowledgeFilter { ["source"] = "doc-d", ["page"] = "3" });

        (await inner.ListAsync(new KnowledgeFilter())).Select(c => c.Id).ShouldBe(["doc-d-p1"]);
        (await _catalog.GetAsync("doc-d")).ShouldBeNull();
    }

    private static KnowledgeDocument Page(string source, int page) => new()
    {
        Id = $"{source}-p{page}",
        Text = $"page {page} of {source}",
        Metadata = new Dictionary<string, string> { ["source"] = source, ["page"] = $"{page}" },
    };

    /// <summary>A store that searches but can't list.</summary>
    private sealed class SearchOnly(IKnowledgeStore inner) : IKnowledgeStore
    {
        public Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(string query, SearchOptions? options = null, CancellationToken ct = default) =>
            inner.SearchAsync(query, options, ct);

        public Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default) => inner.UpsertAsync(documents, ct);

        public Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default) => inner.DeleteAsync(filter, ct);
    }
}
