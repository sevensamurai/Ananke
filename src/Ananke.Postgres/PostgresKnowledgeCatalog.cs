using System.Diagnostics.CodeAnalysis;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge.Catalog;
using Npgsql;

namespace Ananke.Postgres;

/// <summary>
/// An <see cref="IKnowledgeCatalog"/> on PostgreSQL: one row per source document, with its summary, keywords and
/// category embedded in a pgvector column for <see cref="DiscoverAsync"/>. Superseded entries are left out of discovery.
/// </summary>
/// <remarks>
/// Its table is <see cref="PostgresKnowledgeOptions.Table"/>, default <c>ananke_catalog</c> here. It is created on
/// first use and stamped with the embedder's identity, as <see cref="PostgresKnowledgeStore"/>'s is.
/// </remarks>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class PostgresKnowledgeCatalog : IKnowledgeCatalog
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IEmbeddingModel _embedder;
    private readonly PostgresSchema _schema;

    /// <summary>Creates a catalog over <paramref name="dataSource"/>; nothing is created until it is first used.</summary>
    public PostgresKnowledgeCatalog(NpgsqlDataSource dataSource, IEmbeddingModel embedder, PostgresKnowledgeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(embedder);
        _dataSource = dataSource;
        _embedder = embedder;
        _schema = new PostgresSchema(dataSource, embedder, options ?? new PostgresKnowledgeOptions { Table = "ananke_catalog" });
    }

    /// <inheritdoc />
    public async Task IndexAsync(CatalogEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await EnsureAsync(ct).ConfigureAwait(false);
        var embedding = await _embedder.EmbedAsync(EmbeddingText(entry), ct).ConfigureAwait(false);

        await using var command = _dataSource.CreateCommand(
            $"INSERT INTO {_schema.Table} (source, summary, keywords, category, indexed_at, chunk_count, superseded_by, effective_at, replaces, fields, embedding) " +
            "VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10::jsonb, $11::vector) ON CONFLICT (source) DO UPDATE SET summary = EXCLUDED.summary, " +
            "keywords = EXCLUDED.keywords, category = EXCLUDED.category, indexed_at = EXCLUDED.indexed_at, " +
            "chunk_count = EXCLUDED.chunk_count, superseded_by = EXCLUDED.superseded_by, effective_at = EXCLUDED.effective_at, " +
            "replaces = EXCLUDED.replaces, fields = EXCLUDED.fields, " +
            "embedding = EXCLUDED.embedding");
        command.Parameters.Add(new NpgsqlParameter { Value = entry.Source });
        command.Parameters.Add(new NpgsqlParameter { Value = entry.Summary });
        command.Parameters.Add(new NpgsqlParameter { Value = entry.Keywords.ToArray() });
        command.Parameters.Add(new NpgsqlParameter { Value = entry.Category });
        command.Parameters.Add(new NpgsqlParameter { Value = entry.IndexedAt.ToUniversalTime() });
        command.Parameters.Add(new NpgsqlParameter { Value = entry.ChunkCount });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)entry.SupersededBy ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter
        {
            Value = (object?)entry.EffectiveAt?.ToUniversalTime() ?? DBNull.Value,
            NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.TimestampTz,
        });
        command.Parameters.Add(new NpgsqlParameter { Value = entry.Replaces.ToArray() });
        command.Parameters.Add(new NpgsqlParameter { Value = System.Text.Json.JsonSerializer.Serialize(entry.Fields), NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter { Value = PostgresSchema.VectorText(embedding) });
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string source, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        await EnsureAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand($"DELETE FROM {_schema.Table} WHERE source = $1");
        command.Parameters.Add(new NpgsqlParameter { Value = source });
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CatalogEntry?> GetAsync(string source, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        await EnsureAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand($"SELECT {Columns}, 0 FROM {_schema.Table} WHERE source = $1");
        command.Parameters.Add(new NpgsqlParameter { Value = source });
        var entries = await ReadAsync(command, ct).ConfigureAwait(false);
        return entries.Count == 0 ? null : entries[0].Entry;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CatalogSearchResult>> DiscoverAsync(string query, int topK = 5, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        await EnsureAsync(ct).ConfigureAwait(false);
        var embedding = await _embedder.EmbedAsync(query, ct).ConfigureAwait(false);

        await using var command = _dataSource.CreateCommand(
            $"SELECT {Columns}, 1 - (embedding <=> $1::vector) FROM {_schema.Table} " +
            "WHERE superseded_by IS NULL ORDER BY embedding <=> $1::vector LIMIT $2");
        command.Parameters.Add(new NpgsqlParameter { Value = PostgresSchema.VectorText(embedding) });
        command.Parameters.Add(new NpgsqlParameter { Value = topK });
        return [.. (await ReadAsync(command, ct).ConfigureAwait(false)).Select(r => new CatalogSearchResult { Entry = r.Entry, Score = r.Score })];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CatalogEntry>> BrowseAsync(CatalogBrowseOptions? options = null, CancellationToken ct = default)
    {
        options ??= new CatalogBrowseOptions();
        await EnsureAsync(ct).ConfigureAwait(false);

        await using var command = _dataSource.CreateCommand(
            $"SELECT {Columns}, 0 FROM {_schema.Table} " +
            "WHERE ($1::text IS NULL OR lower(category) = lower($1)) AND ($2::timestamptz IS NULL OR indexed_at >= $2) " +
            "AND ($4::text IS NULL OR EXISTS (SELECT 1 FROM jsonb_each_text(fields) f WHERE lower(f.key) = lower($4) AND lower(f.value) = lower($5))) " +
            "ORDER BY CASE WHEN $6 THEN coalesce(effective_at, indexed_at) ELSE indexed_at END DESC LIMIT $3");
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)options.Category ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter
        {
            Value = (object?)options.NotOlderThan?.ToUniversalTime() ?? DBNull.Value,
            NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.TimestampTz,
        });
        command.Parameters.Add(new NpgsqlParameter { Value = options.Limit });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)options.Field ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)options.Value ?? "", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter { Value = options.Order == CatalogBrowseOrder.EffectiveAt });
        return [.. (await ReadAsync(command, ct).ConfigureAwait(false)).Select(r => r.Entry)];
    }

    private const string Columns = "source, summary, keywords, category, indexed_at, chunk_count, superseded_by, effective_at, replaces, fields";

    private Task EnsureAsync(CancellationToken ct) =>
        _schema.EnsureAsync(
            "source text PRIMARY KEY, summary text NOT NULL, keywords text[] NOT NULL, category text NOT NULL, " +
            "indexed_at timestamptz NOT NULL, chunk_count integer NOT NULL, superseded_by text, effective_at timestamptz, replaces text[] NOT NULL DEFAULT '{}', " +
            "fields jsonb NOT NULL DEFAULT '{}'",
            [
                "ALTER TABLE {0} ADD COLUMN IF NOT EXISTS effective_at timestamptz",
                "ALTER TABLE {0} ADD COLUMN IF NOT EXISTS replaces text[] NOT NULL DEFAULT '{{}}'",
                "ALTER TABLE {0} ADD COLUMN IF NOT EXISTS fields jsonb NOT NULL DEFAULT '{{}}'",
            ],
            ct);

    /// <summary>What is embedded for discovery: the same text <see cref="InMemoryKnowledgeCatalog"/> embeds.</summary>
    private static string EmbeddingText(CatalogEntry entry) =>
        $"{entry.Summary}\nKeywords: {string.Join(", ", entry.Keywords)}\nCategory: {entry.Category}";

    private static async Task<List<(CatalogEntry Entry, float Score)>> ReadAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var rows = new List<(CatalogEntry, float)>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var entry = new CatalogEntry
            {
                Source = reader.GetString(0),
                Summary = reader.GetString(1),
                Keywords = reader.GetFieldValue<string[]>(2),
                Category = reader.GetString(3),
                IndexedAt = reader.GetFieldValue<DateTimeOffset>(4),
                ChunkCount = reader.GetInt32(5),
                SupersededBy = await reader.IsDBNullAsync(6, ct).ConfigureAwait(false) ? null : reader.GetString(6),
                EffectiveAt = await reader.IsDBNullAsync(7, ct).ConfigureAwait(false) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                Replaces = reader.GetFieldValue<string[]>(8),
                Fields = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(9)) ?? [],
            };
            rows.Add((entry, Convert.ToSingle(reader.GetValue(10), System.Globalization.CultureInfo.InvariantCulture)));
        }

        return rows;
    }
}
