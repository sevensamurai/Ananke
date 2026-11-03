namespace Ananke.Abstractions.Agents;

/// <summary>
/// Abstraction over a text embedding provider. Implement this interface to integrate
/// any embedding model backend (OpenAI, Google, local models via Ollama, etc.).
/// </summary>
public interface IEmbeddingModel
{
    /// <summary>Embeds a single text string and returns its vector representation.</summary>
    Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default);

    /// <summary>Embeds multiple texts in a single batch call for efficiency.</summary>
    Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default);

    /// <summary>
    /// Which model makes this embedder's vectors, or <see langword="null"/> when the embedder doesn't say.
    /// A store that records it refuses to be read or written with a different one.
    /// </summary>
    EmbeddingModelIdentity? Identity => null;
}
