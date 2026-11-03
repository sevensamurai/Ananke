using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

namespace Ananke.Orchestration.Knowledge.Tests.Catalog;

[TestFixture]
public class CatalogAwareKnowledgeStoreTests
{
    private InMemoryKnowledgeStore _inner = null!;
    private InMemoryKnowledgeCatalog _catalog = null!;
    private CatalogAwareKnowledgeStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        var embedder = new InMemoryEmbedder();
        _inner = new InMemoryKnowledgeStore(embedder);
        _catalog = new InMemoryKnowledgeCatalog(embedder);
        _store = new CatalogAwareKnowledgeStore(_inner, _catalog);
    }

    [Test]
    public void Constructor_NullInner_Throws() =>
        Should.Throw<ArgumentNullException>(() =>
            new CatalogAwareKnowledgeStore(null!, _catalog));

    [Test]
    public void Constructor_NullCatalog_Throws() =>
        Should.Throw<ArgumentNullException>(() =>
            new CatalogAwareKnowledgeStore(_inner, null!));

    [Test]
    public async Task UpsertAsync_DocumentsWithSource_CreateOneCatalogEntryPerSource()
    {
        await _store.UpsertAsync(
        [
            new KnowledgeDocument { Id = "a1", Text = "alpha one", Metadata = new Dictionary<string, string> { ["source"] = "doc-a" } },
            new KnowledgeDocument { Id = "a2", Text = "alpha two", Metadata = new Dictionary<string, string> { ["source"] = "doc-a" } },
            new KnowledgeDocument { Id = "b1", Text = "beta one", Metadata = new Dictionary<string, string> { ["source"] = "doc-b" } }
        ]);

        _catalog.Count.ShouldBe(2);
        var entryA = await _catalog.GetAsync("doc-a");
        entryA.ShouldNotBeNull();
        entryA.ChunkCount.ShouldBe(2);
        var entryB = await _catalog.GetAsync("doc-b");
        entryB.ShouldNotBeNull();
        entryB.ChunkCount.ShouldBe(1);
    }

    [Test]
    public async Task UpsertAsync_DocumentsWithoutSourceMetadata_AreNotCataloged()
    {
        await _store.UpsertAsync(
        [
            new KnowledgeDocument { Id = "x1", Text = "no source here" }
        ]);

        _catalog.Count.ShouldBe(0);
        // The inner store still receives the document — only cataloging is skipped.
        _inner.Count.ShouldBe(1);
    }

    [Test]
    public async Task UpsertAsync_EmptyDocumentList_DoesNothing()
    {
        await _store.UpsertAsync([]);

        _catalog.Count.ShouldBe(0);
        _inner.Count.ShouldBe(0);
    }

    [Test]
    public async Task UpsertAsync_NoExtractorConfigured_CatalogEntryHasEmptyEnrichment()
    {
        await _store.UpsertAsync(
        [
            new KnowledgeDocument { Id = "a1", Text = "text", Metadata = new Dictionary<string, string> { ["source"] = "doc-a" } }
        ]);

        var entry = await _catalog.GetAsync("doc-a");
        entry.ShouldNotBeNull();
        entry.Summary.ShouldBe(string.Empty);
        entry.Keywords.ShouldBeEmpty();
        entry.Category.ShouldBe(string.Empty);
    }

    [Test]
    public async Task DeleteAsync_WithSourceFilter_RemovesBothChunksAndCatalogEntry()
    {
        await _store.UpsertAsync(
        [
            new KnowledgeDocument { Id = "a1", Text = "alpha", Metadata = new Dictionary<string, string> { ["source"] = "doc-a" } }
        ]);
        (await _catalog.GetAsync("doc-a")).ShouldNotBeNull();

        await _store.DeleteAsync(new KnowledgeFilter { ["source"] = "doc-a" });

        (await _catalog.GetAsync("doc-a")).ShouldBeNull();
        _inner.Count.ShouldBe(0);
    }

    [Test]
    public async Task DeleteAsync_WithoutSourceFilter_LeavesCatalogUntouched()
    {
        await _store.UpsertAsync(
        [
            new KnowledgeDocument { Id = "a1", Text = "alpha", Metadata = new Dictionary<string, string> { ["source"] = "doc-a", ["tag"] = "keep-me" } }
        ]);

        await _store.DeleteAsync(new KnowledgeFilter { ["tag"] = "some-other-value" });

        // Nothing matched the filter, so nothing was deleted from either the chunk store or catalog.
        (await _catalog.GetAsync("doc-a")).ShouldNotBeNull();
        _inner.Count.ShouldBe(1);
    }

    [Test]
    public async Task SearchAsync_NoDecayOptions_ReturnsInnerResultsUnchanged()
    {
        await _store.UpsertAsync(
        [
            new KnowledgeDocument { Id = "a1", Text = "alpha search text", Metadata = new Dictionary<string, string> { ["source"] = "doc-a" } }
        ]);

        var results = await _store.SearchAsync("alpha search text");

        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe("a1");
    }

    [Test]
    public async Task SearchAsync_WithDecayOptions_ButNoMatchingCatalogEntry_LeavesScoreUnchanged()
    {
        // Upsert directly through the inner store, bypassing cataloging entirely.
        await _inner.UpsertAsync(
        [
            new KnowledgeDocument { Id = "a1", Text = "alpha search text" }
        ]);
        var decayed = new CatalogAwareKnowledgeStore(_inner, _catalog, decayOptions: new TimeDecayOptions());

        var results = await decayed.SearchAsync("alpha search text");

        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe("a1");
    }

    [Test]
    public async Task SearchAsync_WithDecayOptions_OldDocumentScoresLowerThanFreshOne()
    {
        var embedder = new InMemoryEmbedder();
        var inner = new InMemoryKnowledgeStore(embedder);
        var catalog = new InMemoryKnowledgeCatalog(embedder);
        var decayed = new CatalogAwareKnowledgeStore(inner, catalog,
            decayOptions: new TimeDecayOptions { HalfLifeDays = 1, FloorWeight = 0f });

        // Two near-identical texts so their raw similarity to the query is close; the catalog
        // timestamps are what should separate them once decay is applied.
        await inner.UpsertAsync(
        [
            new KnowledgeDocument { Id = "old", Text = "shared search topic old", Metadata = new Dictionary<string, string> { ["source"] = "old-doc" } },
            new KnowledgeDocument { Id = "fresh", Text = "shared search topic fresh", Metadata = new Dictionary<string, string> { ["source"] = "fresh-doc" } }
        ]);
        await catalog.IndexAsync(new CatalogEntry
        {
            Source = "old-doc",
            Summary = "",
            Keywords = [],
            IndexedAt = DateTimeOffset.UtcNow.AddDays(-365),
            ChunkCount = 1
        });
        await catalog.IndexAsync(new CatalogEntry
        {
            Source = "fresh-doc",
            Summary = "",
            Keywords = [],
            IndexedAt = DateTimeOffset.UtcNow,
            ChunkCount = 1
        });

        var results = await decayed.SearchAsync("shared search topic", new SearchOptions { TopK = 2 });

        results.Count.ShouldBe(2);
        var oldResult = results.Single(r => r.Id == "old");
        var freshResult = results.Single(r => r.Id == "fresh");
        freshResult.Score.ShouldBeGreaterThan(oldResult.Score);
    }

    private static readonly DateTimeOffset ClockNow = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task UpsertAsync_StampsTheCatalogEntryFromTheClock()
    {
        var store = new CatalogAwareKnowledgeStore(_inner, _catalog, timeProvider: new FixedClock(ClockNow));

        await store.UpsertAsync(
        [
            new KnowledgeDocument { Id = "a1", Text = "alpha one", Metadata = new Dictionary<string, string> { ["source"] = "doc-a" } }
        ]);

        (await _catalog.GetAsync("doc-a"))!.IndexedAt.ShouldBe(ClockNow);
    }

    [Test]
    public async Task SearchAsync_WithDecayOptions_MeasuresAgeByTheClock()
    {
        await _inner.UpsertAsync(
        [
            new KnowledgeDocument { Id = "doc", Text = "shared search topic", Metadata = new Dictionary<string, string> { ["source"] = "doc" } }
        ]);
        await _catalog.IndexAsync(new CatalogEntry
        {
            Source = "doc",
            Summary = "",
            Keywords = [],
            IndexedAt = ClockNow,
            ChunkCount = 1
        });
        var raw = (await _inner.SearchAsync("shared search topic")).Single().Score;
        var decayed = new CatalogAwareKnowledgeStore(_inner, _catalog,
            decayOptions: new TimeDecayOptions { HalfLifeDays = 1, FloorWeight = 0f },
            timeProvider: new FixedClock(ClockNow.AddDays(1)));

        var result = (await decayed.SearchAsync("shared search topic")).Single();

        result.Score.ShouldBe(raw / 2, tolerance: 0.001f);
    }

    [Test]
    public async Task UpsertAsync_ChunksCarryTheDocumentsDate_TheEntryKeepsItAsEffectiveAt()
    {
        var store = new CatalogAwareKnowledgeStore(_inner, _catalog, timeProvider: new FixedClock(ClockNow));

        await store.UpsertAsync(
        [
            new KnowledgeDocument
            {
                Id = "a1",
                Text = "alpha one",
                Metadata = new Dictionary<string, string> { ["source"] = "doc-a", ["effective_at"] = "2026-10-30T00:00:00.0000000+00:00" },
            }
        ]);

        var entry = (await _catalog.GetAsync("doc-a"))!;
        entry.EffectiveAt.ShouldBe(new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero));
        entry.IndexedAt.ShouldBe(ClockNow);
    }

    [Test]
    public async Task UpsertAsync_ADateThatDoesntParse_LeavesEffectiveAtEmpty()
    {
        await _store.UpsertAsync(
        [
            new KnowledgeDocument { Id = "a1", Text = "alpha", Metadata = new Dictionary<string, string> { ["source"] = "doc-a", ["effective_at"] = "soon" } }
        ]);

        (await _catalog.GetAsync("doc-a"))!.EffectiveAt.ShouldBeNull();
    }

    [Test]
    public async Task SearchAsync_WithDecayOptions_AgesADocumentFromItsOwnDate()
    {
        await _inner.UpsertAsync(
        [
            new KnowledgeDocument { Id = "doc", Text = "shared search topic", Metadata = new Dictionary<string, string> { ["source"] = "doc" } }
        ]);
        // Indexed just now, but written a day before: decay counts the day.
        await _catalog.IndexAsync(new CatalogEntry
        {
            Source = "doc",
            Summary = "",
            Keywords = [],
            IndexedAt = ClockNow,
            EffectiveAt = ClockNow.AddDays(-1),
            ChunkCount = 1
        });
        var raw = (await _inner.SearchAsync("shared search topic")).Single().Score;
        var decayed = new CatalogAwareKnowledgeStore(_inner, _catalog,
            decayOptions: new TimeDecayOptions { HalfLifeDays = 1, FloorWeight = 0f },
            timeProvider: new FixedClock(ClockNow));

        var result = (await decayed.SearchAsync("shared search topic")).Single();

        result.Score.ShouldBe(raw / 2, tolerance: 0.001f);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
