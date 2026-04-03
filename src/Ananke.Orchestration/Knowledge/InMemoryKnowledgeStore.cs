using System.Collections.Concurrent;
using Ananke.Orchestration.Knowledge.Embeddings;

namespace Ananke.Orchestration.Knowledge;

/// <summary>
/// In-memory <see cref="IKnowledgeStore"/> for testing and single-process scenarios.
/// Uses brute-force cosine similarity over all stored vectors.
/// </summary>
public sealed class InMemoryKnowledgeStore : IKnowledgeStore
{
    private readonly ConcurrentDictionary<string, StoredDocument> _documents = new();
    private readonly IEmbeddingModel _embedder;

    /// <summary>
    /// Creates a new in-memory knowledge store.
    /// </summary>
    /// <param name="embedder">The embedding model used to embed documents and queries.</param>
    public InMemoryKnowledgeStore(IEmbeddingModel embedder)
    {
        ArgumentNullException.ThrowIfNull(embedder);
        _embedder = embedder;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(
        string query, SearchOptions? options = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        options ??= new SearchOptions();
        var queryEmbedding = await _embedder.EmbedAsync(query, ct);

        var scored = new List<(string Id, float Score, StoredDocument Doc)>();

        foreach (var (id, doc) in _documents)
        {
            if (!MatchesFilter(doc.Metadata, options.Filter))
                continue;

            var score = CosineSimilarity(queryEmbedding.Span, doc.Embedding.Span);
            if (score >= options.ScoreThreshold)
                scored.Add((id, score, doc));
        }

        scored.Sort((a, b) => b.Score.CompareTo(a.Score));

        return scored
            .Take(options.TopK)
            .Select(s => new KnowledgeChunk
            {
                Id = s.Id,
                Text = s.Doc.Text,
                Score = s.Score,
                Metadata = s.Doc.Metadata
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var docList = documents.ToList();
        if (docList.Count == 0) return;

        var texts = docList.Select(d => d.Text).ToList();
        var embeddings = await _embedder.EmbedBatchAsync(texts, ct);

        for (var i = 0; i < docList.Count; i++)
        {
            var doc = docList[i];
            _documents[doc.Id] = new StoredDocument(doc.Text, embeddings[i], doc.Metadata);
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
        IReadOnlyDictionary<string, string> Metadata);
}
