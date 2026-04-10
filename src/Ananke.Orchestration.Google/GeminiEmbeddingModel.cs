using Ananke.Abstractions.Agents;
using Google.GenAI;
using Google.GenAI.Types;

namespace Ananke.Orchestration.Google;

/// <summary>
/// Google Gemini implementation of <see cref="IEmbeddingModel"/> using the
/// <c>text-embedding-004</c> model via the official <c>Google.GenAI</c> SDK.
/// Supports both the Gemini Developer API (API key) and Vertex AI.
/// </summary>
public sealed class GeminiEmbeddingModel : IEmbeddingModel
{
    private readonly Client _client;
    private readonly string _model;

    /// <summary>
    /// Creates a <see cref="GeminiEmbeddingModel"/> from an existing <see cref="Client"/>.
    /// </summary>
    /// <param name="client">A configured Google GenAI client.</param>
    /// <param name="model">Embedding model name (e.g. <c>"text-embedding-004"</c>).</param>
    public GeminiEmbeddingModel(Client client, string model = "text-embedding-004")
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        _client = client;
        _model = model;
    }

    /// <summary>
    /// Creates a <see cref="GeminiEmbeddingModel"/> for the Gemini Developer API.
    /// </summary>
    /// <param name="apiKey">Google AI API key.</param>
    /// <param name="model">Embedding model name (e.g. <c>"text-embedding-004"</c>).</param>
    public static GeminiEmbeddingModel Create(string apiKey, string model = "text-embedding-004") =>
        new(new Client(apiKey: apiKey), model);

    /// <summary>
    /// Creates a <see cref="GeminiEmbeddingModel"/> for Google Vertex AI using
    /// Application Default Credentials.
    /// </summary>
    /// <param name="project">Google Cloud project ID.</param>
    /// <param name="location">Google Cloud region (e.g. <c>"us-central1"</c>).</param>
    /// <param name="model">Embedding model name (e.g. <c>"text-embedding-004"</c>).</param>
    public static GeminiEmbeddingModel CreateVertexAI(
        string project, string location, string model = "text-embedding-004") =>
        new(new Client(project: project, location: location, vertexAI: true), model);

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var response = await _client.Models.EmbedContentAsync(
            model: _model, contents: text, config: null, cancellationToken: ct);

        return ToFloatMemory(response.Embeddings![0]);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (texts.Count == 0)
            return [];

        var contents = texts.Select(t => new Content
        {
            Parts = [Part.FromText(t)]
        }).ToList();

        var response = await _client.Models.EmbedContentAsync(
            model: _model, contents: contents, config: null, cancellationToken: ct);

        return response.Embeddings!
            .Select(ToFloatMemory)
            .ToList();
    }

    private static ReadOnlyMemory<float> ToFloatMemory(ContentEmbedding embedding) =>
        embedding.Values!.Select(v => (float)v).ToArray();
}
