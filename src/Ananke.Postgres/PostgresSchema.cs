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
    private const int MaxEfSearch = 1000;
    private static readonly Version IterativeScanSince = new(0, 8);

    private readonly SemaphoreSlim _lock = new(1, 1);
    // Read without the lock: volatile, so a reader that sees it true also sees Dimensions and HasIterativeScan.
    private volatile bool _ready;

    public string Table { get; } = CheckedTable(options.Table);

    public string TextSearchConfiguration { get; } = Checked(options.TextSearchConfiguration, nameof(options.TextSearchConfiguration));

    public int Dimensions { get; private set; }

    /// <summary>Whether the installed pgvector has <c>hnsw.iterative_scan</c> (0.8 or later); read in <see cref="EnsureAsync"/>.</summary>
    public bool HasIterativeScan { get; private set; }

    private readonly int _efSearch = options.HnswEfSearch is >= 1 and <= MaxEfSearch
        ? options.HnswEfSearch
        : throw new ArgumentOutOfRangeException(nameof(options), options.HnswEfSearch,
            $"{nameof(PostgresKnowledgeOptions.HnswEfSearch)} must be between 1 and {MaxEfSearch}, pgvector's limits.");

    private readonly PostgresIterativeScan _iterativeScan = Enum.IsDefined(options.HnswIterativeScan)
        ? options.HnswIterativeScan
        : throw new ArgumentOutOfRangeException(nameof(options), options.HnswIterativeScan,
            $"{nameof(PostgresKnowledgeOptions.HnswIterativeScan)} isn't one of {nameof(PostgresIterativeScan)}'s values.");

    /// <summary>Creates the table, its indexes and its stamp once; later calls return at once.</summary>
    /// <param name="columns">The table's columns other than <c>embedding</c>, as SQL.</param>
    /// <param name="indexes">Its indexes other than the embedding's, as SQL; <c>{0}</c> is the table.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="setup">Run last, on the same connection and transaction, for what else the table needs.</param>
    public async Task EnsureAsync(
        string columns, IReadOnlyList<string> indexes, CancellationToken ct,
        Func<NpgsqlConnection, CancellationToken, Task>? setup = null)
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
            HasIterativeScan = await ReadVectorVersionAsync(connection, ct).ConfigureAwait(false) >= IterativeScanSince;
            await ExecuteAsync(connection,
                $"CREATE TABLE IF NOT EXISTS {EmbeddersTable} (table_name text PRIMARY KEY, embedder text, dimensions integer NOT NULL)", ct).ConfigureAwait(false);
            await CheckStampAsync(connection, ct).ConfigureAwait(false);
            await ExecuteAsync(connection,
                $"CREATE TABLE IF NOT EXISTS {Table} ({columns}, embedding vector({Dimensions.ToString(CultureInfo.InvariantCulture)}) NOT NULL)", ct).ConfigureAwait(false);
            await ExecuteAsync(connection,
                $"CREATE INDEX IF NOT EXISTS {IndexName("embedding")} ON {Table} USING hnsw (embedding vector_cosine_ops)", ct).ConfigureAwait(false);
            foreach (var index in indexes)
                await ExecuteAsync(connection, string.Format(CultureInfo.InvariantCulture, index, Table), ct).ConfigureAwait(false);
            if (setup is not null)
                await setup(connection, ct).ConfigureAwait(false);

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            _ready = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Runs a nearest-neighbour query for <paramref name="rows"/> rows in a transaction of its own, with
    /// <c>hnsw.ef_search</c> at least <paramref name="rows"/> (up to 1,000) and, when pgvector has it,
    /// <c>hnsw.iterative_scan</c> as the options say. Both are <c>SET LOCAL</c>, so they end with the transaction.
    /// </summary>
    /// <remarks>The settings and the query go as one batch, so the server gets them in one round trip.</remarks>
    /// <param name="rows">How many rows the query asks for.</param>
    /// <param name="query">The query, sent after the settings in the same batch.</param>
    /// <param name="read">Reads the query's rows.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<TResult> NearestAsync<TResult>(
        int rows, NpgsqlBatchCommand query, Func<NpgsqlDataReader, Task<TResult>> read, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var batch = new NpgsqlBatch(connection, transaction);
        foreach (var setting in HnswSettings(rows))
            batch.BatchCommands.Add(new NpgsqlBatchCommand(setting));
        batch.BatchCommands.Add(query);
        TResult result;
        await using (var reader = await batch.ExecuteReaderAsync(ct).ConfigureAwait(false))
            result = await read(reader).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    /// <summary>The <c>SET LOCAL</c> statements for a nearest-neighbour query of <paramref name="rows"/> rows.</summary>
    internal IReadOnlyList<string> HnswSettings(int rows)
    {
        var efSearch = Math.Clamp(rows, _efSearch, MaxEfSearch).ToString(CultureInfo.InvariantCulture);
        var efSetting = $"SET LOCAL hnsw.ef_search = {efSearch}";
        return HasIterativeScan
            ? [efSetting, $"SET LOCAL hnsw.iterative_scan = {(_iterativeScan == PostgresIterativeScan.StrictOrder ? "strict_order" : "off")}"]
            : [efSetting];
    }

    /// <summary>An index name for this table: its name with dots made underscores, and a suffix.</summary>
    /// <param name="suffix">At most as long as <see cref="LongestIndexSuffix"/>, which <see cref="CheckedTable"/> checks the name for.</param>
    public string IndexName(string suffix) =>
        suffix.Length <= LongestIndexSuffix.Length
            ? IndexName(Table, suffix)
            : throw new ArgumentException($"'{suffix}' is longer than '{LongestIndexSuffix}', the longest suffix the table's name is checked for.", nameof(suffix));

    private static string IndexName(string table, string suffix) => $"{table.Replace('.', '_')}_{suffix}";

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

    private static async Task<Version?> ReadVectorVersionAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT extversion FROM pg_extension WHERE extname = 'vector'", connection);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is string text && Version.TryParse(text, out var version)
            ? version
            : null;
    }

    internal static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // Postgres cuts identifiers at 63 bytes, and CREATE ... IF NOT EXISTS then skips a name cut to one that exists.
    private const int MaxIdentifierLength = 63;

    // The store adds "_lexemes" and "_lexemes_count" to the table's name; a longer name would make them collide with
    // each other or with the table.
    private const int MaxTableNameLength = 49;

    // The longest suffix IndexName is given; index names hold the schema too.
    private const string LongestIndexSuffix = "embedding";

    private static string CheckedTable(string table)
    {
        var name = Checked(table, nameof(PostgresKnowledgeOptions.Table));
        if (name[(name.IndexOf('.') + 1)..].Length > MaxTableNameLength)
        {
            throw new ArgumentException(
                $"'{name}': the table's name, without its schema, may have at most {MaxTableNameLength} characters, as the store adds '_lexemes_count' to it and Postgres cuts names at {MaxIdentifierLength}.",
                nameof(PostgresKnowledgeOptions.Table));
        }

        return IndexName(name, LongestIndexSuffix).Length <= MaxIdentifierLength
            ? name
            : throw new ArgumentException(
                $"'{name}': the schema and table together, without the dot, may have at most {MaxIdentifierLength - LongestIndexSuffix.Length - 2} characters, as the indexes are named '<schema>_<table>_{LongestIndexSuffix}' and Postgres cuts names at {MaxIdentifierLength}.",
                nameof(PostgresKnowledgeOptions.Table));
    }

    private static string Checked(string name, string option) =>
        Identifier().IsMatch(name)
            ? name
            : throw new ArgumentException(
                $"'{name}' isn't a plain lower-case identifier (letters, digits, underscores, optionally schema-qualified).", option);

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}(\\.[a-z_][a-z0-9_]{0,62})?$")]
    private static partial Regex Identifier();
}
