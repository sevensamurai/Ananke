using System.Text;
using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Documents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

#pragma warning disable ANNKEEXP004

namespace Ananke.Orchestration.Knowledge.Tests.Catalog;

/// <summary>
/// Superseding said by the newer document, as RFCs and PEPs say it: a document's chunks carry the sources it replaces
/// (<c>replaces_sources</c>), and the catalog marks those superseded, whichever is indexed first.
/// </summary>
[TestFixture]
public class SupersededByNewerTests
{
    private InMemoryKnowledgeCatalog _catalog = null!;
    private CatalogAwareKnowledgeStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        var embedder = new InMemoryEmbedder();
        _catalog = new InMemoryKnowledgeCatalog(embedder);
        _store = new CatalogAwareKnowledgeStore(new InMemoryKnowledgeStore(embedder), _catalog);
    }

    private Task IndexAsync(string source, string text, Dictionary<string, string>? metadata = null)
    {
        var all = new Dictionary<string, string>(metadata ?? []) { ["source"] = source };
        return _store.UpsertAsync([new KnowledgeDocument { Id = $"{source}:chunk:0", Text = text, Metadata = all }]);
    }

    private static KnowledgeDocument Chunk(string source, string text, Dictionary<string, string>? metadata = null, int index = 0) =>
        new() { Id = $"{source}:chunk:{index}", Text = text, Metadata = new Dictionary<string, string>(metadata ?? []) { ["source"] = source } };

    private static Dictionary<string, string> Replacing(params string[] sources) =>
        new() { ["replaces_sources"] = System.Text.Json.JsonSerializer.Serialize(sources) };

    [Test]
    public async Task UpsertAsync_ANewerDocumentReplacingIndexedOnes_MarksThemSuperseded()
    {
        await IndexAsync("rfc7230", "HTTP/1.1 message syntax");
        await IndexAsync("rfc7231", "HTTP/1.1 semantics");

        await IndexAsync("rfc9110", "HTTP semantics", Replacing("rfc7230", "rfc7231"));

        (await _catalog.GetAsync("rfc7230"))!.SupersededBy.ShouldBe("rfc9110");
        (await _catalog.GetAsync("rfc7231"))!.SupersededBy.ShouldBe("rfc9110");
        (await _catalog.GetAsync("rfc9110"))!.Replaces.ShouldBe(["rfc7230", "rfc7231"]);
        (await _catalog.DiscoverAsync("HTTP", topK: 10)).Select(r => r.Entry.Source).ShouldBe(["rfc9110"]);
    }

    [Test]
    public async Task UpsertAsync_TheReplacedDocumentIndexedAfter_KeepsTheMark()
    {
        await IndexAsync("rfc9110", "HTTP semantics", Replacing("rfc2616"));

        await IndexAsync("rfc2616", "HTTP/1.1");

        var old = (await _catalog.GetAsync("rfc2616"))!;
        old.SupersededBy.ShouldBe("rfc9110");
        old.ChunkCount.ShouldBe(1);
    }

    [Test]
    public async Task UpsertAsync_TheReplacedDocumentIndexedAgain_KeepsTheMark()
    {
        await IndexAsync("pep-0333", "WSGI");
        await IndexAsync("pep-3333", "WSGI for Python 3", Replacing("pep-0333"));

        await IndexAsync("pep-0333", "WSGI, edited");

        (await _catalog.GetAsync("pep-0333"))!.SupersededBy.ShouldBe("pep-3333");
    }

    [Test]
    public async Task DeleteAsync_ThenIndexedAgain_KeepsTheMarkOfANewerDocument()
    {
        // What DocumentProcessor does on every ingestion: the source's chunks are deleted, then written again.
        await IndexAsync("pep-0443", "Single-dispatch generic functions", Replacing("pep-3124"));
        await _store.DeleteAsync(new KnowledgeFilter { ["source"] = "pep-3124" });

        await IndexAsync("pep-3124", "Overloading, generic functions");

        (await _catalog.GetAsync("pep-3124"))!.SupersededBy.ShouldBe("pep-0443");
    }

    [Test]
    public async Task DeleteAsync_TheNewerDocumentGone_DropsItsMarkWhenTheOlderIsIndexedAgain()
    {
        await IndexAsync("rfc2616", "HTTP/1.1");
        await IndexAsync("rfc9110", "HTTP semantics", Replacing("rfc2616"));
        await _store.DeleteAsync(new KnowledgeFilter { ["source"] = "rfc9110" });

        await _store.DeleteAsync(new KnowledgeFilter { ["source"] = "rfc2616" });
        await IndexAsync("rfc2616", "HTTP/1.1");

        (await _catalog.GetAsync("rfc2616"))!.SupersededBy.ShouldBeNull();
    }

    [Test]
    public async Task DeleteAsync_TheNewerDocument_ClearsTheMarkItLeft()
    {
        var store = new CatalogAwareKnowledgeStore(new InMemoryKnowledgeStore(new InMemoryEmbedder()), _catalog)
        {
            SupersededResults = SupersededResults.Exclude,
        };
        await store.UpsertAsync([Chunk("rfc2616", "HTTP/1.1")]);
        await store.UpsertAsync([Chunk("rfc9110", "HTTP semantics", Replacing("rfc2616"))]);

        await store.DeleteAsync(new KnowledgeFilter { ["source"] = "rfc9110" });

        (await _catalog.GetAsync("rfc2616"))!.SupersededBy.ShouldBeNull();
        (await store.SearchAsync("HTTP")).Select(c => c.Metadata["source"]).ShouldBe(["rfc2616"]);
    }

    [Test]
    public async Task DeleteAsync_TheNewerDocument_KeepsTheOlderDocumentsOwnMark()
    {
        await IndexAsync("adr-7", "Use Postgres", new Dictionary<string, string> { ["superseded_by"] = "adr-9" });
        await IndexAsync("adr-9", "Use Postgres and Qdrant", Replacing("adr-7"));

        await _store.DeleteAsync(new KnowledgeFilter { ["source"] = "adr-9" });

        (await _catalog.GetAsync("adr-7"))!.SupersededBy.ShouldBe("adr-9");
    }

    [Test]
    public async Task DeleteAsync_TheNewerDocumentNamedByALaterChunkOfTheOlder_KeepsTheOlderDocumentsOwnMark()
    {
        await _store.UpsertAsync(
        [
            Chunk("adr-7", "Context"),
            Chunk("adr-7", "Use Postgres", new Dictionary<string, string> { ["superseded_by"] = "adr-9" }, index: 1),
        ]);
        await IndexAsync("adr-9", "Use Postgres and Qdrant", Replacing("adr-7"));

        await _store.DeleteAsync(new KnowledgeFilter { ["source"] = "adr-9" });

        (await _catalog.GetAsync("adr-7"))!.SupersededBy.ShouldBe("adr-9");
    }

    [Test]
    public async Task DeleteAsync_TheNewerDocument_RemovesTheEntryThatOnlyCarriedItsMark()
    {
        await IndexAsync("rfc9110", "HTTP semantics", Replacing("rfc2616"));

        await _store.DeleteAsync(new KnowledgeFilter { ["source"] = "rfc9110" });

        (await _catalog.GetAsync("rfc2616")).ShouldBeNull();
    }

    [Test]
    public async Task DeleteAsync_TheNewerDocumentOverAStoreThatCannotList_KeepsTheMark()
    {
        var store = new CatalogAwareKnowledgeStore(new SearchOnly(new InMemoryKnowledgeStore(new InMemoryEmbedder())), _catalog);
        await store.UpsertAsync([Chunk("rfc2616", "HTTP/1.1")]);
        await store.UpsertAsync([Chunk("rfc9110", "HTTP semantics", Replacing("rfc2616"))]);

        await store.DeleteAsync(new KnowledgeFilter { ["source"] = "rfc9110" });

        (await _catalog.GetAsync("rfc2616"))!.SupersededBy.ShouldBe("rfc9110");
    }

    [Test]
    public async Task DeleteAsync_OneOfTwoNewerDocuments_LeavesTheOlderSupersededByTheOther()
    {
        await IndexAsync("rfc2616", "HTTP/1.1");
        await IndexAsync("rfc7230", "HTTP/1.1 message syntax", Replacing("rfc2616"));
        await IndexAsync("rfc7231", "HTTP/1.1 semantics", Replacing("rfc2616"));
        (await _catalog.GetAsync("rfc2616"))!.SupersededBy.ShouldBe("rfc7230");

        await _store.DeleteAsync(new KnowledgeFilter { ["source"] = "rfc7230" });

        (await _catalog.GetAsync("rfc2616"))!.SupersededBy.ShouldBe("rfc7231");
    }

    [Test]
    public async Task DeleteAsync_OneOfTwoNewerDocumentsOfOneNotIndexed_KeepsItsEntrySupersededByTheOther()
    {
        await IndexAsync("rfc7230", "HTTP/1.1 message syntax", Replacing("rfc2616"));
        await IndexAsync("rfc7231", "HTTP/1.1 semantics", Replacing("rfc2616"));

        await _store.DeleteAsync(new KnowledgeFilter { ["source"] = "rfc7230" });

        var old = (await _catalog.GetAsync("rfc2616"))!;
        old.SupersededBy.ShouldBe("rfc7231");
        old.ChunkCount.ShouldBe(0);
    }

    [Test]
    public async Task DeleteAsync_OneOfTwoNewerDocumentsOverACatalogOfOnesOwn_LeavesTheOlderSupersededByTheOther()
    {
        var store = new CatalogAwareKnowledgeStore(new InMemoryKnowledgeStore(new InMemoryEmbedder()), new OwnCatalog(_catalog));
        await store.UpsertAsync([Chunk("rfc2616", "HTTP/1.1")]);
        await store.UpsertAsync([Chunk("rfc7230", "HTTP/1.1 message syntax", Replacing("rfc2616"))]);
        await store.UpsertAsync([Chunk("rfc7231", "HTTP/1.1 semantics", Replacing("rfc2616"))]);

        await store.DeleteAsync(new KnowledgeFilter { ["source"] = "rfc7230" });

        (await _catalog.GetAsync("rfc2616"))!.SupersededBy.ShouldBe("rfc7231");
    }

    [Test]
    public async Task DeleteAsync_ThenIndexedAgainWithoutItsOwnHeader_KeepsTheMarkOfANewerDocumentThatReplacesIt()
    {
        await IndexAsync("adr-7", "Use Postgres", new Dictionary<string, string> { ["superseded_by"] = "adr-8" });
        await IndexAsync("adr-9", "Use Postgres and Qdrant", Replacing("adr-7"));
        await _store.DeleteAsync(new KnowledgeFilter { ["source"] = "adr-7" });

        await IndexAsync("adr-7", "Use Postgres");

        (await _catalog.GetAsync("adr-7"))!.SupersededBy.ShouldBe("adr-9");
    }

    [Test]
    public async Task UpsertAsync_TheNewerDocumentAgainWithFewerSources_ClearsItsMarkOnTheOnesItNoLongerReplaces()
    {
        await IndexAsync("rfc7230", "HTTP/1.1 message syntax");
        await IndexAsync("rfc7231", "HTTP/1.1 semantics");
        await IndexAsync("rfc9110", "HTTP semantics", Replacing("rfc7230", "rfc7231"));

        await IndexAsync("rfc9110", "HTTP semantics, edited", Replacing("rfc7230"));

        (await _catalog.GetAsync("rfc7230"))!.SupersededBy.ShouldBe("rfc9110");
        (await _catalog.GetAsync("rfc7231"))!.SupersededBy.ShouldBeNull();
    }

    [Test]
    public async Task UpsertAsync_TheNewerDocumentAgainWithoutASourceAnotherStillReplaces_LeavesItSupersededByTheOther()
    {
        await IndexAsync("rfc7232", "HTTP/1.1 conditional requests");
        await IndexAsync("rfc9110", "HTTP semantics", Replacing("rfc7232"));
        await IndexAsync("rfc9112", "HTTP/1.1", Replacing("rfc7232"));

        await IndexAsync("rfc9110", "HTTP semantics, edited");

        (await _catalog.GetAsync("rfc7232"))!.SupersededBy.ShouldBe("rfc9112");
    }

    [Test]
    public async Task GetReplacingAsync_InMemory_ReturnsTheEntriesThatNameTheSource()
    {
        await IndexAsync("rfc7230", "HTTP/1.1 message syntax", Replacing("rfc2616"));
        await IndexAsync("rfc7231", "HTTP/1.1 semantics", Replacing("rfc2616", "rfc2617"));
        await IndexAsync("rfc9110", "HTTP semantics", Replacing("rfc7231"));

        (await _catalog.GetReplacingAsync("rfc2616")).Select(e => e.Source).ShouldBe(["rfc7230", "rfc7231"], ignoreOrder: true);
        (await _catalog.GetReplacingAsync("rfc2617")).Select(e => e.Source).ShouldBe(["rfc7231"]);
        (await _catalog.GetReplacingAsync("rfc2618")).ShouldBeEmpty();
        await Should.ThrowAsync<ArgumentException>(() => _catalog.GetReplacingAsync(" "));
    }

    [Test]
    public async Task ProcessAsync_TheReplacedDocumentAfterTheNewer_IsMarkedSuperseded()
    {
        var processor = new DocumentProcessor(new HttpClient(), [new Plain()], new SlidingWindowChunker(), _store)
        {
            Headers = new DocumentHeaderOptions
            {
                Read = d => new Dictionary<string, string> { ["Replaces"] = d.Sections[0].Text.StartsWith("Replaces:") ? "pep-3124" : "" },
                Replaces = f => f["Replaces"] is { Length: > 0 } r ? [r] : null,
            },
        };

        await processor.ProcessAsync(new MemoryStream(Encoding.UTF8.GetBytes("Replaces: 3124\n\nSingle dispatch")), ".txt", "pep-0443");
        await processor.ProcessAsync(new MemoryStream(Encoding.UTF8.GetBytes("Overloading")), ".txt", "pep-3124");

        (await _catalog.GetAsync("pep-3124"))!.SupersededBy.ShouldBe("pep-0443");
        (await _catalog.GetAsync("pep-3124"))!.ChunkCount.ShouldBe(1);
    }

    [Test]
    public async Task UpsertAsync_ADocumentsOwnHeader_StillWins()
    {
        await IndexAsync("old", "old text", new Dictionary<string, string> { ["superseded_by"] = "newest" });
        await IndexAsync("newer", "newer text", Replacing("old"));

        (await _catalog.GetAsync("old"))!.SupersededBy.ShouldBe("newest");
    }

    /// <summary>A catalog as written against 0.9.2: every member but <see cref="IKnowledgeCatalog.GetManyAsync"/> and <see cref="IKnowledgeCatalog.GetReplacingAsync"/>.</summary>
    private sealed class OwnCatalog(InMemoryKnowledgeCatalog inner) : IKnowledgeCatalog
    {
        public Task IndexAsync(CatalogEntry entry, CancellationToken ct = default) => inner.IndexAsync(entry, ct);

        public Task RemoveAsync(string source, CancellationToken ct = default) => inner.RemoveAsync(source, ct);

        public Task<CatalogEntry?> GetAsync(string source, CancellationToken ct = default) => inner.GetAsync(source, ct);

        public Task<IReadOnlyList<CatalogSearchResult>> DiscoverAsync(string query, int topK = 5, CancellationToken ct = default) =>
            inner.DiscoverAsync(query, topK, ct);

        public Task<IReadOnlyList<CatalogEntry>> BrowseAsync(CatalogBrowseOptions? options = null, CancellationToken ct = default) =>
            inner.BrowseAsync(options, ct);
    }

    /// <summary>A store that searches but can't list.</summary>
    private sealed class SearchOnly(IKnowledgeStore inner) : IKnowledgeStore
    {
        public Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(string query, SearchOptions? options = null, CancellationToken ct = default) =>
            inner.SearchAsync(query, options, ct);

        public Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default) => inner.UpsertAsync(documents, ct);

        public Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default) => inner.DeleteAsync(filter, ct);
    }

    /// <summary>The whole text as one section.</summary>
    private sealed class Plain : IDocumentExtractor
    {
        public bool CanExtract(string fileExtension) => true;

        public async Task<ExtractedDocument> ExtractAsync(Stream data, CancellationToken ct = default) =>
            new([new ExtractedSection(await new StreamReader(data).ReadToEndAsync(ct))]);
    }
}
