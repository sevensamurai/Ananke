using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Tests.Search;
using Shouldly;

namespace Ananke.Postgres.Tests.Containers;

/// <summary>
/// <see cref="FilteredKeywordSearchCases"/> on <see cref="PostgresKnowledgeStore"/>, and the rare-term rule kept when
/// there is no filter or the filter leaves a row holding a rare term.
/// </summary>
[TestFixture]
public class PostgresFilteredKeywordSearchTests
{
    private PostgresKnowledgeStore? _store;

    [OneTimeSetUp]
    public async Task FillAsync()
    {
        if (PostgresDatabase.DataSource is not { } dataSource)
            return;
        _store = new PostgresKnowledgeStore(
            dataSource,
            FilteredKeywordSearchCases.Embedder,
            new PostgresKnowledgeOptions { Table = PostgresDatabase.NewTable("filtered") });
        await FilteredKeywordSearchCases.FillAsync(_store);
    }

    private PostgresKnowledgeStore Store()
    {
        PostgresDatabase.Require();
        return _store!;
    }

    [Test]
    public Task SearchAsync_SparseMode_FilterLeavesNoRowWithTheRareTerm_ReturnsRowsWithTheOtherTerms() =>
        FilteredKeywordSearchCases.SearchAsync_SparseMode_FilterLeavesNoRowWithTheRareTerm_ReturnsRowsWithTheOtherTerms(Store());

    [Test]
    public Task SearchAsync_HybridMode_FilterLeavesNoRowWithTheRareTerm_ReturnsRowsWithTheOtherTerms() =>
        FilteredKeywordSearchCases.SearchAsync_HybridMode_FilterLeavesNoRowWithTheRareTerm_ReturnsRowsWithTheOtherTerms(Store());

    [Test]
    public async Task SearchAsync_SparseMode_FilterLeavesNoRowWithTheRareTerm_ReturnsRowsWithEveryTermTheKeptRowsHold()
    {
        // No roadmap.md row holds signoz; two hold the and plan, and are scored, not roadmap-3, which holds only the.
        var results = await Store().SearchAsync(
            "the SigNoz plan", FilteredKeywordSearchCases.Filtered(SearchMode.Sparse, FilteredKeywordSearchCases.Roadmap));

        results.Select(r => r.Id).ShouldBe(["roadmap-1", "roadmap-2"], ignoreOrder: true);
    }

    [Test]
    public async Task SearchAsync_SparseMode_NoRareTermAndFilterKeepsNoRowWithEveryTerm_ReturnsRowsWithEveryTermTheKeptRowsHold()
    {
        // the (5 rows), plan (3) and note (34) are all common; no roadmap.md row holds note, two hold the and plan.
        var results = await Store().SearchAsync(
            "the plan note", FilteredKeywordSearchCases.Filtered(SearchMode.Sparse, FilteredKeywordSearchCases.Roadmap));

        results.Select(r => r.Id).ShouldBe(["roadmap-1", "roadmap-2"], ignoreOrder: true);
    }

    [Test]
    public async Task SearchAsync_SparseMode_NoFilter_ScoresOnlyTheRowsHoldingTheRareTerm()
    {
        var results = await Store().SearchAsync(
            FilteredKeywordSearchCases.Query, new SearchOptions { Mode = SearchMode.Sparse, TopK = 10 });

        results.Select(r => r.Id).ShouldBe(["signoz-1", "signoz-2"], ignoreOrder: true);
    }

    [Test]
    public async Task SearchAsync_SparseMode_FilterKeepsARowWithTheRareTerm_ScoresOnlyThoseRows()
    {
        var results = await Store().SearchAsync(
            FilteredKeywordSearchCases.Query,
            FilteredKeywordSearchCases.Filtered(SearchMode.Sparse, FilteredKeywordSearchCases.Telemetry));

        results.Select(r => r.Id).ShouldBe(["signoz-1", "signoz-2"], ignoreOrder: true);
    }

    [Test]
    public async Task SearchAsync_SparseMode_NoRareTermAndFilterKeepsARowWithEveryTerm_ScoresOnlyThoseRows()
    {
        // the (5 rows) and plan (3 rows) are both common; of telemetry.md's rows only telemetry-plan holds both.
        var results = await Store().SearchAsync(
            "the plan", FilteredKeywordSearchCases.Filtered(SearchMode.Sparse, FilteredKeywordSearchCases.Telemetry));

        results.Select(r => r.Id).ShouldBe(["telemetry-plan"]);
    }
}
