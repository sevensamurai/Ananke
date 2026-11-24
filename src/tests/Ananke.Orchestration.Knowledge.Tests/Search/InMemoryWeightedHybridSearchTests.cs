using Shouldly;

namespace Ananke.Orchestration.Knowledge.Tests.Search;

/// <summary><see cref="WeightedHybridSearchCases"/> on <see cref="InMemoryKnowledgeStore"/>.</summary>
[TestFixture]
public class InMemoryWeightedHybridSearchTests
{
    private InMemoryKnowledgeStore _store = null!;

    [OneTimeSetUp]
    public async Task FillAsync()
    {
        _store = new InMemoryKnowledgeStore(WeightedHybridSearchCases.Embedder);
        await WeightedHybridSearchCases.FillAsync(_store);
    }

    [Test]
    public Task SearchAsync_HybridModeDefaultWeights_HitsAtTheSameRankTie() =>
        WeightedHybridSearchCases.SearchAsync_HybridModeDefaultWeights_HitsAtTheSameRankTie(_store);

    [Test]
    public Task SearchAsync_HybridModeKeywordWeight2_KeywordOnlyHitAboveDenseOnlyHit() =>
        WeightedHybridSearchCases.SearchAsync_HybridModeKeywordWeight2_KeywordOnlyHitAboveDenseOnlyHit(_store);

    [Test]
    public Task SearchAsync_HybridModeBothWeights0_Throws() =>
        WeightedHybridSearchCases.SearchAsync_HybridModeBothWeights0_Throws(_store);

    [Test]
    public Task SearchAsync_DenseModeBothWeights0_ReturnsTheDenseHit() =>
        WeightedHybridSearchCases.SearchAsync_DenseModeBothWeights0_ReturnsTheDenseHit(_store);

    [Test]
    public Task SearchAsync_HybridModeKeywordWeight0_ReturnsOnlyTheDenseHit() =>
        WeightedHybridSearchCases.SearchAsync_HybridModeKeywordWeight0_ReturnsOnlyTheDenseHit(_store);

    [Test]
    public void SearchOptions_NegativeWeight_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new SearchOptions { DenseWeight = -1 });
        Should.Throw<ArgumentOutOfRangeException>(() => new SearchOptions { KeywordWeight = -1 });
    }

    [Test]
    public void SearchOptions_NotFiniteWeight_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new SearchOptions { DenseWeight = float.NaN });
        Should.Throw<ArgumentOutOfRangeException>(() => new SearchOptions { KeywordWeight = float.PositiveInfinity });
    }
}
