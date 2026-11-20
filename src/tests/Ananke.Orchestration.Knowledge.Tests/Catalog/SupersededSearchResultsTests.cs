using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Documents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

#pragma warning disable ANNKEEXP004

namespace Ananke.Orchestration.Knowledge.Tests.Catalog;

/// <summary>
/// What <see cref="CatalogAwareKnowledgeStore.SearchAsync"/> says about each result's document: its date and what
/// supersedes it, from the catalog when the chunk doesn't carry them.
/// </summary>
[TestFixture]
public class SupersededSearchResultsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);

    private static KnowledgeDocument Chunk(string source, string text, Dictionary<string, string>? metadata = null) =>
        new() { Id = $"{source}:chunk:0", Text = text, Metadata = new Dictionary<string, string>(metadata ?? []) { ["source"] = source } };

    private static Dictionary<string, string> Replacing(params string[] sources) =>
        new() { [DocumentHeaderOptions.ReplacesSourcesKey] = System.Text.Json.JsonSerializer.Serialize(sources) };

    [TestCase(false)]
    [TestCase(true)]
    public async Task SearchAsync_SupersededOnlyByANewerDocument_MarksTheOlderPassage(bool decay)
    {
        var embedder = new InMemoryEmbedder();
        var catalog = new InMemoryKnowledgeCatalog(embedder);
        var store = new CatalogAwareKnowledgeStore(new InMemoryKnowledgeStore(embedder), catalog,
            decayOptions: decay ? new TimeDecayOptions() : null, timeProvider: new FixedTime(Now));
        await store.UpsertAsync([Chunk("rfc7231", "HTTP/1.1 semantics and content")]);
        await store.UpsertAsync([Chunk("rfc9110", "HTTP semantics", Replacing("rfc7231"))]);
        // The date known only to the catalog, as for an entry written before chunks carried it.
        await catalog.IndexAsync((await catalog.GetAsync("rfc7231"))! with { EffectiveAt = new DateTimeOffset(2014, 6, 1, 0, 0, 0, TimeSpan.Zero) });

        var results = await store.SearchAsync("HTTP semantics", new SearchOptions { TopK = 5 });

        var old = results.Single(r => r.Metadata["source"] == "rfc7231");
        old.Metadata.ShouldContainKeyAndValue(DocumentHeaderOptions.SupersededByKey, "rfc9110");
        old.Metadata.ShouldContainKeyAndValue(DocumentHeaderOptions.EffectiveAtKey, "2014-06-01T00:00:00.0000000+00:00");
        results.Single(r => r.Metadata["source"] == "rfc9110").Metadata.ShouldNotContainKey(DocumentHeaderOptions.SupersededByKey);
    }

    [Test]
    public async Task SearchAsync_TheChunkSaysWhatSupersedesIt_KeepsTheChunksOwn()
    {
        var embedder = new InMemoryEmbedder();
        var catalog = new InMemoryKnowledgeCatalog(embedder);
        var store = new CatalogAwareKnowledgeStore(new InMemoryKnowledgeStore(embedder), catalog);
        await store.UpsertAsync([Chunk("adr-7", "Use Postgres", new() { [DocumentHeaderOptions.SupersededByKey] = "ADR-9, ADR-10" })]);
        await catalog.IndexAsync((await catalog.GetAsync("adr-7"))! with { SupersededBy = "adr-9" });

        var results = await store.SearchAsync("Postgres");

        results.Single().Metadata.ShouldContainKeyAndValue(DocumentHeaderOptions.SupersededByKey, "ADR-9, ADR-10");
    }

    /// <summary>
    /// A store over ranked passages of six documents, old-1 to old-3 (each superseded by new-1) ranked above new-1 to
    /// new-3, and a catalog that knows it.
    /// </summary>
    private static async Task<(CatalogAwareKnowledgeStore Store, RankedStore Inner)> RankedAsync(SupersededResults? superseded = null)
    {
        var inner = new RankedStore(["old-1", "old-2", "new-1", "old-3", "new-2", "new-3"]);
        var catalog = new InMemoryKnowledgeCatalog(new InMemoryEmbedder());
        foreach (var source in inner.Sources)
        {
            await catalog.IndexAsync(new CatalogEntry
            {
                Source = source,
                Summary = "",
                Keywords = [],
                IndexedAt = Now,
                ChunkCount = 1,
                SupersededBy = source.StartsWith("old", StringComparison.Ordinal) ? "new-1" : null,
            }).ConfigureAwait(false);
        }
        var store = superseded is { } handling
            ? new CatalogAwareKnowledgeStore(inner, catalog) { SupersededResults = handling }
            : new CatalogAwareKnowledgeStore(inner, catalog);
        return (store, inner);
    }

    [Test]
    public async Task SearchAsync_ByDefault_MarksAndLeavesTheRankingAsItIs()
    {
        var (store, _) = await RankedAsync();

        var results = await store.SearchAsync("q", new SearchOptions { TopK = 4 });

        results.Select(r => r.Metadata["source"]).ShouldBe(["old-1", "old-2", "new-1", "old-3"]);
        results.Select(r => r.Metadata.GetValueOrDefault(DocumentHeaderOptions.SupersededByKey)).ShouldBe(["new-1", "new-1", null, "new-1"]);
    }

    [Test]
    public async Task SearchAsync_Demote_RanksSupersededBelowCurrentInTheirOrder()
    {
        var (store, _) = await RankedAsync(SupersededResults.Demote);

        var results = await store.SearchAsync("q", new SearchOptions { TopK = 4 });

        results.Select(r => r.Metadata["source"]).ShouldBe(["new-1", "old-1", "old-2", "old-3"]);
        results[1].Metadata.ShouldContainKeyAndValue(DocumentHeaderOptions.SupersededByKey, "new-1");
    }

    [Test]
    public async Task SearchAsync_Exclude_LeavesThemOutAndStillReturnsTopK()
    {
        var (store, inner) = await RankedAsync(SupersededResults.Exclude);

        var results = await store.SearchAsync("q", new SearchOptions { TopK = 3 });

        results.Select(r => r.Metadata["source"]).ShouldBe(["new-1", "new-2", "new-3"]);
        inner.Asked.ShouldBe([3, 6]);
    }

    [Test]
    public async Task SearchAsync_ExcludeWithFewerCurrentThanTopK_ReturnsTheCurrentOnes()
    {
        var (store, inner) = await RankedAsync(SupersededResults.Exclude);

        var results = await store.SearchAsync("q", new SearchOptions { TopK = 5 });

        results.Select(r => r.Metadata["source"]).ShouldBe(["new-1", "new-2", "new-3"]);
        inner.Asked.ShouldBe([5, 10]);
    }

    [Test]
    public async Task SearchAsync_ExcludeWithCurrentOnesRankedFarDown_StopsAskingAtSixteenTimesTopK()
    {
        var sources = Enumerable.Range(0, 40).Select(i => $"old-{i}").Append("new-1").ToList();
        var inner = new RankedStore(sources);
        var catalog = new InMemoryKnowledgeCatalog(new InMemoryEmbedder());
        foreach (var source in sources.Where(s => s.StartsWith("old", StringComparison.Ordinal)))
            await catalog.IndexAsync(new CatalogEntry { Source = source, Summary = "", Keywords = [], IndexedAt = Now, ChunkCount = 1, SupersededBy = "new-1" });
        var store = new CatalogAwareKnowledgeStore(inner, catalog) { SupersededResults = SupersededResults.Exclude };

        var results = await store.SearchAsync("q", new SearchOptions { TopK = 2 });

        results.ShouldBeEmpty();
        inner.Asked.ShouldBe([2, 4, 8, 16, 32]);
    }

    [Test]
    public async Task SearchAsync_AChunkWithABlankSource_ComesBackAsItIs()
    {
        var store = new CatalogAwareKnowledgeStore(new RankedStore(["", "new-1"]), new InMemoryKnowledgeCatalog(new InMemoryEmbedder()));

        var results = await store.SearchAsync("q");

        results.Select(r => r.Metadata["source"]).ShouldBe(["", "new-1"]);
    }

    /// <summary>Returns the first TopK of its sources' passages, scored in rank order, and records each TopK asked for.</summary>
    private sealed class RankedStore(IReadOnlyList<string> sources) : IKnowledgeStore
    {
        public IReadOnlyList<string> Sources => sources;

        public List<int> Asked { get; } = [];

        public Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(string query, SearchOptions? options = null, CancellationToken ct = default)
        {
            var topK = (options ?? new SearchOptions()).TopK;
            Asked.Add(topK);
            IReadOnlyList<KnowledgeChunk> results = [.. sources.Take(topK).Select((source, i) => new KnowledgeChunk
            {
                Id = $"{source}:chunk:0",
                Text = source,
                Score = 1f - (i * 0.1f),
                Metadata = new Dictionary<string, string> { ["source"] = source },
            })];
            return Task.FromResult(results);
        }

        public Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
