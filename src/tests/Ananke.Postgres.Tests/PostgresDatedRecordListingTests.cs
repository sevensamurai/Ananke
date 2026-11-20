using Ananke.Orchestration.Tests;

namespace Ananke.Postgres.Tests;

/// <summary><see cref="DatedRecordListingCases"/> on <see cref="PostgresKnowledgeStore"/>.</summary>
[TestFixture]
public class PostgresDatedRecordListingTests
{
    private PostgresKnowledgeStore? _store;

    [OneTimeSetUp]
    public async Task FillAsync()
    {
        if (PostgresDatabase.DataSource is not { } dataSource)
            return;
        _store = new PostgresKnowledgeStore(
            dataSource,
            new HashedEmbedder(8),
            new PostgresKnowledgeOptions { Table = PostgresDatabase.NewTable("records") });
        await DatedRecordListingCases.FillAsync(_store);
    }

    private PostgresKnowledgeStore Store()
    {
        PostgresDatabase.Require();
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
}
