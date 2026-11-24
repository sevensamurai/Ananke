using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Documents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

#pragma warning disable ANNKEEXP004

namespace Ananke.Orchestration.Knowledge.Tests.Catalog;

/// <summary>
/// <see cref="IKnowledgeCatalog.GetManyAsync"/>, and <see cref="CatalogAwareKnowledgeStore.SearchAsync"/> reading the
/// catalog through it: one call per round of results.
/// </summary>
[TestFixture]
public class CatalogGetManyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 0, 0, 0, TimeSpan.Zero);

    private static readonly string[] Sources = ["doc-1", "doc-2", "doc-3", "doc-4", "doc-5"];

    private static CatalogEntry Entry(string source, string? supersededBy = null, DateTimeOffset? effectiveAt = null) => new()
    {
        Source = source,
        Summary = "",
        Keywords = [],
        IndexedAt = Now,
        EffectiveAt = effectiveAt,
        ChunkCount = 1,
        SupersededBy = supersededBy,
    };

    [Test]
    public async Task GetManyAsync_ACatalogWithOnlyTheOtherMembers_GetsEachSourceOnceAndLeavesOutTheMissing()
    {
        var inner = new InMemoryKnowledgeCatalog(new InMemoryEmbedder());
        await inner.IndexAsync(Entry("doc-1"));
        await inner.IndexAsync(Entry("doc-2"));
        var catalog = new GetOnlyCatalog(inner);

        var entries = await ((IKnowledgeCatalog)catalog).GetManyAsync(["doc-1", "missing", "doc-2", "doc-1"]);

        entries.Select(e => e.Source).ShouldBe(["doc-1", "doc-2"], ignoreOrder: true);
        catalog.Gets.ShouldBe(["doc-1", "missing", "doc-2"]);
    }

    [Test]
    public async Task GetManyAsync_InMemory_ReturnsTheEntriesFoundAndLeavesOutTheMissing()
    {
        var catalog = new InMemoryKnowledgeCatalog(new InMemoryEmbedder());
        await catalog.IndexAsync(Entry("doc-1"));
        await catalog.IndexAsync(Entry("doc-2"));
        await catalog.IndexAsync(Entry("doc-3"));

        var entries = await catalog.GetManyAsync(["doc-3", "missing", "doc-1", "doc-3"]);

        entries.Select(e => e.Source).ShouldBe(["doc-1", "doc-3"], ignoreOrder: true);
        (await catalog.GetManyAsync([])).ShouldBeEmpty();
        await Should.ThrowAsync<ArgumentException>(() => catalog.GetManyAsync(["doc-1", " "]));
    }

    /// <summary>A store over one passage of each of five documents, doc-2 superseded by doc-5 and doc-3 dated.</summary>
    private static async Task<CatalogAwareKnowledgeStore> StoreAsync(IKnowledgeCatalog catalog, InMemoryKnowledgeCatalog entries)
    {
        foreach (var source in Sources)
        {
            await entries.IndexAsync(Entry(source,
                supersededBy: source == "doc-2" ? "doc-5" : null,
                effectiveAt: source == "doc-3" ? new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero) : null)).ConfigureAwait(false);
        }
        return new CatalogAwareKnowledgeStore(await InnerAsync().ConfigureAwait(false), catalog, timeProvider: new FixedTime(Now));
    }

    [Test]
    public async Task SearchAsync_ResultsFromFiveSources_ReadsTheCatalogOnceWithTheSameResultsAndMarks()
    {
        var counted = new InMemoryKnowledgeCatalog(new InMemoryEmbedder());
        var counting = new CountingCatalog(counted);
        var store = await StoreAsync(counting, counted);
        var oneByOne = new InMemoryKnowledgeCatalog(new InMemoryEmbedder());
        var before = await StoreAsync(new GetOnlyCatalog(oneByOne), oneByOne);

        var results = await store.SearchAsync("release notes", new SearchOptions { TopK = 10 });
        var expected = await before.SearchAsync("release notes", new SearchOptions { TopK = 10 });

        counting.GetManyCalls.ShouldBe(1);
        counting.Gets.ShouldBeEmpty();
        results.Select(r => r.Metadata["source"]).ShouldBe(Sources, ignoreOrder: true);
        results.Select(r => (r.Id, r.Score, Metadata: string.Join(";", r.Metadata.OrderBy(m => m.Key).Select(m => $"{m.Key}={m.Value}"))))
            .ShouldBe(expected.Select(r => (r.Id, r.Score, Metadata: string.Join(";", r.Metadata.OrderBy(m => m.Key).Select(m => $"{m.Key}={m.Value}")))));
        results.Single(r => r.Metadata["source"] == "doc-2").Metadata.ShouldContainKeyAndValue(DocumentHeaderOptions.SupersededByKey, "doc-5");
        results.Single(r => r.Metadata["source"] == "doc-3").Metadata.ShouldContainKey(DocumentHeaderOptions.EffectiveAtKey);
    }

    [Test]
    public async Task SearchAsync_ExcludeAskingTwice_ReadsTheCatalogOncePerRound()
    {
        var counted = new InMemoryKnowledgeCatalog(new InMemoryEmbedder());
        var counting = new CountingCatalog(counted);
        foreach (var source in Sources)
            await counted.IndexAsync(Entry(source, supersededBy: source is "doc-1" or "doc-2" or "doc-3" ? "doc-5" : null));
        var store = new CatalogAwareKnowledgeStore(await InnerAsync(), counting) { SupersededResults = SupersededResults.Exclude };

        var results = await store.SearchAsync("release notes", new SearchOptions { TopK = 3 });

        results.Count.ShouldBe(2);
        counting.GetManyCalls.ShouldBe(2);
        counting.Gets.ShouldBeEmpty();
    }

    private static async Task<InMemoryKnowledgeStore> InnerAsync()
    {
        var inner = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        await inner.UpsertAsync([.. Sources.Select(s => new KnowledgeDocument
        {
            Id = $"{s}:chunk:0",
            Text = $"release notes {s}",
            Metadata = new Dictionary<string, string> { ["source"] = s },
        })]).ConfigureAwait(false);
        return inner;
    }

    /// <summary>A catalog as written against 0.9.2: every member but <see cref="IKnowledgeCatalog.GetManyAsync"/>.</summary>
    private class GetOnlyCatalog(InMemoryKnowledgeCatalog inner) : IKnowledgeCatalog
    {
        protected InMemoryKnowledgeCatalog Inner => inner;

        public List<string> Gets { get; } = [];

        public Task IndexAsync(CatalogEntry entry, CancellationToken ct = default) => inner.IndexAsync(entry, ct);

        public Task RemoveAsync(string source, CancellationToken ct = default) => inner.RemoveAsync(source, ct);

        public Task<CatalogEntry?> GetAsync(string source, CancellationToken ct = default)
        {
            Gets.Add(source);
            return inner.GetAsync(source, ct);
        }

        public Task<IReadOnlyList<CatalogSearchResult>> DiscoverAsync(string query, int topK = 5, CancellationToken ct = default) =>
            inner.DiscoverAsync(query, topK, ct);

        public Task<IReadOnlyList<CatalogEntry>> BrowseAsync(CatalogBrowseOptions? options = null, CancellationToken ct = default) =>
            inner.BrowseAsync(options, ct);
    }

    /// <summary>A catalog that answers <see cref="IKnowledgeCatalog.GetManyAsync"/> itself and counts the calls.</summary>
    private sealed class CountingCatalog(InMemoryKnowledgeCatalog inner) : GetOnlyCatalog(inner), IKnowledgeCatalog
    {
        public int GetManyCalls { get; private set; }

        public Task<IReadOnlyList<CatalogEntry>> GetManyAsync(IEnumerable<string> sources, CancellationToken ct = default)
        {
            GetManyCalls++;
            return Inner.GetManyAsync(sources, ct);
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
