using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge;
using Shouldly;

#pragma warning disable ANNKEEXP004 // KnowledgeFilter.AnyOf is experimental

namespace Ananke.Postgres.Tests.Containers;

/// <summary>
/// <see cref="KnowledgeFilter.AnyOf"/> on <see cref="PostgresKnowledgeStore"/>: a key holding several values matches a
/// row whose value is any of them, in search, listing and delete.
/// </summary>
[TestFixture]
public class PostgresKnowledgeStoreAnyOfTests
{
    private static readonly KnowledgeDocument[] Articles =
    [
        Article("techcrunch", "TechCrunch", "news"),
        Article("verge", "The Verge", "review"),
        Article("wired", "Wired", "news"),
    ];

    private static readonly Dictionary<string, float[]> Vectors = new()
    {
        ["chip"] = [1f, 1f, 1f],
        [Articles[0].Text] = [1f, 0f, 0f],
        [Articles[1].Text] = [0f, 1f, 0f],
        [Articles[2].Text] = [0f, 0f, 1f],
    };

    private PostgresKnowledgeStore _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _store = new PostgresKnowledgeStore(
            PostgresDatabase.Require(),
            new FixedEmbedder(Vectors, new EmbeddingModelIdentity("test", "fixed", 3)),
            new PostgresKnowledgeOptions { Table = PostgresDatabase.NewTable("anyof") });
        await _store.UpsertAsync(Articles);
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
    public async Task ListAsync_SeveralValuesForOneKey_ListsEveryRowHoldingOne()
    {
        (await _store.ListAsync(TwoOutlets())).Select(r => r.Id).ShouldBe(["techcrunch", "verge"]);
    }

    [Test]
    public async Task DeleteAsync_SeveralValuesForOneKey_RemovesEveryRowHoldingOne()
    {
        await _store.DeleteAsync(TwoOutlets());

        (await _store.ListAsync(new KnowledgeFilter())).Select(r => r.Id).ShouldBe(["wired"]);
    }

    [Test]
    public async Task ListAsync_SeveralValuesAndAnEntry_MatchesBoth()
    {
        var filter = TwoOutlets();
        filter["kind"] = "news";

        (await _store.ListAsync(filter)).Select(r => r.Id).ShouldBe(["techcrunch"]);
    }

    [Test]
    public async Task ListAsync_SeveralValuesForTwoKeys_MatchesBoth()
    {
        var filter = TwoOutlets();
        filter.AnyOf["kind"] = ["review", "opinion"];

        (await _store.ListAsync(filter)).Select(r => r.Id).ShouldBe(["verge"]);
    }

    [Test]
    public async Task ListAsync_AKeyWithNoValues_MatchesNothing()
    {
        var filter = new KnowledgeFilter { AnyOf = { ["outlet"] = [] } };

        (await _store.ListAsync(filter)).ShouldBeEmpty();
    }
}
