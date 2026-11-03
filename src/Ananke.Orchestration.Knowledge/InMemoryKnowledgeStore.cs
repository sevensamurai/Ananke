using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge.Embeddings;

namespace Ananke.Orchestration.Knowledge;

/// <summary>
/// In-memory <see cref="IKnowledgeStore"/> for testing and single-process scenarios.
/// Searches by brute force over every stored document: cosine similarity for
/// <see cref="SearchMode.Dense"/>, BM25 over the documents' terms for <see cref="SearchMode.Sparse"/>,
/// and both rankings fused by reciprocal rank for <see cref="SearchMode.Hybrid"/>.
/// </summary>
/// <remarks>
/// Terms are runs of letters and digits, lower-cased, keeping inner dots, hyphens and underscores, so
/// <c>Core.Identity</c> and <c>oauth2-proxy</c> are single terms. Term statistics are taken over the documents
/// that pass <see cref="SearchOptions.Filter"/>. <see cref="SearchOptions.ScoreThreshold"/> applies to cosine
/// similarity only. A result's <see cref="KnowledgeChunk.Score"/> is the score of the mode asked for.
/// </remarks>
#pragma warning disable ANNKEEXP004 // implements the experimental listing
public sealed partial class InMemoryKnowledgeStore : IListableKnowledgeStore
#pragma warning restore ANNKEEXP004
{
    private const double Bm25K1 = 1.2;
    private const double Bm25B = 0.75;
    private const int ReciprocalRankK = 60;
    private const int CandidatesPerTopK = 4;

    private readonly ConcurrentDictionary<string, StoredDocument> _documents = new();
    private readonly IEmbeddingModel _embedder;
    // 5.7: Hard cap prevents unbounded heap growth in long-running single-process scenarios.
    private readonly int _maxDocuments;

    /// <summary>
    /// Creates a new in-memory knowledge store.
    /// </summary>
    /// <param name="embedder">The embedding model used to embed documents and queries.</param>
    /// <param name="maxDocuments">
    /// Maximum number of documents retained. When the store is at capacity, calls to
    /// <see cref="UpsertAsync"/> that would exceed the limit throw
    /// <see cref="InvalidOperationException"/>. Default is <c>100_000</c>.
    /// </param>
    public InMemoryKnowledgeStore(IEmbeddingModel embedder, int maxDocuments = 100_000)
    {
        ArgumentNullException.ThrowIfNull(embedder);
        if (maxDocuments <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDocuments), "Must be positive.");
        _embedder = embedder;
        _maxDocuments = maxDocuments;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(
        string query, SearchOptions? options = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        options ??= new SearchOptions();
        var candidates = _documents.Where(d => MatchesFilter(d.Value.Metadata, options.Filter)).ToList();

#pragma warning disable ANNKEEXP004 // limits per source, an experimental option
        var take = options.Candidates();
        IReadOnlyList<(string Id, float Score)> ranked = options.Mode switch
        {
            SearchMode.Sparse => RankByTerms(query, candidates, take),
            SearchMode.Hybrid => Fuse(
                await RankByEmbeddingAsync(query, candidates, options, take * CandidatesPerTopK, ct),
                RankByTerms(query, candidates, take * CandidatesPerTopK),
                take),
            _ => await RankByEmbeddingAsync(query, candidates, options, take, ct),
        };

        return ranked
            .Select(r => (r.Id, r.Score, Doc: _documents.TryGetValue(r.Id, out var doc) ? doc : null))
            .Where(r => r.Doc is not null)
            .Select(r => new KnowledgeChunk
            {
                Id = r.Id,
                Text = r.Doc!.Text,
                Score = r.Score,
                Metadata = r.Doc.Metadata
            })
            .LimitPerSource(options);
#pragma warning restore ANNKEEXP004
    }

    /// <inheritdoc />
    public async Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var docList = documents.ToList();
        if (docList.Count == 0) return;

        // 5.7: Enforce capacity. Net-new = docs whose IDs don't already exist.
        var netNew = docList.Count(d => !_documents.ContainsKey(d.Id));
        if (_documents.Count + netNew > _maxDocuments)
            throw new InvalidOperationException(
                $"InMemoryKnowledgeStore is at capacity ({_maxDocuments:N0} documents). " +
                "Delete documents before upserting more.");

        var texts = docList.Select(d => d.Text).ToList();
        var embeddings = await _embedder.EmbedBatchAsync(texts, ct);

        for (var i = 0; i < docList.Count; i++)
        {
            var doc = docList[i];
            _documents[doc.Id] = new StoredDocument(doc.Text, embeddings[i], doc.Metadata, CountTerms(doc.Text));
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        foreach (var (id, doc) in _documents)
        {
            if (MatchesFilter(doc.Metadata, filter))
                _documents.TryRemove(id, out _);
        }

        return Task.CompletedTask;
    }

    /// <summary>Returns the number of documents currently stored.</summary>
    public int Count => _documents.Count;

    /// <inheritdoc />
    public Task<IReadOnlyList<KnowledgeChunk>> ListAsync(KnowledgeFilter filter, int limit = 1000, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);

        IReadOnlyList<KnowledgeChunk> listed = [.. _documents
            .Where(d => MatchesFilter(d.Value.Metadata, filter))
            .OrderBy(d => d.Key, StringComparer.Ordinal)
            .Take(limit)
            .Select(d => new KnowledgeChunk { Id = d.Key, Text = d.Value.Text, Score = 0f, Metadata = d.Value.Metadata })];
        return Task.FromResult(listed);
    }

    private async Task<IReadOnlyList<(string Id, float Score)>> RankByEmbeddingAsync(
        string query,
        List<KeyValuePair<string, StoredDocument>> candidates,
        SearchOptions options,
        int take,
        CancellationToken ct)
    {
        var queryEmbedding = await _embedder.EmbedAsync(query, ct).ConfigureAwait(false);

        var scored = new List<(string Id, float Score)>();
        foreach (var (id, doc) in candidates)
        {
            var score = CosineSimilarity(queryEmbedding.Span, doc.Embedding.Span);
            if (score >= options.ScoreThreshold)
                scored.Add((id, score));
        }

        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        return scored.Take(take).ToList();
    }

    private static IReadOnlyList<(string Id, float Score)> RankByTerms(
        string query, List<KeyValuePair<string, StoredDocument>> candidates, int take)
    {
        var queryTerms = Terms(query).Distinct().ToList();
        if (queryTerms.Count == 0 || candidates.Count == 0)
            return [];

        var averageLength = candidates.Average(c => (double)c.Value.Length);
        var documentFrequency = queryTerms.ToDictionary(
            term => term,
            term => candidates.Count(c => c.Value.TermCounts.ContainsKey(term)));

        var scored = new List<(string Id, float Score)>();
        foreach (var (id, doc) in candidates)
        {
            var score = 0d;
            foreach (var term in queryTerms)
            {
                if (!doc.TermCounts.TryGetValue(term, out var frequency))
                    continue;

                var df = documentFrequency[term];
                var idf = Math.Log(1 + (candidates.Count - df + 0.5) / (df + 0.5));
                var lengthNorm = 1 - Bm25B + Bm25B * doc.Length / Math.Max(averageLength, 1);
                score += idf * frequency * (Bm25K1 + 1) / (frequency + Bm25K1 * lengthNorm);
            }

            if (score > 0)
                scored.Add((id, (float)score));
        }

        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        return scored.Take(take).ToList();
    }

    private static IReadOnlyList<(string Id, float Score)> Fuse(
        IReadOnlyList<(string Id, float Score)> dense,
        IReadOnlyList<(string Id, float Score)> sparse,
        int take)
    {
        var fused = new Dictionary<string, float>();
        foreach (var ranking in (IReadOnlyList<(string Id, float Score)>[])[dense, sparse])
        {
            for (var rank = 0; rank < ranking.Count; rank++)
            {
                var id = ranking[rank].Id;
                fused[id] = fused.GetValueOrDefault(id) + 1f / (ReciprocalRankK + rank + 1);
            }
        }

        return fused
            .OrderByDescending(f => f.Value)
            .ThenBy(f => f.Key, StringComparer.Ordinal)
            .Take(take)
            .Select(f => (f.Key, f.Value))
            .ToList();
    }

    private static Dictionary<string, int> CountTerms(string text) =>
        Terms(text).GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>A text's terms; a text whose terms can't be read within the pattern's time limit has none.</summary>
    private static IReadOnlyList<string> Terms(string text)
    {
        try
        {
            return [.. TermPattern().Matches(text.ToLowerInvariant()).Select(m => m.Value)];
        }
        catch (RegexMatchTimeoutException)
        {
            return [];
        }
    }

    [GeneratedRegex(@"[\p{L}\p{N}](?:[\p{L}\p{N}._-]*[\p{L}\p{N}])?", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TermPattern();

    private static bool MatchesFilter(
        IReadOnlyDictionary<string, string> metadata, KnowledgeFilter? filter)
    {
        if (filter is null or { Count: 0 })
            return true;

        foreach (var (key, value) in filter)
        {
            if (!metadata.TryGetValue(key, out var actual) || actual != value)
                return false;
        }

        return true;
    }

    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var dot = 0f;
        var normA = 0f;
        var normB = 0f;

        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator == 0f ? 0f : dot / denominator;
    }

    private sealed record StoredDocument(
        string Text,
        ReadOnlyMemory<float> Embedding,
        IReadOnlyDictionary<string, string> Metadata,
        IReadOnlyDictionary<string, int> TermCounts)
    {
        public int Length { get; } = TermCounts.Values.Sum();
    }
}
