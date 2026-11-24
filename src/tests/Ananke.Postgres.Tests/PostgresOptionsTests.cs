using Npgsql;
using Shouldly;

namespace Ananke.Postgres.Tests;

/// <summary>Checks of <see cref="PostgresKnowledgeStore"/>'s options and server version that need no database.</summary>
[TestFixture]
public class PostgresOptionsTests
{
    private const int Dimensions = 32;

    [Test]
    public void HnswSettings_MoreRowsThanTheLeastEfSearch_RaisesEfSearchToTheRowsAskedUpToOneThousand()
    {
        using var dataSource = NpgsqlDataSource.Create("Host=unused");
        var schema = new PostgresSchema(dataSource, new HashedEmbedder(Dimensions), new PostgresKnowledgeOptions { HnswEfSearch = 100 });

        schema.HnswSettings(5).ShouldBe(["SET LOCAL hnsw.ef_search = 100"]);
        schema.HnswSettings(200).ShouldBe(["SET LOCAL hnsw.ef_search = 200"]);
        schema.HnswSettings(5000).ShouldBe(["SET LOCAL hnsw.ef_search = 1000"]);
    }

    [TestCase(0)]
    [TestCase(1001)]
    public void Constructor_EfSearchOutsidePgvectorsLimits_IsRefused(int efSearch)
    {
        using var dataSource = NpgsqlDataSource.Create("Host=unused");

        Should.Throw<ArgumentOutOfRangeException>(() => new PostgresKnowledgeStore(
            dataSource, new HashedEmbedder(Dimensions), new PostgresKnowledgeOptions { HnswEfSearch = efSearch }));
    }

    [Test]
    public void Constructor_ASchemaAndTableOver52Characters_IsRefusedNamingTheLimit()
    {
        using var dataSource = NpgsqlDataSource.Create("Host=unused");
        var table = "knowledge_base_x." + new string('t', 37);

        var store = Should.Throw<ArgumentException>(() => new PostgresKnowledgeStore(
            dataSource, new HashedEmbedder(Dimensions), new PostgresKnowledgeOptions { Table = table }));
        var catalog = Should.Throw<ArgumentException>(() => new PostgresKnowledgeCatalog(
            dataSource, new HashedEmbedder(Dimensions), new PostgresKnowledgeOptions { Table = table }));

        store.Message.ShouldContain("at most 52 characters");
        catalog.Message.ShouldContain("at most 52 characters");
    }

    [Test]
    public void CheckDateOrder_Postgres15_IsRefusedNamingTheMinimumAndTheServer()
    {
        var refused = Should.Throw<NotSupportedException>(() => PostgresKnowledgeStore.CheckDateOrder(new Version(15, 8)));
        refused.Message.ShouldContain("PostgreSQL 16 or later");
        refused.Message.ShouldContain("PostgreSQL 15.8");
    }

    [Test]
    public void CheckDateOrder_Postgres16_IsAllowed() =>
        Should.NotThrow(() => PostgresKnowledgeStore.CheckDateOrder(new Version(16, 0)));
}
