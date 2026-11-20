using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

#pragma warning disable ANNKEEXP004 // KnowledgeFilter.AnyOf and listing are experimental

namespace Ananke.Orchestration.Knowledge.Tests.Catalog;

/// <summary>
/// <see cref="CatalogAwareKnowledgeStore"/> passes a filter's <see cref="KnowledgeFilter.AnyOf"/> to the store it
/// wraps, and a delete of several sources removes each one's catalog entry.
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
}
