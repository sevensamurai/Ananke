using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

#pragma warning disable ANNKEEXP004

namespace Ananke.Orchestration.Knowledge.Tests.Search;

/// <summary><see cref="SearchOptions.MaxPerSource"/>: one document can't fill every result.</summary>
[TestFixture]
public class MaxPerSourceTests
{
    private InMemoryKnowledgeStore _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _store = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        // One long article that matches the query in every chunk, and two that match it once.
        await _store.UpsertAsync([
            .. Enumerable.Range(0, 6).Select(i => Chunk($"verge:{i}", "verge.md", $"FTX trial Sam Bankman-Fried fraud day {i}")),
            Chunk("techcrunch:0", "techcrunch.md", "Sam Bankman-Fried FTX fraud trial verdict"),
            Chunk("cnbc:0", "cnbc.md", "FTX founder Bankman-Fried trial on fraud charges"),
        ]);
    }

    private static KnowledgeDocument Chunk(string id, string source, string text) =>
        new() { Id = id, Text = text, Metadata = new Dictionary<string, string> { ["source"] = source } };

    [TestCase(SearchMode.Sparse)]
    [TestCase(SearchMode.Dense)]
    [TestCase(SearchMode.Hybrid)]
    public async Task SearchAsync_MaxPerSource_LeavesRoomForOtherDocuments(SearchMode mode)
    {
        var results = await _store.SearchAsync("FTX Sam Bankman-Fried fraud trial", new SearchOptions { Mode = mode, TopK = 4, MaxPerSource = 1 });

        results.Select(r => r.Metadata["source"]).ShouldBe(["verge.md", "techcrunch.md", "cnbc.md"], ignoreOrder: true);
    }

    [Test]
    public async Task SearchAsync_MaxPerSource_KeepsTheRankWithinEachDocument()
    {
        var results = await _store.SearchAsync("FTX Sam Bankman-Fried fraud trial", new SearchOptions { Mode = SearchMode.Sparse, TopK = 8, MaxPerSource = 2 });

        results.Count(r => r.Metadata["source"] == "verge.md").ShouldBe(2);
        results.Count.ShouldBe(4);
        results.Select(r => r.Score).ShouldBeInOrder(SortDirection.Descending);
    }

    [Test]
    public async Task SearchAsync_JoinPerSource_GivesEachDocumentOneResultWithItsBestPassages()
    {
        var results = await _store.SearchAsync("FTX Sam Bankman-Fried fraud trial",
            new SearchOptions { Mode = SearchMode.Sparse, TopK = 3, MaxPerSource = 2, JoinPerSource = true });

        results.Select(r => r.Metadata["source"]).ShouldBe(["verge.md", "techcrunch.md", "cnbc.md"], ignoreOrder: true);
        var verge = results.Single(r => r.Metadata["source"] == "verge.md");
        verge.Text.Split(SearchResultLimits.PassageSeparator).Length.ShouldBe(2);
        results.Single(r => r.Metadata["source"] == "cnbc.md").Text.ShouldNotContain(SearchResultLimits.PassageSeparator);
    }

    [Test]
    public async Task SearchAsync_NoMaxPerSource_IsUnchanged()
    {
        var results = await _store.SearchAsync("FTX Sam Bankman-Fried fraud trial", new SearchOptions { Mode = SearchMode.Sparse, TopK = 8 });

        results.Count.ShouldBe(8);
    }
}
