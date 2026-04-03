using System.ClientModel;
using Ananke.Orchestration.Knowledge.Embeddings;
using OpenAI;
using OpenAI.Embeddings;

namespace Ananke.Orchestration.OpenAI;

/// <summary>
/// OpenAI implementation of <see cref="IEmbeddingModel"/> using the Embeddings API.
/// Supports OpenAI-compatible providers such as Ollama, LM Studio, vLLM, and Azure OpenAI
/// via the optional endpoint parameter on <see cref="Create"/>.
/// </summary>
public sealed class OpenAIEmbeddingModel(EmbeddingClient client) : IEmbeddingModel
{
    private readonly EmbeddingClient _client = client;

    /// <summary>
    /// Creates an <see cref="OpenAIEmbeddingModel"/> from an API key, model name, and optional
    /// custom endpoint.
    /// </summary>
    /// <param name="apiKey">API key. For local servers that don't require auth, use any non-empty string (e.g. <c>"ollama"</c>).</param>
    /// <param name="model">Embedding model name (e.g. <c>"text-embedding-3-small"</c>).</param>
    /// <param name="endpoint">Custom API base URL, or <see langword="null"/> for the default OpenAI endpoint.</param>
    public static OpenAIEmbeddingModel Create(
        string apiKey, string model = "text-embedding-3-small", Uri? endpoint = null)
    {
        var credential = new ApiKeyCredential(apiKey);

        if (endpoint is not null)
        {
            var options = new OpenAIClientOptions { Endpoint = endpoint };
            return new OpenAIEmbeddingModel(new EmbeddingClient(model, credential, options));
        }

        return new OpenAIEmbeddingModel(new EmbeddingClient(model, credential));
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var result = await _client.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return result.Value.ToFloats();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (texts.Count == 0)
            return [];

        var result = await _client.GenerateEmbeddingsAsync(texts, cancellationToken: ct);
        return result.Value
            .OrderBy(e => e.Index)
            .Select(e => e.ToFloats())
            .ToList();
    }
}
