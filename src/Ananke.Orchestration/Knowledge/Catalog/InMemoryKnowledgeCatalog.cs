using System.Collections.Concurrent;
using Ananke.Orchestration.Knowledge.Embeddings;

namespace Ananke.Orchestration.Knowledge.Catalog;

/// <summary>
/// In-memory <see cref="IKnowledgeCatalog"/> for testing and single-process scenarios.
/// Uses brute-force cosine similarity over catalog entry embeddings.
/// </summary>
public sealed class InMemoryKnowledgeCatalog : IKnowledgeCatalog
{
    private readonly ConcurrentDictionary<string, StoredEntry> _entries = new();
    private readonly IEmbeddingModel _embedder;

    /// <summary>
    /// Creates a new in-memory knowledge catalog.
    /// </summary>
    /// <param name="embedder">Embedding model for vectorizing catalog summaries.</param>
    public InMemoryKnowledgeCatalog(IEmbeddingModel embedder)
    {
        ArgumentNullException.ThrowIfNull(embedder);
        _embedder = embedder;
    }

    /// <inheritdoc />
    public async Task IndexAsync(CatalogEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var text = BuildEmbeddingText(entry);
        var embedding = await _embedder.EmbedAsync(text, ct);
        _entries[entry.Source] = new StoredEntry(entry, embedding);
    }

    /// <inheritdoc />
    public Task RemoveAsync(string source, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        _entries.TryRemove(source, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<CatalogEntry?> GetAsync(string source, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        return Task.FromResult(
            _entries.TryGetValue(source, out var stored) ? stored.Entry : null);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CatalogSearchResult>> DiscoverAsync(
        string query, int topK = 5, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var queryEmbedding = await _embedder.EmbedAsync(query, ct);

        var scored = new List<CatalogSearchResult>();

        foreach (var (_, stored) in _entries)
        {
            if (stored.Entry.SupersededBy is not null) continue;

            var score = CosineSimilarity(queryEmbedding.Span, stored.Embedding.Span);
            scored.Add(new CatalogSearchResult { Entry = stored.Entry, Score = score });
        }

        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        return scored.Take(topK).ToList();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CatalogEntry>> BrowseAsync(
        CatalogBrowseOptions? options = null, CancellationToken ct = default)
    {
        options ??= new CatalogBrowseOptions();

        var results = _entries.Values
            .Select(s => s.Entry)
            .Where(e => options.Category is null
                        || string.Equals(e.Category, options.Category, StringComparison.OrdinalIgnoreCase))
            .Where(e => options.NotOlderThan is null || e.IndexedAt >= options.NotOlderThan)
            .OrderByDescending(e => e.IndexedAt)
            .Take(options.Limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<CatalogEntry>>(results);
    }

    /// <summary>Returns the number of entries currently in the catalog.</summary>
    public int Count => _entries.Count;

    private static string BuildEmbeddingText(CatalogEntry entry)
    {
        var keywords = entry.Keywords.Count > 0
            ? string.Join(", ", entry.Keywords)
            : string.Empty;

        return $"{entry.Summary}\nKeywords: {keywords}\nCategory: {entry.Category}";
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

    private sealed record StoredEntry(CatalogEntry Entry, ReadOnlyMemory<float> Embedding);
}
