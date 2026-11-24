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

    [Test]
    public Task ListAsync_DateWithoutAnOffset_IsReadAsUtc() =>
        DatedRecordListingCases.ListAsync_DateWithoutAnOffset_IsReadAsUtc(new InMemoryKnowledgeStore(new InMemoryEmbedder()));

    [Test]
    public Task ListAsync_ValuesThatArentIsoDates_AreNoDate() =>
        DatedRecordListingCases.ListAsync_ValuesThatArentIsoDates_AreNoDate(new InMemoryKnowledgeStore(new InMemoryEmbedder()));

    [Test]
    public Task ListAsync_OffsetsAndDatesOutOfRange_AreNoDate() =>
        DatedRecordListingCases.ListAsync_OffsetsAndDatesOutOfRange_AreNoDate(new InMemoryKnowledgeStore(new InMemoryEmbedder()));
}
