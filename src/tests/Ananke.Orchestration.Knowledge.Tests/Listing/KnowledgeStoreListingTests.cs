using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

#pragma warning disable ANNKEEXP004

namespace Ananke.Orchestration.Knowledge.Tests.Listing;

/// <summary>
/// <see cref="IListableKnowledgeStore"/> on the in-memory store and through the catalog-aware store: every match of a
/// filter, not the top few a search returns.
/// </summary>
[TestFixture]
public class KnowledgeStoreListingTests
{
    private InMemoryKnowledgeStore _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _store = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        await _store.UpsertAsync([.. Enumerable.Range(0, 30).Select(i => Plan(i, "accepted")), .. Enumerable.Range(30, 5).Select(i => Plan(i, "proposed"))]);
    }

    private static KnowledgeDocument Plan(int i, string status) => new()
    {
        Id = $"plan-{i:D2}",
        Text = $"Plan {i}",
        Metadata = new Dictionary<string, string> { ["source"] = $"plan-{i:D2}.md", ["status"] = status },
    };

    [Test]
    public async Task ListAsync_MoreMatchesThanASearchReturns_ListsEveryOne()
    {
        var listed = await _store.ListAsync(new KnowledgeFilter { ["status"] = "accepted" });

        listed.Count.ShouldBe(30);
        listed.Select(c => c.Id).ShouldBe([.. Enumerable.Range(0, 30).Select(i => $"plan-{i:D2}")]);
        listed.ShouldAllBe(c => c.Score == 0f && c.Metadata["status"] == "accepted");
    }

    [Test]
    public async Task ListAsync_Limit_KeepsTheFirstByIdOrder()
    {
        var listed = await _store.ListAsync(new KnowledgeFilter { ["status"] = "proposed" }, limit: 2);

        listed.Select(c => c.Id).ShouldBe(["plan-30", "plan-31"]);
    }

    [Test]
    public async Task ListAsync_ThroughTheCatalogAwareStore_ListsTheStoreItWraps()
    {
        var store = new CatalogAwareKnowledgeStore(_store, new InMemoryKnowledgeCatalog(new InMemoryEmbedder()));

        (await store.ListAsync(new KnowledgeFilter { ["status"] = "proposed" })).Count.ShouldBe(5);
    }

    [Test]
    public async Task ListAsync_WrappingAStoreThatCantList_Throws()
    {
        var store = new CatalogAwareKnowledgeStore(new SearchOnly(), new InMemoryKnowledgeCatalog(new InMemoryEmbedder()));

        await Should.ThrowAsync<NotSupportedException>(() => store.ListAsync(new KnowledgeFilter()));
    }

    /// <summary>A store that searches but can't list.</summary>
    private sealed class SearchOnly : IKnowledgeStore
    {
        public Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(string query, SearchOptions? options = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<KnowledgeChunk>>([]);

        public Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default) => Task.CompletedTask;
    }
}
