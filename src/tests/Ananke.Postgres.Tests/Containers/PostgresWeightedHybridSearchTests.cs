using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Tests.Search;

namespace Ananke.Postgres.Tests.Containers;

/// <summary><see cref="WeightedHybridSearchCases"/> on <see cref="PostgresKnowledgeStore"/>.</summary>
[TestFixture]
public class PostgresWeightedHybridSearchTests
{
    private PostgresKnowledgeStore? _store;

    [OneTimeSetUp]
    public async Task FillAsync()
    {
        if (PostgresDatabase.DataSource is not { } dataSource)
            return;
        _store = new PostgresKnowledgeStore(
            dataSource,
            WeightedHybridSearchCases.Embedder,
            new PostgresKnowledgeOptions { Table = PostgresDatabase.NewTable("weighted") });
        await WeightedHybridSearchCases.FillAsync(_store);
    }

    private PostgresKnowledgeStore Store()
    {
        PostgresDatabase.Require();
        return _store!;
    }

    [Test]
    public Task SearchAsync_HybridModeDefaultWeights_HitsAtTheSameRankTie() =>
        WeightedHybridSearchCases.SearchAsync_HybridModeDefaultWeights_HitsAtTheSameRankTie(Store());

    [Test]
    public Task SearchAsync_HybridModeKeywordWeight2_KeywordOnlyHitAboveDenseOnlyHit() =>
        WeightedHybridSearchCases.SearchAsync_HybridModeKeywordWeight2_KeywordOnlyHitAboveDenseOnlyHit(Store());

    [Test]
    public Task SearchAsync_HybridModeBothWeights0_Throws() =>
        WeightedHybridSearchCases.SearchAsync_HybridModeBothWeights0_Throws(Store());

    [Test]
    public Task SearchAsync_DenseModeBothWeights0_ReturnsTheDenseHit() =>
        WeightedHybridSearchCases.SearchAsync_DenseModeBothWeights0_ReturnsTheDenseHit(Store());

    [Test]
    public Task SearchAsync_HybridModeKeywordWeight0_ReturnsOnlyTheDenseHit() =>
        WeightedHybridSearchCases.SearchAsync_HybridModeKeywordWeight0_ReturnsOnlyTheDenseHit(Store());
}
