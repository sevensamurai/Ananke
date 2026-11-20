using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Orchestration.Tests;
using Shouldly;

namespace Ananke.Qdrant.Tests;

/// <summary><see cref="DatedRecordListingCases"/> on <see cref="QdrantKnowledgeStore"/>.</summary>
[TestFixture]
public class QdrantDatedRecordListingTests
{
    private QdrantKnowledgeStore? _store;

    [OneTimeSetUp]
    public async Task FillAsync()
    {
        if (QdrantServer.Client is not { } client)
            return;
        _store = new QdrantKnowledgeStore(client, new InMemoryEmbedder(), QdrantServer.NewCollection("records"), vectorSize: 64);
        await DatedRecordListingCases.FillAsync(_store);
    }

    private QdrantKnowledgeStore Store()
    {
        QdrantServer.Require();
        return _store!;
    }

    [Test]
    public Task ListRecords_MoreThanTheLimit_ShowsTheNewestAndSaysTheLimitWasReached() =>
        DatedRecordListingCases.ListRecords_MoreThanTheLimit_ShowsTheNewestAndSaysTheLimitWasReached(Store());

    [Test]
    public Task ListRecords_Since_ListsOnlyTheLaterOnes() =>
        DatedRecordListingCases.ListRecords_Since_ListsOnlyTheLaterOnes(Store());

    [Test]
    public Task ListAsync_ByDate_OrdersAndBoundsInTheStore() =>
        DatedRecordListingCases.ListAsync_ByDate_OrdersAndBoundsInTheStore(Store());

    [Test]
    public async Task ListAsync_ByDateWithMorePointsOnOneDateThanAPage_ListsEachOnce()
    {
        var store = new QdrantKnowledgeStore(
            QdrantServer.Require(), new InMemoryEmbedder(), QdrantServer.NewCollection("ties"), vectorSize: 64);
        await store.UpsertAsync([.. Enumerable.Range(0, 600).Select(i => new KnowledgeDocument
        {
            Id = $"n{i}",
            Text = $"Note {i}",
            Metadata = new Dictionary<string, string> { ["effective_at"] = i < 300 ? "2026-11-01T00:00:00+00:00" : "2026-10-01T00:00:00+00:00" },
        })]);

        var listed = await store.ListAsync(new KnowledgeFilter(), limit: 1000, order: new KnowledgeDateOrder("effective_at"));

        listed.Select(c => c.Id).Distinct().Count().ShouldBe(600);
        listed.Take(300).ShouldAllBe(c => c.Metadata["effective_at"].StartsWith("2026-11-01"));
    }
}
