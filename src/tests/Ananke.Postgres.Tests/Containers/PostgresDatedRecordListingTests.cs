using Ananke.Orchestration.Tests;
using Npgsql;

namespace Ananke.Postgres.Tests.Containers;

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

    [Test]
    public async Task ListAsync_DateWithoutAnOffset_IsReadAsUtc()
    {
        PostgresDatabase.Require();
        var connection = new NpgsqlConnectionStringBuilder(PostgresDatabase.ConnectionString) { Timezone = "Asia/Tokyo" };
        await using var aheadOfUtc = NpgsqlDataSource.Create(connection);
        await DatedRecordListingCases.ListAsync_DateWithoutAnOffset_IsReadAsUtc(new PostgresKnowledgeStore(
            aheadOfUtc, new HashedEmbedder(8), new PostgresKnowledgeOptions { Table = PostgresDatabase.NewTable("no_offset") }));
    }

    [Test]
    public Task ListAsync_ValuesThatArentIsoDates_AreNoDate() =>
        DatedRecordListingCases.ListAsync_ValuesThatArentIsoDates_AreNoDate(new PostgresKnowledgeStore(
            PostgresDatabase.Require(), new HashedEmbedder(8), new PostgresKnowledgeOptions { Table = PostgresDatabase.NewTable("not_dates") }));

    [Test]
    public Task ListAsync_OffsetsAndDatesOutOfRange_AreNoDate() =>
        DatedRecordListingCases.ListAsync_OffsetsAndDatesOutOfRange_AreNoDate(new PostgresKnowledgeStore(
            PostgresDatabase.Require(), new HashedEmbedder(8), new PostgresKnowledgeOptions { Table = PostgresDatabase.NewTable("out_of_range") }));
}
