using Ananke.Abstractions.Agents;

namespace Ananke.Postgres.Tests;

/// <summary>Fixed vectors by text, and an identity a test chooses, so rankings and stamps are known in advance.</summary>
internal sealed class FixedEmbedder(IReadOnlyDictionary<string, float[]> vectors, EmbeddingModelIdentity? identity = null) : IEmbeddingModel
{
    public EmbeddingModelIdentity? Identity => identity;

    public Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default) =>
        Task.FromResult<ReadOnlyMemory<float>>(Vector(text));

    public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>([.. texts.Select(t => (ReadOnlyMemory<float>)Vector(t))]);

    // Text the test didn't give a vector for (the catalog's composed text, a probe) gets one by length, never zero.
    private float[] Vector(string text) => vectors.TryGetValue(text, out var v) ? v : [1f, text.Length % 7, 1f];
}
