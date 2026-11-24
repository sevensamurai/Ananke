using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

#pragma warning disable ANNKEEXP004 // KnowledgeFilter.AnyOf and listing are experimental

namespace Ananke.Qdrant.Tests.Containers;

/// <summary>
/// <see cref="KnowledgeFilter.AnyOf"/> on <see cref="QdrantKnowledgeStore"/> against Qdrant: a key holding several
/// values matches a point whose value is any of them, in search, listing and delete.
/// </summary>
[TestFixture]
public class QdrantKnowledgeStoreAnyOfTests
{
    private QdrantKnowledgeStore _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _store = new QdrantKnowledgeStore(
            QdrantServer.Require(), new InMemoryEmbedder(), QdrantServer.NewCollection("anyof"), vectorSize: 64);
        await _store.UpsertAsync(
        [
            Article("techcrunch", "TechCrunch", "news"),
            Article("verge", "The Verge", "review"),
            Article("wired", "Wired", "news"),
        ]);
    }

    private static KnowledgeDocument Article(string id, string outlet, string kind) => new()
    {
        Id = id,
        Text = $"{outlet} on the new chip",
        Metadata = new Dictionary<string, string> { ["source"] = $"{id}.html", ["outlet"] = outlet, ["kind"] = kind },
    };

    private static KnowledgeFilter TwoOutlets() => new() { AnyOf = { ["outlet"] = ["TechCrunch", "The Verge"] } };

    private async Task<IEnumerable<string>> ListedSourcesAsync(KnowledgeFilter filter) =>
        (await _store.ListAsync(filter).ConfigureAwait(false)).Select(c => c.Metadata["source"]).Order();

    [Test]
    public async Task SearchAsync_SeveralValuesForOneKey_MatchesAnyOfThem()
    {
        var results = await _store.SearchAsync("chip", new SearchOptions { TopK = 10, Filter = TwoOutlets() });

        results.Select(r => r.Metadata["source"]).Order().ShouldBe(["techcrunch.html", "verge.html"]);
    }

    [Test]
    public async Task SearchAsync_OneValue_MatchesAsBefore()
    {
        var results = await _store.SearchAsync("chip", new SearchOptions { TopK = 10, Filter = new KnowledgeFilter { ["outlet"] = "Wired" } });

        results.Select(r => r.Metadata["source"]).ShouldBe(["wired.html"]);
    }

    [Test]
    public async Task ListAsync_SeveralValuesForOneKey_ListsEveryPointHoldingOne()
    {
        (await ListedSourcesAsync(TwoOutlets())).ShouldBe(["techcrunch.html", "verge.html"]);
    }

    [Test]
    public async Task DeleteAsync_SeveralValuesForOneKey_RemovesEveryPointHoldingOne()
    {
        await _store.DeleteAsync(TwoOutlets());

        (await ListedSourcesAsync(new KnowledgeFilter())).ShouldBe(["wired.html"]);
    }

    [Test]
    public async Task ListAsync_SeveralValuesAndAnEntry_MatchesBoth()
    {
        var filter = TwoOutlets();
        filter["kind"] = "news";

        (await ListedSourcesAsync(filter)).ShouldBe(["techcrunch.html"]);
    }

    [Test]
    public async Task ListAsync_AKeyWithNoValues_MatchesNothing()
    {
        var filter = new KnowledgeFilter { AnyOf = { ["outlet"] = [] } };

        (await ListedSourcesAsync(filter)).ShouldBeEmpty();
        (await _store.SearchAsync("chip", new SearchOptions { TopK = 10, Filter = filter })).ShouldBeEmpty();
    }

    [Test]
    public async Task DeleteAsync_AKeyWithNoValues_RemovesNothing()
    {
        var filter = new KnowledgeFilter { AnyOf = { ["outlet"] = [] } };

        await _store.DeleteAsync(filter);
        (await ListedSourcesAsync(new KnowledgeFilter())).Count().ShouldBe(3);
    }
}
