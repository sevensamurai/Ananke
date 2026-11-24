using Shouldly;

namespace Ananke.Postgres.Tests.Containers;

/// <summary>
/// Index names of schema-qualified tables at the longest names allowed: each table gets all its indexes, none cut by
/// Postgres to a name another index has.
/// </summary>
[TestFixture]
public class PostgresIndexNameTests
{
    private const int Dimensions = 8;

    // Three characters, so the table may have 49, the most without a schema too: both limits at once.
    private const string Schema = "nnk";

    [OneTimeSetUp]
    public async Task CreateSchemaAsync()
    {
        await using var command = PostgresDatabase.Require().CreateCommand($"CREATE SCHEMA IF NOT EXISTS {Schema}");
        await command.ExecuteNonQueryAsync();
    }

    [Test]
    public async Task Store_SchemaAndTableAtTheLongestAllowed_GetsItsThreeIndexes()
    {
        var table = LongestTable("s");

        await UpsertAsync(table);

        (await IndexesAsync(table)).ShouldBe(
            [$"{Schema}_{table}_embedding", $"{Schema}_{table}_metadata", $"{Schema}_{table}_tsv"], ignoreOrder: true);
    }

    [Test]
    public async Task Store_TwoTablesSharingTheirFirst40Characters_EachGetsItsOwnIndexes()
    {
        var shared = LongestTable("p")[..40];
        var first = shared.PadRight(49, 'a');
        var second = shared.PadRight(49, 'b');

        await UpsertAsync(first);
        await UpsertAsync(second);

        (await IndexesAsync(first)).Count.ShouldBe(3);
        (await IndexesAsync(second)).Count.ShouldBe(3);
    }

    [Test]
    public async Task Catalog_SchemaAndTableAtTheLongestAllowed_GetsItsEmbeddingIndex()
    {
        var table = LongestTable("c");
        var catalog = new PostgresKnowledgeCatalog(
            PostgresDatabase.Require(), new HashedEmbedder(Dimensions), new PostgresKnowledgeOptions { Table = $"{Schema}.{table}" });

        await catalog.GetAsync("0001.md");

        (await IndexesAsync(table)).ShouldContain($"{Schema}_{table}_embedding");
    }

    /// <summary>A table name of its own, as long as <see cref="Schema"/> allows.</summary>
    private static string LongestTable(string prefix) => PostgresDatabase.NewTable(prefix).PadRight(52 - Schema.Length, 'x')[..(52 - Schema.Length)];

    private static async Task UpsertAsync(string table)
    {
        var store = new PostgresKnowledgeStore(
            PostgresDatabase.Require(), new HashedEmbedder(Dimensions), new PostgresKnowledgeOptions { Table = $"{Schema}.{table}" });
        await store.UpsertAsync([new() { Id = "a", Text = "Core.Identity is built on OpenIddict" }]).ConfigureAwait(false);
    }

    private static async Task<List<string>> IndexesAsync(string table)
    {
        await using var command = PostgresDatabase.Require().CreateCommand(
            "SELECT indexname FROM pg_indexes WHERE schemaname = $1 AND tablename = $2 AND indexname NOT LIKE '%_pkey'");
        command.Parameters.Add(new() { Value = Schema });
        command.Parameters.Add(new() { Value = table });
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
            names.Add(reader.GetString(0));
        return names;
    }
}
