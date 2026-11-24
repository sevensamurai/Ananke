using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
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
/// Dense search reads the HNSW index with <c>hnsw.ef_search</c> raised to the rows asked and, on pgvector 0.8 or
/// later, <c>hnsw.iterative_scan</c> on, so a filter or a score threshold doesn't cut the rows returned short (see
/// <see cref="PostgresKnowledgeOptions.HnswEfSearch"/> and <see cref="PostgresKnowledgeOptions.HnswIterativeScan"/>).
/// A filter matches rows whose metadata contains every key and value given, and for each key in
/// <see cref="KnowledgeFilter.AnyOf"/> one of its values; the key <c>id</c> is the row's own id. Deletes remove rows.
/// </para>
/// <para>
/// Keyword search ranks by BM25 (k1 1.2, b 0.75, as <see cref="InMemoryKnowledgeStore"/>), from lexeme counts kept in
/// <c>&lt;table&gt;_lexemes</c>: how many rows hold each lexeme, and the table's rows and length. Triggers on the table
/// keep them as rows are inserted, updated and deleted, and writes to one table run one at a time. A table made without
/// them gets them, filled from <c>ts_stat</c>, on first use. Only rows holding a rare query term are scored (see
/// <see cref="PostgresKnowledgeOptions.RareTermShare"/>); a query with no rare term requires all its terms. The counts
/// are over the whole table, not the rows a filter keeps, so when a filter leaves no row matching, every term some
/// kept row holds is required instead, and then any term is enough.
/// </para>
/// </remarks>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class PostgresKnowledgeStore : IListableKnowledgeStore
{
    private const int ReciprocalRankK = 60;
    private const int CandidatesPerTopK = 4;
    private const double Bm25K1 = 1.2;
    private const double Bm25B = 0.75;
    private const string IdKey = "id";
    private static readonly Version DateOrderSince = new(16, 0);

    private readonly NpgsqlDataSource _dataSource;
    private readonly IEmbeddingModel _embedder;
    private readonly PostgresSchema _schema;
    private readonly string _lexemes;
    private readonly double _rareTermShare;

    /// <summary>Creates a store over <paramref name="dataSource"/>; nothing is created until it is first used.</summary>
    public PostgresKnowledgeStore(NpgsqlDataSource dataSource, IEmbeddingModel embedder, PostgresKnowledgeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(embedder);
        options ??= new PostgresKnowledgeOptions();
        _dataSource = dataSource;
        _embedder = embedder;
        _schema = new PostgresSchema(dataSource, embedder, options);
        _lexemes = _schema.Table + "_lexemes";
        _rareTermShare = options.RareTermShare is > 0 and <= 1
            ? options.RareTermShare
            : throw new ArgumentOutOfRangeException(nameof(options), options.RareTermShare,
                $"{nameof(PostgresKnowledgeOptions.RareTermShare)} must be more than 0 and at most 1.");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(
        string query, SearchOptions? options = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        options ??= new SearchOptions();
        if (options is { Mode: SearchMode.Hybrid, DenseWeight: 0, KeywordWeight: 0 })
            throw new ArgumentException(
                "Hybrid search needs a DenseWeight or a KeywordWeight above 0.", nameof(options));
        await EnsureAsync(ct).ConfigureAwait(false);

        var take = options.Candidates();
        var ranked = options.Mode switch
        {
            SearchMode.Sparse => await RankByTermsAsync(query, options.Filter, take, ct).ConfigureAwait(false),
            SearchMode.Hybrid => await HybridAsync(query, options, take, ct).ConfigureAwait(false),
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

        // One statement, so the lexeme counts are updated once for the whole batch. One statement can't write a row
        // twice, so the last document given for an id is the one kept.
        list = [.. Enumerable.Reverse(list).DistinctBy(d => d.Id).Reverse()];
        await EnsureAsync(ct).ConfigureAwait(false);
        var embeddings = await _embedder.EmbedBatchAsync([.. list.Select(d => d.Text)], ct).ConfigureAwait(false);

        await WriteAsync(
            $"INSERT INTO {_schema.Table} (id, text, metadata, embedding) " +
            "SELECT id, text, metadata, embedding::vector " +
            "FROM unnest($1::text[], $2::text[], $3::jsonb[], $4::text[]) AS u(id, text, metadata, embedding) " +
            "ON CONFLICT (id) DO UPDATE SET text = EXCLUDED.text, metadata = EXCLUDED.metadata, embedding = EXCLUDED.embedding",
            [
                new NpgsqlParameter { Value = list.Select(d => d.Id).ToArray() },
                new NpgsqlParameter { Value = list.Select(d => d.Text).ToArray() },
                new NpgsqlParameter
                {
                    Value = list.Select(d => JsonSerializer.Serialize(d.Metadata)).ToArray(),
                    NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Jsonb,
                },
                new NpgsqlParameter { Value = embeddings.Select(e => PostgresSchema.VectorText(e)).ToArray() },
            ],
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
#pragma warning disable ANNKEEXP004 // reads the experimental AnyOf
        if (filter.Count == 0 && filter.AnyOf.Count == 0)
            throw new ArgumentException("A delete with an empty filter would remove every document; name what to delete.", nameof(filter));
#pragma warning restore ANNKEEXP004
        await EnsureAsync(ct).ConfigureAwait(false);

        var (where, parameters) = Where(filter, first: 1);
        await WriteAsync($"DELETE FROM {_schema.Table} WHERE {where}", parameters, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// With <paramref name="order"/>, the key's value is cast to <c>timestamptz</c> in the query, as UTC when it has no
    /// offset; a value that isn't an ISO 8601 date and time, as <see cref="KnowledgeDateOrder"/> says, is read as none
    /// (<c>pg_input_is_valid</c>, PostgreSQL 16 or later).
    /// On an earlier server it throws <see cref="NotSupportedException"/>, naming the server's version.
    /// </remarks>
    public async Task<IReadOnlyList<KnowledgeChunk>> ListAsync(
        KnowledgeFilter filter, int limit = 1000, KnowledgeDateOrder? order = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        await EnsureAsync(ct).ConfigureAwait(false);

        if (order is null)
        {
            var (where, parameters) = Where(filter, first: 2);
            await using var byId = _dataSource.CreateCommand(
                $"SELECT id, text, metadata, 0::float8 AS score FROM {_schema.Table} WHERE {where} ORDER BY id LIMIT $1");
            byId.Parameters.Add(new NpgsqlParameter { Value = limit });
            byId.Parameters.AddRange(parameters);
            return await ReadAsync(byId, ct).ConfigureAwait(false);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(order.Key, nameof(order));

        // A value that isn't an ISO 8601 date and time, in the pattern KnowledgeDateOrder.ReadDate checks, is read as
        // no date, rather than cast as PostgreSQL reads it ('infinity', 'today', '03/04/2026'). One that ends in an
        // offset (Z, +hh, +hh:mm, +hhmm after the time) is cast to timestamptz; any other is read as UTC, not in the
        // session's time zone. One outside DateTimeOffset's range in UTC, which timestamptz holds, is read as no date
        // too (to 10000-01-01, where PostgreSQL rounds .NET's last tick).
        var (filtered, filterParameters) = Where(filter, first: 4);
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        CheckDateOrder(connection.PostgreSqlVersion);
        await using var byDate = new NpgsqlCommand(
            "SELECT id, text, metadata, 0::float8 AS score FROM (" +
            "SELECT id, text, metadata, " +
            "CASE WHEN at >= '0001-01-01 00:00:00+00' AND at <= '10000-01-01 00:00:00+00' THEN at END AS dated " +
            $"FROM {_schema.Table} CROSS JOIN LATERAL (SELECT metadata ->> $2 AS value) v " +
            "CROSS JOIN LATERAL (SELECT " +
            @"CASE WHEN value !~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}(T([01][0-9]|2[0-3]):[0-5][0-9](:[0-5][0-9](\.[0-9]{1,7})?)?(Z|[+-](0[0-9]|1[0-3])(:?[0-5][0-9])?|[+-]14(:?00)?)?)?$' " +
            "OR NOT pg_input_is_valid(value, 'timestamptz') THEN NULL " +
            "WHEN value ~ 'T.*(Z|[+-][0-9]{2}(:?[0-9]{2})?)$' THEN value::timestamptz " +
            "ELSE value::timestamp AT TIME ZONE 'UTC' END AS at) a " +
            $"WHERE {filtered}) listed " +
            "WHERE $3::timestamptz IS NULL OR dated >= $3 " +
            "ORDER BY dated DESC NULLS LAST, id LIMIT $1",
            connection);
        byDate.Parameters.Add(new NpgsqlParameter { Value = limit });
        byDate.Parameters.Add(new NpgsqlParameter { Value = order.Key, NpgsqlDbType = NpgsqlDbType.Text });
        byDate.Parameters.Add(new NpgsqlParameter
        {
            Value = (object?)order.Since?.ToUniversalTime() ?? DBNull.Value,
            NpgsqlDbType = NpgsqlDbType.TimestampTz,
        });
        byDate.Parameters.AddRange(filterParameters);
        return await ReadAsync(byDate, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>One <c>UPDATE</c> of the row's metadata, where its text and its metadata are still those listed.</remarks>
    public async Task UpdateMetadataAsync(KnowledgeChunk listed, IReadOnlyDictionary<string, string> metadata, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(listed);
        ArgumentNullException.ThrowIfNull(metadata);
        await EnsureAsync(ct).ConfigureAwait(false);

        await WriteAsync(
            $"UPDATE {_schema.Table} SET metadata = $1 WHERE id = $2 AND text = $3 AND metadata = $4",
            [
                Jsonb(metadata),
                new NpgsqlParameter { Value = listed.Id, NpgsqlDbType = NpgsqlDbType.Text },
                new NpgsqlParameter { Value = listed.Text, NpgsqlDbType = NpgsqlDbType.Text },
                Jsonb(listed.Metadata),
            ],
            ct).ConfigureAwait(false);
    }

    /// <summary>Throws when <paramref name="server"/> is older than PostgreSQL 16, which listing by date needs.</summary>
    internal static void CheckDateOrder(Version server)
    {
        if (server < DateOrderSince)
        {
            throw new NotSupportedException(
                $"Listing by date needs PostgreSQL {DateOrderSince.Major} or later (for pg_input_is_valid); this server is PostgreSQL {server}.");
        }
    }

    /// <summary>
    /// The table's write lock: held from the start of each write to its commit, so writes to one table run one at a
    /// time, and two writes can't deadlock on rows or on lexeme counts. The triggers take it too, before a statement
    /// from elsewhere touches a row. It is keyed by the table's oid, so every spelling of the table's name takes the
    /// same lock.
    /// </summary>
    /// <param name="relation">SQL for the table's oid.</param>
    private static string WriteLock(string relation) =>
        $"pg_advisory_xact_lock(hashtext('ananke_knowledge_lexemes'), hashtext(({relation})::oid::text))";

    private async Task WriteAsync(string sql, NpgsqlParameter[] parameters, CancellationToken ct)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await PostgresSchema.ExecuteAsync(connection, $"SELECT {WriteLock($"'{_schema.Table}'::regclass")}", ct).ConfigureAwait(false);
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddRange(parameters);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private Task EnsureAsync(CancellationToken ct) =>
        _schema.EnsureAsync(
            "id text PRIMARY KEY, text text NOT NULL, metadata jsonb NOT NULL DEFAULT '{}', " +
            $"tsv tsvector GENERATED ALWAYS AS (to_tsvector('{_schema.TextSearchConfiguration}', text)) STORED",
            [
                $"CREATE INDEX IF NOT EXISTS {_schema.IndexName("tsv")} ON {{0}} USING gin (tsv)",
                $"CREATE INDEX IF NOT EXISTS {_schema.IndexName("metadata")} ON {{0}} USING gin (metadata jsonb_path_ops)",
            ],
            ct,
            EnsureLexemesAsync);

    /// <summary>The triggers that keep the lexeme counts. Trigger names are per table, so they carry no table name.</summary>
    private static readonly string[] LexemeTriggers =
        ["ananke_lexemes_lock", "ananke_lexemes_insert", "ananke_lexemes_update", "ananke_lexemes_delete", "ananke_lexemes_truncate"];

    /// <summary>
    /// Creates the lexeme counts and the triggers that keep them, and fills them from the rows already in the table,
    /// when the table doesn't have every trigger (a table made by an earlier version, or dropped and made again, whose
    /// counts are then refilled). Runs under the schema lock; creating the triggers locks out writers until it commits.
    /// </summary>
    /// <remarks>
    /// One row per lexeme, as <c>ts_stat</c> gives it: <c>ndoc</c> the rows holding it, <c>nentry</c> its occurrences.
    /// The row with the empty word, which no lexeme is, holds the table's rows and the sum of every row's length.
    /// </remarks>
    private async Task EnsureLexemesAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using (var exists = new NpgsqlCommand(
            "SELECT to_regclass($1) IS NOT NULL AND " +
            "(SELECT count(*) FROM pg_trigger WHERE tgrelid = to_regclass($2) AND tgname = ANY($3)) = cardinality($3)",
            connection))
        {
            exists.Parameters.Add(new NpgsqlParameter { Value = _lexemes });
            exists.Parameters.Add(new NpgsqlParameter { Value = _schema.Table });
            exists.Parameters.Add(new NpgsqlParameter { Value = LexemeTriggers });
            if (await exists.ExecuteScalarAsync(ct).ConfigureAwait(false) is true)
                return;
        }

        var table = _schema.Table;
        var function = _lexemes + "_count";
        string[] statements =
        [
            $"CREATE TABLE IF NOT EXISTS {_lexemes} (word text PRIMARY KEY, ndoc bigint NOT NULL, nentry bigint NOT NULL)",
            $"TRUNCATE {_lexemes}",
            $"""
            CREATE OR REPLACE FUNCTION {function}() RETURNS trigger LANGUAGE plpgsql AS $fn$
            DECLARE emptied text[];
            BEGIN
                PERFORM {WriteLock("TG_RELID")};
                IF TG_WHEN = 'BEFORE' THEN
                    RETURN NULL;
                ELSIF TG_OP = 'TRUNCATE' THEN
                    DELETE FROM {_lexemes} WHERE word <> '';
                    UPDATE {_lexemes} SET ndoc = 0, nentry = 0 WHERE word = '';
                    RETURN NULL;
                ELSIF TG_OP = 'INSERT' THEN
                    {CountChanges("SELECT tsv, 1 FROM added")}
                ELSIF TG_OP = 'UPDATE' THEN
                    {CountChanges("SELECT tsv, -1 FROM removed UNION ALL SELECT tsv, 1 FROM added")}
                ELSE
                    {CountChanges("SELECT tsv, -1 FROM removed")}
                END IF;
                IF emptied IS NOT NULL THEN
                    DELETE FROM {_lexemes} WHERE word = ANY(emptied) AND ndoc = 0;
                END IF;
                RETURN NULL;
            END
            $fn$
            """,
            .. LexemeTriggers.Select(name => $"DROP TRIGGER IF EXISTS {name} ON {table}"),
            $"CREATE TRIGGER {LexemeTriggers[0]} BEFORE INSERT OR UPDATE OR DELETE ON {table} " +
                $"FOR EACH STATEMENT EXECUTE FUNCTION {function}()",
            $"CREATE TRIGGER {LexemeTriggers[1]} AFTER INSERT ON {table} " +
                $"REFERENCING NEW TABLE AS added FOR EACH STATEMENT EXECUTE FUNCTION {function}()",
            $"CREATE TRIGGER {LexemeTriggers[2]} AFTER UPDATE ON {table} " +
                $"REFERENCING OLD TABLE AS removed NEW TABLE AS added FOR EACH STATEMENT EXECUTE FUNCTION {function}()",
            $"CREATE TRIGGER {LexemeTriggers[3]} AFTER DELETE ON {table} " +
                $"REFERENCING OLD TABLE AS removed FOR EACH STATEMENT EXECUTE FUNCTION {function}()",
            $"CREATE TRIGGER {LexemeTriggers[4]} AFTER TRUNCATE ON {table} " +
                $"FOR EACH STATEMENT EXECUTE FUNCTION {function}()",
            $"INSERT INTO {_lexemes} (word, ndoc, nentry) SELECT word, ndoc, nentry FROM ts_stat('SELECT tsv FROM {table}')",
            $"INSERT INTO {_lexemes} (word, ndoc, nentry) " +
                $"SELECT '', (SELECT count(*) FROM {table}), (SELECT coalesce(sum(nentry), 0) FROM {_lexemes})",
        ];
        foreach (var statement in statements)
            await PostgresSchema.ExecuteAsync(connection, statement, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The trigger's statement that adds the changed rows' lexemes to the counts, in word order, and collects into
    /// <c>emptied</c> the words no row holds any more.
    /// </summary>
    /// <param name="changes">The changed rows, as (<c>tsv</c>, 1 for a row added or -1 for a row removed).</param>
    private string CountChanges(string changes) =>
        $"WITH changes(tsv, sign) AS ({changes}), " +
        "terms AS (SELECT t.lexeme AS word, c.sign, greatest(coalesce(cardinality(t.positions), 0), 1) AS n " +
        "FROM changes c, unnest(c.tsv) AS t), " +
        "delta AS (SELECT word, sum(sign) AS ndoc, sum(sign * n) AS nentry FROM terms GROUP BY word UNION ALL " +
        "SELECT '', (SELECT coalesce(sum(sign), 0) FROM changes), (SELECT coalesce(sum(sign * n), 0) FROM terms)), " +
        $"counted AS (INSERT INTO {_lexemes} AS l (word, ndoc, nentry) " +
        "SELECT word, ndoc, nentry FROM delta WHERE ndoc <> 0 OR nentry <> 0 ORDER BY word " +
        "ON CONFLICT (word) DO UPDATE SET ndoc = l.ndoc + EXCLUDED.ndoc, nentry = l.nentry + EXCLUDED.nentry " +
        "RETURNING word, ndoc) " +
        "SELECT array_agg(word) INTO emptied FROM counted WHERE ndoc = 0 AND word <> '';";

    /// <summary>
    /// Both rankings, each on its own connection and run at the same time, fused. The keyword query starts first, since
    /// an embedder can compute the embedding before its task first yields; when one ranking fails the other is cancelled.
    /// A ranking whose weight is 0 isn't run.
    /// </summary>
    private async Task<IReadOnlyList<KnowledgeChunk>> HybridAsync(string query, SearchOptions options, int take, CancellationToken ct)
    {
        using var failed = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sparse = options.KeywordWeight == 0
            ? Task.FromResult<IReadOnlyList<KnowledgeChunk>>([])
            : CancelOnFailureAsync(RankByTermsAsync(query, options.Filter, take * CandidatesPerTopK, failed.Token), failed);
        var dense = options.DenseWeight == 0
            ? Task.FromResult<IReadOnlyList<KnowledgeChunk>>([])
            : CancelOnFailureAsync(RankByEmbeddingAsync(query, options, take * CandidatesPerTopK, failed.Token), failed);
        var rankings = await Task.WhenAll(dense, sparse).ConfigureAwait(false);
        return Fuse(rankings[0], rankings[1], options, take);
    }

    private static async Task<IReadOnlyList<KnowledgeChunk>> CancelOnFailureAsync(
        Task<IReadOnlyList<KnowledgeChunk>> ranking, CancellationTokenSource failed)
    {
        try
        {
            return await ranking.ConfigureAwait(false);
        }
        catch
        {
            await failed.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<IReadOnlyList<KnowledgeChunk>> RankByEmbeddingAsync(
        string query, SearchOptions options, int take, CancellationToken ct)
    {
        var embedding = await _embedder.EmbedAsync(query, ct).ConfigureAwait(false);
        var (where, parameters) = Where(options.Filter, first: 4);
        // The score threshold is applied to the nearest rows, outside the scan: rows come in distance order, so it
        // keeps the same rows, and an iterative scan doesn't read on to hnsw.max_scan_tuples for rows it would drop.
        var command = new NpgsqlBatchCommand(
            "SELECT id, text, metadata, score FROM (" +
            $"SELECT id, text, metadata, 1 - (embedding <=> $1::vector) AS score FROM {_schema.Table} " +
            $"WHERE {where} ORDER BY embedding <=> $1::vector LIMIT $3) AS nearest " +
            "WHERE score >= $2 ORDER BY score DESC");
        command.Parameters.Add(new NpgsqlParameter { Value = PostgresSchema.VectorText(embedding) });
        command.Parameters.Add(new NpgsqlParameter { Value = (double)options.ScoreThreshold });
        command.Parameters.Add(new NpgsqlParameter { Value = take });
        command.Parameters.AddRange(parameters);
        return await _schema.NearestAsync(take, command, reader => ReadAsync(reader, ct), ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<KnowledgeChunk>> RankByTermsAsync(
        string query, KnowledgeFilter? filter, int take, CancellationToken ct)
    {
        // terms: the query's lexemes some row holds, each with its IDF and whether it is rare. lits: each as a tsquery
        // literal, the lexeme as it is, quoted (chr 39) with backslashes and quotes escaped (chr 92), so it isn't read
        // through the configuration again. m: three tsqueries, the rare terms (none when no term is rare), every term,
        // and any term. held: every term some row the filter keeps holds, read only when picked reaches it. picked: the
        // rows to score, those holding a rare term, else those holding every term; under a filter, a rule no row the
        // filter keeps meets gives way to held (no kept row holds a rare term then, so the rule over the terms kept rows
        // hold is all of them), and then to any term. It is materialized so its EXISTS run once, not once per row. Each
        // row is then scored by BM25 over every term it holds; its length is its occurrences.
        var config = _schema.TextSearchConfiguration;
        var (where, parameters) = Where(filter, first: 6);
#pragma warning disable ANNKEEXP004 // reads the experimental AnyOf
        var filtered = filter is { Count: > 0 } || filter?.AnyOf.Count > 0;
#pragma warning restore ANNKEEXP004
        string Kept(string tsquery) =>
            filtered ? $"EXISTS (SELECT 1 FROM {_schema.Table} r WHERE r.tsv @@ {tsquery} AND {where})" : "true";
        await using var command = _dataSource.CreateCommand(
            "WITH q AS (SELECT l.word, coalesce(s.ndoc, 0)::float8 AS df " +
            $"FROM unnest(tsvector_to_array(to_tsvector('{config}', $1))) AS l(word) LEFT JOIN {_lexemes} s ON s.word = l.word), " +
            $"totals AS (SELECT ndoc::float8 AS n, nentry::float8 / greatest(ndoc, 1) AS avgdl FROM {_lexemes} WHERE word = ''), " +
            "terms AS (SELECT q.word, ln(1 + (t.n - q.df + 0.5) / (q.df + 0.5)) AS idf, q.df <= ceil($3::numeric * t.n::numeric) AS rare " +
            "FROM q, totals t WHERE q.df > 0), " +
            "lits AS (SELECT rare, chr(39) || replace(replace(word, chr(92), chr(92) || chr(92)), chr(39), chr(92) || chr(39)) || chr(39) AS lit " +
            "FROM terms), " +
            "m AS (SELECT (string_agg(lit, ' | ') FILTER (WHERE rare))::tsquery AS rare_terms, " +
            "string_agg(lit, ' & ')::tsquery AS all_terms, string_agg(lit, ' | ')::tsquery AS any_term FROM lits), " +
            $"held AS MATERIALIZED (SELECT string_agg(lit, ' & ')::tsquery AS all_terms FROM lits WHERE {Kept("lit::tsquery")}), " +
            "picked AS MATERIALIZED (SELECT CASE " +
            $"WHEN m.rare_terms IS NOT NULL AND {Kept("m.rare_terms")} THEN m.rare_terms " +
            $"WHEN m.rare_terms IS NULL AND (m.all_terms IS NULL OR {Kept("m.all_terms")}) THEN m.all_terms " +
            $"WHEN {Kept("(SELECT all_terms FROM held)")} THEN (SELECT all_terms FROM held) " +
            "ELSE m.any_term END AS query FROM m) " +
            "SELECT r.id, r.text, r.metadata, s.score " +
            $"FROM {_schema.Table} r CROSS JOIN picked m CROSS JOIN totals t CROSS JOIN LATERAL (" +
            "SELECT sum(terms.idf * f.n * ($4 + 1) / (f.n + $4 * (1 - $5 + $5 * f.length / greatest(t.avgdl, 1))))::float8 AS score " +
            "FROM (SELECT lexeme, n, sum(n) OVER () AS length FROM (" +
            "SELECT lexeme, greatest(coalesce(cardinality(positions), 0), 1)::float8 AS n FROM unnest(r.tsv)) AS u) AS f " +
            "JOIN terms ON terms.word = f.lexeme) AS s " +
            $"WHERE r.tsv @@ m.query AND {where} ORDER BY s.score DESC, r.id LIMIT $2");
        command.Parameters.Add(new NpgsqlParameter { Value = query });
        command.Parameters.Add(new NpgsqlParameter { Value = take });
        command.Parameters.Add(new NpgsqlParameter { Value = _rareTermShare });
        command.Parameters.Add(new NpgsqlParameter { Value = Bm25K1 });
        command.Parameters.Add(new NpgsqlParameter { Value = Bm25B });
        command.Parameters.AddRange(parameters);
        return await ReadAsync(command, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<KnowledgeChunk>> ReadAsync(NpgsqlCommand command, CancellationToken ct)
    {
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await ReadAsync(reader, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<KnowledgeChunk>> ReadAsync(NpgsqlDataReader reader, CancellationToken ct)
    {
        var chunks = new List<KnowledgeChunk>();
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
        IReadOnlyList<KnowledgeChunk> dense, IReadOnlyList<KnowledgeChunk> sparse, SearchOptions options, int take)
    {
        var fused = new Dictionary<string, (KnowledgeChunk Chunk, float Score)>();
        foreach (var (ranking, weight) in (ReadOnlySpan<(IReadOnlyList<KnowledgeChunk>, float)>)
                 [(dense, options.DenseWeight), (sparse, options.KeywordWeight)])
        {
            for (var rank = 0; rank < ranking.Count; rank++)
            {
                var chunk = ranking[rank];
                var score = fused.TryGetValue(chunk.Id, out var seen) ? seen.Score : 0f;
                fused[chunk.Id] = (chunk, score + weight / (ReciprocalRankK + rank + 1));
            }
        }

        return fused.Values
            .OrderByDescending(f => f.Score)
            .ThenBy(f => f.Chunk.Id, StringComparer.Ordinal)
            .Take(take)
            .Select(f => f.Chunk with { Score = f.Score })
            .ToList();
    }

    /// <summary>
    /// The condition a row meets to match <paramref name="filter"/>, and its parameters, numbered from
    /// <paramref name="first"/>: containment of the filter's entries in its metadata, and for each key in
    /// <see cref="KnowledgeFilter.AnyOf"/> containment of any one of its values. The key <c>id</c> is the row's id
    /// column, not a metadata key. The values are joined with <c>OR</c> rather than <c>@&gt; ANY(array)</c> so the
    /// metadata's GIN index serves each one.
    /// </summary>
    private static (string Sql, NpgsqlParameter[] Parameters) Where(KnowledgeFilter? filter, int first)
    {
        filter ??= new KnowledgeFilter();
        var sql = new StringBuilder($"metadata @> ${first}");
        var hasId = filter.TryGetValue(IdKey, out var id);
        List<NpgsqlParameter> parameters = [Jsonb(hasId ? filter.Where(kv => kv.Key != IdKey).ToDictionary() : filter)];
        if (hasId)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND id = ${first + parameters.Count}");
            parameters.Add(new NpgsqlParameter { Value = id, NpgsqlDbType = NpgsqlDbType.Text });
        }
#pragma warning disable ANNKEEXP004 // applies the experimental AnyOf
        foreach (var (key, values) in filter.AnyOf)
#pragma warning restore ANNKEEXP004
        {
            if (key == IdKey)
            {
                sql.Append(CultureInfo.InvariantCulture, $" AND id = ANY(${first + parameters.Count})");
                parameters.Add(new NpgsqlParameter { Value = values.Distinct(StringComparer.Ordinal).ToArray(), NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text });
                continue;
            }

            List<string> any = [];
            foreach (var value in values.Distinct(StringComparer.Ordinal))
            {
                any.Add(string.Create(CultureInfo.InvariantCulture, $"metadata @> ${first + parameters.Count}"));
                parameters.Add(Jsonb(new Dictionary<string, string> { [key] = value }));
            }
            sql.Append(any.Count == 0 ? " AND false" : $" AND ({string.Join(" OR ", any)})");
        }

        return (sql.ToString(), [.. parameters]);
    }

    private static NpgsqlParameter Jsonb(IReadOnlyDictionary<string, string> value) => new()
    {
        Value = JsonSerializer.Serialize(value),
        NpgsqlDbType = NpgsqlDbType.Jsonb,
    };
}
