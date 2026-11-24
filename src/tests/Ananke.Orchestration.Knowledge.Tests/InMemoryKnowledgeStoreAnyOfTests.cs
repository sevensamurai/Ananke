using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

#pragma warning disable ANNKEEXP004 // KnowledgeFilter.AnyOf and listing are experimental

namespace Ananke.Orchestration.Knowledge.Tests;

/// <summary>
/// <see cref="KnowledgeFilter.AnyOf"/> on <see cref="InMemoryKnowledgeStore"/>: a key holding several values matches a
/// document whose value is any of them, in search, listing and delete.
/// </summary>
[TestFixture]
public class InMemoryKnowledgeStoreAnyOfTests
{
    private InMemoryKnowledgeStore _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _store = new InMemoryKnowledgeStore(new InMemoryEmbedder());
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

    [TestCase(SearchMode.Dense)]
    [TestCase(SearchMode.Sparse)]
    [TestCase(SearchMode.Hybrid)]
    public async Task SearchAsync_SeveralValuesForOneKey_MatchesAnyOfThem(SearchMode mode)
    {
        var results = await _store.SearchAsync("chip", new SearchOptions { Mode = mode, TopK = 10, Filter = TwoOutlets() });

        results.Select(r => r.Id).Order().ShouldBe(["techcrunch", "verge"]);
    }

    [Test]
    public async Task ListAsync_SeveralValuesForOneKey_ListsEveryDocumentHoldingOne()
    {
        var listed = await _store.ListAsync(TwoOutlets());

        listed.Select(c => c.Id).ShouldBe(["techcrunch", "verge"]);
    }

    [Test]
    public async Task DeleteAsync_SeveralValuesForOneKey_RemovesEveryDocumentHoldingOne()
    {
        await _store.DeleteAsync(TwoOutlets());

        (await _store.ListAsync(new KnowledgeFilter())).Select(c => c.Id).ShouldBe(["wired"]);
    }

    [Test]
    public async Task ListAsync_SeveralValuesAndAnEntry_MatchesBoth()
    {
        var filter = TwoOutlets();
        filter["kind"] = "news";

        (await _store.ListAsync(filter)).Select(c => c.Id).ShouldBe(["techcrunch"]);
    }

    [Test]
    public async Task ListAsync_AKeyWithNoValues_MatchesNothing()
    {
        var filter = new KnowledgeFilter { AnyOf = { ["outlet"] = [] } };

        (await _store.ListAsync(filter)).ShouldBeEmpty();
        (await _store.SearchAsync("chip", new SearchOptions { Mode = SearchMode.Sparse, Filter = filter })).ShouldBeEmpty();
    }

    [Test]
    public async Task ListAsync_OneValueThroughAnyOf_MatchesAsAnEntryDoes()
    {
        var filter = new KnowledgeFilter { AnyOf = { ["outlet"] = ["Wired"] } };

        (await _store.ListAsync(filter)).Select(c => c.Id).ShouldBe(["wired"]);
    }
}

[TestFixture]
public class KnowledgeFilterCopyTests
{
    [Test]
    public void Copy_holds_entries_and_AnyOf_and_leaves_the_original_alone()
    {
        var original = new KnowledgeFilter { ["kind"] = "news", AnyOf = { ["outlet"] = ["Wired", "The Verge"] } };

        var copy = original.Copy();
        copy.ShouldBe(original);
        copy.AnyOf["outlet"].ShouldBe(["Wired", "The Verge"]);

        copy["kind"] = "review";
        copy["extra"] = "x";
        copy.AnyOf["outlet"] = ["Wired"];
        copy.AnyOf["other"] = ["y"];

        original.Count.ShouldBe(1);
        original["kind"].ShouldBe("news");
        original.AnyOf.Count.ShouldBe(1);
        original.AnyOf["outlet"].ShouldBe(["Wired", "The Verge"]);
    }
}
