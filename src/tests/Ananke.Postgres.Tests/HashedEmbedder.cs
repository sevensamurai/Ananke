using Ananke.Abstractions.Agents;

namespace Ananke.Postgres.Tests;

/// <summary>
/// A vector for any text, the same on every run: <paramref name="dimensions"/> components in [0, 1) drawn from a
/// generator seeded by the text's hash. Every pair of vectors has a positive cosine similarity, so no row falls under
/// the default score threshold of 0.
/// </summary>
internal sealed class HashedEmbedder(int dimensions) : IEmbeddingModel
{
    public EmbeddingModelIdentity? Identity { get; } = new("test", "hashed", dimensions);

    public Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default) =>
        Task.FromResult<ReadOnlyMemory<float>>(Vector(text));

    public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>([.. texts.Select(t => (ReadOnlyMemory<float>)Vector(t))]);

    private float[] Vector(string text)
    {
        var random = new Random(Seed(text));
        var vector = new float[dimensions];
        for (var i = 0; i < vector.Length; i++)
            vector[i] = (float)random.NextDouble();
        return vector;
    }

    // FNV-1a over the text's characters: string.GetHashCode differs from run to run.
    private static int Seed(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text)
            hash = (hash ^ c) * 16777619u;
        return (int)hash;
    }
}
