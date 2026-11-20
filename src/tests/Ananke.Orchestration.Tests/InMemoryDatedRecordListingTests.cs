using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Embeddings;

#pragma warning disable ANNKEEXP004

namespace Ananke.Orchestration.Tests;

/// <summary><see cref="DatedRecordListingCases"/> on <see cref="InMemoryKnowledgeStore"/>.</summary>
[TestFixture]
public class InMemoryDatedRecordListingTests
{
    private InMemoryKnowledgeStore _store = null!;

    [OneTimeSetUp]
    public async Task FillAsync()
    {
        _store = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        await DatedRecordListingCases.FillAsync(_store);
    }

    [Test]
    public Task ListRecords_MoreThanTheLimit_ShowsTheNewestAndSaysTheLimitWasReached() =>
        DatedRecordListingCases.ListRecords_MoreThanTheLimit_ShowsTheNewestAndSaysTheLimitWasReached(_store);

    [Test]
    public Task ListRecords_Since_ListsOnlyTheLaterOnes() =>
        DatedRecordListingCases.ListRecords_Since_ListsOnlyTheLaterOnes(_store);

    [Test]
    public Task ListAsync_ByDate_OrdersAndBoundsInTheStore() =>
        DatedRecordListingCases.ListAsync_ByDate_OrdersAndBoundsInTheStore(_store);
}
