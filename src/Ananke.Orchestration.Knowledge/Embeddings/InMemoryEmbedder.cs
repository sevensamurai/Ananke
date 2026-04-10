using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Knowledge.Embeddings;

/// <summary>
/// Lightweight, deterministic embedding model using character hashing.
/// Produces normalized vectors via a stable hash of each word in the input text,
/// making it suitable for in-memory cosine similarity over small corpora.
/// No external API calls or model downloads required.
/// </summary>
/// <remarks>
/// Designed as the companion embedder for <see cref="InMemoryKnowledgeStore"/>
/// in testing, demos, and single-process scenarios.
/// </remarks>
public sealed class InMemoryEmbedder : IEmbeddingModel
{
    /// <summary>Default dimensionality for the embedding vectors.</summary>
    private const int DefaultDims = 64;

    private static readonly char[] Separators =
        [' ', '\n', '\r', '\t', '.', ',', '!', '?', ':', ';', '-', '(', ')', '"', '\''];

    private readonly int _dims;

    /// <summary>
    /// Creates a new <see cref="InMemoryEmbedder"/> with the specified vector dimensionality.
    /// </summary>
    /// <param name="dims">Number of dimensions for the output vectors. Defaults to 64.</param>
    public InMemoryEmbedder(int dims = DefaultDims)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dims, 1);
        _dims = dims;
    }

    /// <inheritdoc />
    public Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default) =>
        Task.FromResult(Embed(text));

    /// <inheritdoc />
    public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(
            texts.Select(Embed).ToList());

    private ReadOnlyMemory<float> Embed(string text)
    {
        var vec = new float[_dims];
        foreach (var word in text.ToLowerInvariant().Split(
            Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var h = StableHash(word);
            for (var i = 0; i < _dims; i++)
                vec[i] += ((h >> (i % 32)) & 1) == 1 ? 1f : -1f;
        }

        var norm = MathF.Sqrt(vec.Sum(v => v * v));
        if (norm > 0) for (var i = 0; i < _dims; i++) vec[i] /= norm;
        return vec;
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            var hash = 17;
            foreach (var c in s) hash = hash * 31 + c;
            return hash;
        }
    }
}
