using Ananke.Abstractions.Agents;

namespace LearningPrimitivesDemo.Routing;

/// <summary>
/// Deterministic fake embedding model for demos (no API key needed).
/// Produces a stable unit vector derived from the input text hash.
/// Dimension-16 vectors — lightweight but sufficient for cosine similarity.
/// </summary>
internal sealed class FakeEmbeddingModel : IEmbeddingModel
{
    private const int Dimensions = 16;

    public Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default) =>
        Task.FromResult<ReadOnlyMemory<float>>(Embed(text));

    public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(
            texts.Select(t => (ReadOnlyMemory<float>)Embed(t)).ToList());

    private static float[] Embed(string text)
    {
        var hash = 0;
        foreach (var c in text.ToLowerInvariant())
            hash = hash * 31 + c;

        var rng = new Random(hash);
        var vec = new float[Dimensions];
        for (var i = 0; i < Dimensions; i++)
            vec[i] = (float)rng.NextDouble();
        var mag = MathF.Sqrt(vec.Sum(v => v * v));
        for (var i = 0; i < Dimensions; i++)
            vec[i] /= mag;
        return vec;
    }
}
