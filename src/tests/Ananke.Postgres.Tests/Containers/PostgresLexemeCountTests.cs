using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge;
using Npgsql;
using Shouldly;

namespace Ananke.Postgres.Tests.Containers;

/// <summary>
/// The lexeme counts <see cref="PostgresKnowledgeStore"/> keeps for keyword search, in <c>&lt;table&gt;_lexemes</c>,
/// against <c>ts_stat</c> over the table after each write.
/// </summary>
[TestFixture]
public class PostgresLexemeCountTests
{
    private static readonly IEmbeddingModel Embedder = new HashedEmbedder(3);

    [Test]
    public async Task Counts_AfterUpsertsAndDeletes_MatchTsStat()
    {
        var table = PostgresDatabase.NewTable("lexemes");
        var store = new PostgresKnowledgeStore(PostgresDatabase.Require(), Embedder, new PostgresKnowledgeOptions { Table = table });

        await store.UpsertAsync([
            Doc("a", "the telemetry backend is SigNoz", "one"),
            Doc("b", "the plan is the plan", "one"),
            Doc("c", "Grafana Cloud was the backend", "two"),
            Doc("d", "", "two"),
        ]);
        await ShouldMatchTsStatAsync(table, rows: 4);

        await store.UpsertAsync([
            Doc("a", "the backend is SigNoz, SigNoz again", "one"),
            Doc("b", "the plan is the plan", "one"),
            Doc("e", "a new row with the plan", "two"),
            Doc("e", "the last text given for an id is kept", "two"),
        ]);
        await ShouldMatchTsStatAsync(table, rows: 5);

        await store.DeleteAsync(new KnowledgeFilter { ["group"] = "two" });
        await ShouldMatchTsStatAsync(table, rows: 2);

        await store.DeleteAsync(new KnowledgeFilter { ["group"] = "one" });
        await ShouldMatchTsStatAsync(table, rows: 0);
    }

    [Test]
    public async Task Counts_ATableMadeWithoutThem_AreFilledOnFirstUse()
    {
        var table = PostgresDatabase.NewTable("lexemes_old");
        var dataSource = PostgresDatabase.Require();
        await ExecuteAsync(dataSource,
            "CREATE EXTENSION IF NOT EXISTS vector; " +
            $"CREATE TABLE {table} (id text PRIMARY KEY, text text NOT NULL, metadata jsonb NOT NULL DEFAULT '{{}}', " +
            "tsv tsvector GENERATED ALWAYS AS (to_tsvector('simple', text)) STORED, embedding vector(3) NOT NULL); " +
            $"INSERT INTO {table} (id, text, embedding) VALUES " +
            "('a', 'the telemetry backend is SigNoz', '[1,0,0]'), ('b', 'the plan is the plan', '[0,1,0]')");
        var store = new PostgresKnowledgeStore(dataSource, Embedder, new PostgresKnowledgeOptions { Table = table });

        var results = await store.SearchAsync("telemetry", new SearchOptions { Mode = SearchMode.Sparse });

        results.Select(r => r.Id).ShouldBe(["a"]);
        await ShouldMatchTsStatAsync(table, rows: 2);

        await store.UpsertAsync([Doc("c", "the plan for telemetry", "one")]);
        await ShouldMatchTsStatAsync(table, rows: 3);
    }

    [Test]
    public async Task Counts_ATableDroppedAndMadeAgain_AreKeptAgain()
    {
        var table = PostgresDatabase.NewTable("lexemes_dropped");
        var dataSource = PostgresDatabase.Require();
        var options = new PostgresKnowledgeOptions { Table = table };
        await new PostgresKnowledgeStore(dataSource, Embedder, options).UpsertAsync([Doc("a", "the plan is the plan", "one")]);
        await ExecuteAsync(dataSource, $"DROP TABLE {table}");

        var store = new PostgresKnowledgeStore(dataSource, Embedder, options);
        await store.UpsertAsync([Doc("b", "the telemetry backend is SigNoz", "one")]);

        await ShouldMatchTsStatAsync(table, rows: 1);
        (await store.SearchAsync("telemetry", new SearchOptions { Mode = SearchMode.Sparse }))
            .Select(r => r.Id).ShouldBe(["b"]);
    }

    [Test]
    public async Task Counts_AfterTruncate_AreEmpty()
    {
        var table = PostgresDatabase.NewTable("lexemes_truncate");
        var store = new PostgresKnowledgeStore(PostgresDatabase.Require(), Embedder, new PostgresKnowledgeOptions { Table = table });
        await store.UpsertAsync([Doc("a", "the plan is the plan", "one"), Doc("b", "the telemetry backend", "two")]);

        await ExecuteAsync(PostgresDatabase.Require(), $"TRUNCATE {table}");
        await ShouldMatchTsStatAsync(table, rows: 0);

        await store.UpsertAsync([Doc("c", "the plan for telemetry", "one")]);
        await ShouldMatchTsStatAsync(table, rows: 1);
    }

    [Test]
    public async Task Counts_UnderConcurrentWrites_MatchTsStat()
    {
        var table = PostgresDatabase.NewTable("lex_concurrent");
        var store = new PostgresKnowledgeStore(PostgresDatabase.Require(), Embedder, new PostgresKnowledgeOptions { Table = table });

        // Eight writers upsert overlapping ids with overlapping words, in different orders, and delete by filter.
        await Parallel.ForAsync(0, 8, async (writer, ct) =>
        {
            for (var round = 0; round < 10; round++)
            {
                await store.UpsertAsync([.. Enumerable.Range(0, 20)
                    .Select(i => (i * (writer + 1) + round) % 30)
                    .Select(i => Doc($"row-{i}", $"the plan {i % 7} is the plan of writer {writer} round {round}", $"w{writer}"))], ct);
                if (round % 4 == 3)
                    await store.DeleteAsync(new KnowledgeFilter { ["group"] = $"w{(writer + 1) % 8}" }, ct);
            }
        }).WaitAsync(TimeSpan.FromMinutes(1));

        var rows = await ReadAsync(PostgresDatabase.Require(), $"SELECT '', count(*), 0::bigint FROM {table}");
        await ShouldMatchTsStatAsync(table, rows: (int)rows[0].Rows);
    }

    private static KnowledgeDocument Doc(string id, string text, string group) => new()
    {
        Id = id,
        Text = text,
        Metadata = new Dictionary<string, string> { ["group"] = group },
    };

    private static async Task ShouldMatchTsStatAsync(string table, int rows)
    {
        var dataSource = PostgresDatabase.Require();
        var kept = await ReadAsync(dataSource, $"SELECT word, ndoc, nentry FROM {table}_lexemes WHERE word <> '' ORDER BY word").ConfigureAwait(false);
        var stat = await ReadAsync(dataSource, $"SELECT word, ndoc::bigint, nentry::bigint FROM ts_stat('SELECT tsv FROM {table}') ORDER BY word").ConfigureAwait(false);
        var totals = await ReadAsync(dataSource, $"SELECT word, ndoc, nentry FROM {table}_lexemes WHERE word = ''").ConfigureAwait(false);

        kept.ShouldBe(stat);
        totals.ShouldBe([("", rows, stat.Sum(s => s.Entries))]);
    }

    private static async Task<List<(string Word, long Rows, long Entries)>> ReadAsync(NpgsqlDataSource dataSource, string sql)
    {
        var rows = new List<(string, long, long)>();
        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
            rows.Add((reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)));
        return rows;
    }

    private static async Task ExecuteAsync(NpgsqlDataSource dataSource, string sql)
    {
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
