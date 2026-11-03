using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Ananke.Abstractions.Agents;
using Npgsql;

namespace Ananke.Postgres;

/// <summary>
/// Creates a knowledge table on first use and records which embedder wrote it, in <c>ananke_embedders</c>. A table
/// opened with a different embedder, or with vectors of another length, is refused.
/// </summary>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
internal sealed partial class PostgresSchema(NpgsqlDataSource dataSource, IEmbeddingModel embedder, PostgresKnowledgeOptions options)
{
    private const string EmbeddersTable = "ananke_embedders";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _ready;

    public string Table { get; } = Checked(options.Table, nameof(options.Table));

    public string TextSearchConfiguration { get; } = Checked(options.TextSearchConfiguration, nameof(options.TextSearchConfiguration));

    public int Dimensions { get; private set; }

    /// <summary>Creates the table, its indexes and its stamp once; later calls return at once.</summary>
    /// <param name="columns">The table's columns other than <c>embedding</c>, as SQL.</param>
    /// <param name="indexes">Its indexes other than the embedding's, as SQL; <c>{0}</c> is the table.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task EnsureAsync(string columns, IReadOnlyList<string> indexes, CancellationToken ct)
    {
        if (_ready)
            return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_ready)
                return;

            Dimensions = options.Dimensions
                ?? embedder.Identity?.Dimensions
                ?? (await embedder.EmbedAsync("dimensions", ct).ConfigureAwait(false)).Length;

            await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

            // One creator at a time, across processes, so two stores starting together don't race on DDL.
            await ExecuteAsync(connection, "SELECT pg_advisory_xact_lock(hashtext('ananke_knowledge_schema'))", ct).ConfigureAwait(false);
            await ExecuteAsync(connection, "CREATE EXTENSION IF NOT EXISTS vector", ct).ConfigureAwait(false);
            await ExecuteAsync(connection,
                $"CREATE TABLE IF NOT EXISTS {EmbeddersTable} (table_name text PRIMARY KEY, embedder text, dimensions integer NOT NULL)", ct).ConfigureAwait(false);
            await CheckStampAsync(connection, ct).ConfigureAwait(false);
            await ExecuteAsync(connection,
                $"CREATE TABLE IF NOT EXISTS {Table} ({columns}, embedding vector({Dimensions.ToString(CultureInfo.InvariantCulture)}) NOT NULL)", ct).ConfigureAwait(false);
            await ExecuteAsync(connection,
                $"CREATE INDEX IF NOT EXISTS {IndexName("embedding")} ON {Table} USING hnsw (embedding vector_cosine_ops)", ct).ConfigureAwait(false);
            foreach (var index in indexes)
                await ExecuteAsync(connection, string.Format(CultureInfo.InvariantCulture, index, Table), ct).ConfigureAwait(false);

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            _ready = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>An index name for this table: its name with dots made underscores, and a suffix.</summary>
    public string IndexName(string suffix) => $"{Table.Replace('.', '_')}_{suffix}";

    /// <summary>A vector as pgvector's text form, <c>[0.1,0.2]</c>, cast with <c>::vector</c> in SQL.</summary>
    public static string VectorText(ReadOnlyMemory<float> vector) =>
        "[" + string.Join(",", vector.ToArray().Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "]";

    private async Task CheckStampAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        var identity = embedder.Identity?.ToString();
        await using var read = new NpgsqlCommand($"SELECT embedder, dimensions FROM {EmbeddersTable} WHERE table_name = @table", connection);
        read.Parameters.AddWithValue("table", Table);
        await using (var reader = await read.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var stamped = await reader.IsDBNullAsync(0, ct).ConfigureAwait(false) ? null : reader.GetString(0);
                var dimensions = reader.GetInt32(1);
                if (dimensions != Dimensions || (stamped is not null && identity is not null && stamped != identity))
                {
                    throw new InvalidOperationException(
                        $"Table '{Table}' holds vectors from {stamped ?? "an embedder that didn't say"} ({dimensions} dimensions); " +
                        $"this store's embedder is {identity ?? "one that doesn't say"} ({Dimensions} dimensions). " +
                        "Vectors from different models aren't comparable: use another table, or re-index this one.");
                }

                return;
            }
        }

        await using var write = new NpgsqlCommand(
            $"INSERT INTO {EmbeddersTable} (table_name, embedder, dimensions) VALUES (@table, @embedder, @dimensions)", connection);
        write.Parameters.AddWithValue("table", Table);
        write.Parameters.AddWithValue("embedder", (object?)identity ?? DBNull.Value);
        write.Parameters.AddWithValue("dimensions", Dimensions);
        await write.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static string Checked(string name, string option) =>
        Identifier().IsMatch(name)
            ? name
            : throw new ArgumentException(
                $"'{name}' isn't a plain lower-case identifier (letters, digits, underscores, optionally schema-qualified).", option);

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}(\\.[a-z_][a-z0-9_]{0,62})?$")]
    private static partial Regex Identifier();
}
