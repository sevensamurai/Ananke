using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Catalog;
using Npgsql;
using Shouldly;

namespace Ananke.Postgres.Tests.Containers;

/// <summary>
/// Dense search on tables large enough for the planner to read them through the HNSW index, where pgvector reads a
/// bounded number of candidates and applies the <c>WHERE</c> clause after. The store's table has 20,000 rows over 20
/// tenants; the catalog's has 20,000 entries, of which 1 in 20 is not superseded. At these sizes, and for the limits
/// asked here, the planner takes the HNSW index rather than a sequential or metadata index scan.
/// </summary>
[TestFixture]
public class PostgresHnswSearchTests
{
    private const int Dimensions = 32;
    private const int Rows = 20_000;
    private const int Tenants = 20;
    private const int CatalogEntries = 20_000;
    private const string Query = "a question";

    // The store's columns, for a schema opened on its table directly; the table exists, so they aren't used.
    private const string Columns = "id text PRIMARY KEY";

    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private string _storeTable = null!;
    private string _catalogTable = null!;

    [OneTimeSetUp]
    public async Task FillAsync()
    {
        var dataSource = PostgresDatabase.Require();
        _storeTable = PostgresDatabase.NewTable("hnsw_chunks");
        _catalogTable = PostgresDatabase.NewTable("hnsw_catalog");

        var store = Store();
        foreach (var rows in Enumerable.Range(0, Rows).Chunk(500))
        {
            await store.UpsertAsync([.. rows.Select(i => new KnowledgeDocument
            {
                Id = $"row-{i}",
                Text = $"row {i}",
                Metadata = new Dictionary<string, string> { ["tenant"] = $"t{i % Tenants}" },
            })]);
        }

        var catalog = Catalog();
        await Parallel.ForAsync(0, CatalogEntries, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (i, ct) =>
            await catalog.IndexAsync(new CatalogEntry
            {
                Source = $"doc-{i}.md",
                Summary = $"Summary of doc {i}",
                Keywords = ["doc"],
                Category = "design",
                IndexedAt = Now,
                ChunkCount = 1,
                SupersededBy = i % Tenants == 0 ? null : $"doc-{i + 1}.md",
            }, ct));

        await using var analyze = dataSource.CreateCommand($"ANALYZE {_storeTable}; ANALYZE {_catalogTable}");
        await analyze.ExecuteNonQueryAsync();
    }

    private PostgresKnowledgeStore Store(PostgresKnowledgeOptions? options = null) => new(
        PostgresDatabase.Require(), new HashedEmbedder(Dimensions), (options ?? new PostgresKnowledgeOptions()) with { Table = _storeTable });

    private PostgresKnowledgeCatalog Catalog(PostgresKnowledgeOptions? options = null) => new(
        PostgresDatabase.Require(), new HashedEmbedder(Dimensions), (options ?? new PostgresKnowledgeOptions()) with { Table = _catalogTable });

    [Test]
    public async Task SearchAsync_DenseModeAskingForMoreRowsThanTheIndexReadsByDefault_ReturnsEveryRowAsked()
    {
        var results = await Store().SearchAsync(Query, new SearchOptions { TopK = 200 });

        results.Count.ShouldBe(200);
        results.Select(r => r.Score).ShouldBeInOrder(SortDirection.Descending);
    }

    [Test]
    public async Task SearchAsync_DenseModeWithAFilterKeepingOneRowInTwenty_ReturnsEveryMatchingRowUpToTheLimit()
    {
        var options = new SearchOptions { TopK = 20, Filter = new KnowledgeFilter { ["tenant"] = "t3" } };

        var results = await Store().SearchAsync(Query, options);

        results.Count.ShouldBe(20);
        results.ShouldAllBe(r => r.Metadata["tenant"] == "t3");
        results.Select(r => r.Score).ShouldBeInOrder(SortDirection.Descending);
    }

    [Test]
    public async Task DiscoverAsync_WithMostEntriesSuperseded_ReturnsEveryCurrentEntryUpToTheLimit()
    {
        var results = await Catalog().DiscoverAsync(Query, topK: 20);

        results.Count.ShouldBe(20);
        results.ShouldAllBe(r => r.Entry.SupersededBy == null);
        results.Select(r => r.Score).ShouldBeInOrder(SortDirection.Descending);
    }

    [Test]
    public async Task SearchAsync_IterativeScanOff_ReadsNoMoreThanEfSearchCandidates()
    {
        var options = new SearchOptions { TopK = 20, Filter = new KnowledgeFilter { ["tenant"] = "t3" } };

        var off = await Store(new PostgresKnowledgeOptions { HnswIterativeScan = PostgresIterativeScan.Off }).SearchAsync(Query, options);
        var wider = await Store(new PostgresKnowledgeOptions { HnswIterativeScan = PostgresIterativeScan.Off, HnswEfSearch = 1000 })
            .SearchAsync(Query, options);

        off.Count.ShouldBeLessThan(20);
        wider.Count.ShouldBe(20);
    }

    [Test]
    public async Task HnswSettings_OnPgvectorWithIterativeScan_SetsItAsTheOptionsSay()
    {
        var strict = new PostgresSchema(PostgresDatabase.Require(), new HashedEmbedder(Dimensions), new PostgresKnowledgeOptions { Table = _storeTable });
        var off = new PostgresSchema(PostgresDatabase.Require(), new HashedEmbedder(Dimensions),
            new PostgresKnowledgeOptions { Table = _storeTable, HnswIterativeScan = PostgresIterativeScan.Off });
        await strict.EnsureAsync(Columns, [], default);
        await off.EnsureAsync(Columns, [], default);

        strict.HasIterativeScan.ShouldBeTrue();
        strict.HnswSettings(5).ShouldBe(["SET LOCAL hnsw.ef_search = 40", "SET LOCAL hnsw.iterative_scan = strict_order"]);
        off.HnswSettings(5).ShouldBe(["SET LOCAL hnsw.ef_search = 40", "SET LOCAL hnsw.iterative_scan = off"]);
    }

    [Test]
    public async Task NearestAsync_QueryInTheSettingsBatch_RunsWithTheSettings()
    {
        var schema = new PostgresSchema(PostgresDatabase.Require(), new HashedEmbedder(Dimensions), new PostgresKnowledgeOptions { Table = _storeTable });
        await schema.EnsureAsync(Columns, [], default);

        var settings = await schema.NearestAsync(200,
            new NpgsqlBatchCommand("SELECT current_setting('hnsw.ef_search'), current_setting('hnsw.iterative_scan')"),
            async reader =>
            {
                (await reader.ReadAsync()).ShouldBeTrue();
                return (reader.GetString(0), reader.GetString(1));
            },
            default);

        settings.ShouldBe(("200", "strict_order"));
    }
}
