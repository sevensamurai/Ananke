namespace Ananke.Orchestration.Knowledge.Tests.Search;

/// <summary><see cref="FilteredKeywordSearchCases"/> on <see cref="InMemoryKnowledgeStore"/>.</summary>
[TestFixture]
public class InMemoryFilteredKeywordSearchTests
{
    private InMemoryKnowledgeStore _store = null!;

    [OneTimeSetUp]
    public async Task FillAsync()
    {
        _store = new InMemoryKnowledgeStore(FilteredKeywordSearchCases.Embedder);
        await FilteredKeywordSearchCases.FillAsync(_store);
    }

    [Test]
    public Task SearchAsync_SparseMode_FilterLeavesNoRowWithTheRareTerm_ReturnsRowsWithTheOtherTerms() =>
        FilteredKeywordSearchCases.SearchAsync_SparseMode_FilterLeavesNoRowWithTheRareTerm_ReturnsRowsWithTheOtherTerms(_store);

    [Test]
    public Task SearchAsync_HybridMode_FilterLeavesNoRowWithTheRareTerm_ReturnsRowsWithTheOtherTerms() =>
        FilteredKeywordSearchCases.SearchAsync_HybridMode_FilterLeavesNoRowWithTheRareTerm_ReturnsRowsWithTheOtherTerms(_store);
}
