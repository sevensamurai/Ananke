using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge;
using Npgsql;
using NpgsqlTypes;

namespace Ananke.Postgres;

/// <summary>
/// An <see cref="IKnowledgeStore"/> on PostgreSQL. Embeddings are in a pgvector column searched by cosine distance;
/// text is in a generated <c>tsvector</c> column searched by full-text search; metadata is <c>jsonb</c>.
/// <see cref="SearchMode.Dense"/>, <see cref="SearchMode.Sparse"/> and <see cref="SearchMode.Hybrid"/> (both rankings
/// fused by reciprocal rank) are all supported.
/// </summary>
/// <remarks>
/// <para>
/// The table, its indexes (HNSW on the embedding, GIN on the text and the metadata) and the <c>vector</c> extension
/// are created on first use; the role needs the rights to create them, or they must exist. The table is stamped with
/// the embedder's <see cref="IEmbeddingModel.Identity"/>, and a store whose embedder differs is refused.
/// </para>
/// <para>
/// Keyword search matches any of the query's words, ranked by <c>ts_rank_cd</c>. A filter matches rows whose
/// metadata contains every key and value given. Deletes remove rows.
/// </para>
/// </remarks>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class PostgresKnowledgeStore : IListableKnowledgeStore
{
    private const int ReciprocalRankK = 60;
    private const int CandidatesPerTopK = 4;

    private readonly NpgsqlDataSource _dataSource;
    private readonly IEmbeddingModel _embedder;
    private readonly PostgresSchema _schema;

    /// <summary>Creates a store over <paramref name="dataSource"/>; nothing is created until it is first used.</summary>
    public PostgresKnowledgeStore(NpgsqlDataSource dataSource, IEmbeddingModel embedder, PostgresKnowledgeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(embedder);
        _dataSource = dataSource;
        _embedder = embedder;
        _schema = new PostgresSchema(dataSource, embedder, options ?? new PostgresKnowledgeOptions());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(
        string query, SearchOptions? options = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        options ??= new SearchOptions();
        await EnsureAsync(ct).ConfigureAwait(false);

        var take = options.Candidates();
        var ranked = options.Mode switch
        {
            SearchMode.Sparse => await RankByTermsAsync(query, options.Filter, take, ct).ConfigureAwait(false),
            SearchMode.Hybrid => Fuse(
                await RankByEmbeddingAsync(query, options, take * CandidatesPerTopK, ct).ConfigureAwait(false),
                await RankByTermsAsync(query, options.Filter, take * CandidatesPerTopK, ct).ConfigureAwait(false),
                take),
            _ => await RankByEmbeddingAsync(query, options, take, ct).ConfigureAwait(false),
        };
        return ranked.LimitPerSource(options);
    }

    /// <inheritdoc />
    public async Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var list = documents.ToList();
        if (list.Count == 0)
            return;

        await EnsureAsync(ct).ConfigureAwait(false);
        var embeddings = await _embedder.EmbedBatchAsync([.. list.Select(d => d.Text)], ct).ConfigureAwait(false);

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var batch = new NpgsqlBatch(connection);
        for (var i = 0; i < list.Count; i++)
        {
            var command = new NpgsqlBatchCommand(
                $"INSERT INTO {_schema.Table} (id, text, metadata, embedding) VALUES ($1, $2, $3, $4::vector) " +
                "ON CONFLICT (id) DO UPDATE SET text = EXCLUDED.text, metadata = EXCLUDED.metadata, embedding = EXCLUDED.embedding");
            command.Parameters.Add(new NpgsqlParameter { Value = list[i].Id });
            command.Parameters.Add(new NpgsqlParameter { Value = list[i].Text });
            command.Parameters.Add(new NpgsqlParameter { Value = JsonSerializer.Serialize(list[i].Metadata), NpgsqlDbType = NpgsqlDbType.Jsonb });
            command.Parameters.Add(new NpgsqlParameter { Value = PostgresSchema.VectorText(embeddings[i]) });
            batch.BatchCommands.Add(command);
        }

        await batch.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>An empty filter matches every row, so it empties the table.</remarks>
    public async Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await EnsureAsync(ct).ConfigureAwait(false);

        await using var command = _dataSource.CreateCommand($"DELETE FROM {_schema.Table} WHERE metadata @> $1");
        command.Parameters.Add(Filter(filter));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeChunk>> ListAsync(KnowledgeFilter filter, int limit = 1000, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        await EnsureAsync(ct).ConfigureAwait(false);

        await using var command = _dataSource.CreateCommand(
            $"SELECT id, text, metadata, 0::float8 AS score FROM {_schema.Table} WHERE metadata @> $1 ORDER BY id LIMIT $2");
        command.Parameters.Add(Filter(filter));
        command.Parameters.Add(new NpgsqlParameter { Value = limit });
        return await ReadAsync(command, ct).ConfigureAwait(false);
    }

    private Task EnsureAsync(CancellationToken ct) =>
        _schema.EnsureAsync(
            "id text PRIMARY KEY, text text NOT NULL, metadata jsonb NOT NULL DEFAULT '{}', " +
            $"tsv tsvector GENERATED ALWAYS AS (to_tsvector('{_schema.TextSearchConfiguration}', text)) STORED",
            [
                $"CREATE INDEX IF NOT EXISTS {_schema.IndexName("tsv")} ON {{0}} USING gin (tsv)",
                $"CREATE INDEX IF NOT EXISTS {_schema.IndexName("metadata")} ON {{0}} USING gin (metadata jsonb_path_ops)",
            ],
            ct);

    private async Task<IReadOnlyList<KnowledgeChunk>> RankByEmbeddingAsync(
        string query, SearchOptions options, int take, CancellationToken ct)
    {
        var embedding = await _embedder.EmbedAsync(query, ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            $"SELECT id, text, metadata, 1 - (embedding <=> $1::vector) AS score FROM {_schema.Table} " +
            "WHERE metadata @> $2 AND 1 - (embedding <=> $1::vector) >= $3 " +
            "ORDER BY embedding <=> $1::vector LIMIT $4");
        command.Parameters.Add(new NpgsqlParameter { Value = PostgresSchema.VectorText(embedding) });
        command.Parameters.Add(Filter(options.Filter));
        command.Parameters.Add(new NpgsqlParameter { Value = (double)options.ScoreThreshold });
        command.Parameters.Add(new NpgsqlParameter { Value = take });
        return await ReadAsync(command, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<KnowledgeChunk>> RankByTermsAsync(
        string query, KnowledgeFilter? filter, int take, CancellationToken ct)
    {
        // The query's own lexemes, each quoted and joined with OR, so a row needs only one of them, as BM25 does.
        var config = _schema.TextSearchConfiguration;
        await using var command = _dataSource.CreateCommand(
            $"WITH q AS (SELECT to_tsquery('{config}', coalesce(nullif(array_to_string(ARRAY(" +
            $"SELECT quote_literal(l) FROM unnest(tsvector_to_array(to_tsvector('{config}', $1))) AS l), ' | '), ''), '')) AS query) " +
            $"SELECT id, text, metadata, ts_rank_cd(tsv, q.query)::float8 AS score FROM {_schema.Table}, q " +
            "WHERE tsv @@ q.query AND metadata @> $2 ORDER BY score DESC, id LIMIT $3");
        command.Parameters.Add(new NpgsqlParameter { Value = query });
        command.Parameters.Add(Filter(filter));
        command.Parameters.Add(new NpgsqlParameter { Value = take });
        return await ReadAsync(command, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<KnowledgeChunk>> ReadAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var chunks = new List<KnowledgeChunk>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            chunks.Add(new KnowledgeChunk
            {
                Id = reader.GetString(0),
                Text = reader.GetString(1),
                Metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(2)) ?? [],
                Score = (float)reader.GetDouble(3),
            });
        }

        return chunks;
    }

    private static IReadOnlyList<KnowledgeChunk> Fuse(
        IReadOnlyList<KnowledgeChunk> dense, IReadOnlyList<KnowledgeChunk> sparse, int take)
    {
        var fused = new Dictionary<string, (KnowledgeChunk Chunk, float Score)>();
        foreach (var ranking in (IReadOnlyList<KnowledgeChunk>[])[dense, sparse])
        {
            for (var rank = 0; rank < ranking.Count; rank++)
            {
                var chunk = ranking[rank];
                var score = fused.TryGetValue(chunk.Id, out var seen) ? seen.Score : 0f;
                fused[chunk.Id] = (chunk, score + 1f / (ReciprocalRankK + rank + 1));
            }
        }

        return fused.Values
            .OrderByDescending(f => f.Score)
            .ThenBy(f => f.Chunk.Id, StringComparer.Ordinal)
            .Take(take)
            .Select(f => f.Chunk with { Score = f.Score })
            .ToList();
    }

    private static NpgsqlParameter Filter(KnowledgeFilter? filter) => new()
    {
        Value = JsonSerializer.Serialize(filter ?? new KnowledgeFilter()),
        NpgsqlDbType = NpgsqlDbType.Jsonb,
    };
}
