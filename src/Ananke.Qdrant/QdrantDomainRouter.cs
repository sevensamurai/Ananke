using Ananke.Abstractions.Agents;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Ananke.Qdrant;

/// <summary>
/// Qdrant-backed <see cref="IDomainRouter"/> that classifies user prompts
/// to child cells using vector similarity. Each child cell is represented
/// as a Qdrant point whose vector is the embedding of its concatenated
/// tool descriptions. Prompt routing is a nearest-neighbor search.
/// </summary>
/// <remarks>
/// <para>
/// This is Phase 1 of the Hybrid Routing design (Option D): division emits a routing artifact,
/// and routing uses semantic similarity instead of keyword matching.
/// The <c>FakeEmbeddingModel</c> from the demo produces deterministic
/// hash-based vectors — no API key required.
/// </para>
/// <para>
/// Wrap with a <c>RoutingAffinityTracker</c> for Phase 2 adaptive refinement.
/// </para>
/// </remarks>
public sealed class QdrantDomainRouter : IDomainRouter
{
    private const string CellNamePayloadKey = "cell_name";
    private const string DomainPayloadKey = "domain";

    private readonly QdrantClient _client;
    private readonly IEmbeddingModel _embedder;
    private readonly string _collectionName;
    private readonly uint _vectorSize;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    // Fallback: first cell indexed (used when Qdrant returns no results)
    private string? _fallbackCell;

    /// <summary>
    /// Creates a Qdrant-backed domain router.
    /// </summary>
    /// <param name="client">Qdrant gRPC client instance.</param>
    /// <param name="embedder">Embedding model for vectorizing tool descriptions and prompts.</param>
    /// <param name="collectionName">Qdrant collection name. Default is <c>"domain_routing"</c>.</param>
    /// <param name="vectorSize">Dimensionality of the embedding vectors. Must match the embedding model output.</param>
    public QdrantDomainRouter(
        QdrantClient client,
        IEmbeddingModel embedder,
        string collectionName = "domain_routing",
        uint vectorSize = 16)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(embedder);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        _client = client;
        _embedder = embedder;
        _collectionName = collectionName;
        _vectorSize = vectorSize;
    }

    /// <inheritdoc />
    public async Task<string> RouteAsync(string userMessage, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
        await EnsureCollectionAsync(ct);

        var queryVector = await _embedder.EmbedAsync(userMessage, ct);

        var results = await _client.SearchAsync(
            collectionName: _collectionName,
            vector: queryVector,
            limit: 1,
            payloadSelector: true,
            cancellationToken: ct);

        if (results.Count > 0)
        {
            var cellName = results[0].Payload[CellNamePayloadKey].StringValue;
            return cellName;
        }

        // Fallback: return first indexed cell (should not happen with indexed data)
        return _fallbackCell
            ?? throw new InvalidOperationException("No cells indexed in the domain router.");
    }

    /// <inheritdoc />
    public async Task IndexAsync(
        IReadOnlyList<ChildSpec> children,
        IReadOnlyDictionary<string, string> toolDescriptions,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(children);
        ArgumentNullException.ThrowIfNull(toolDescriptions);
        await EnsureCollectionAsync(ct);

        // Delete existing points (re-index after new division)
        try
        {
            await _client.DeleteAsync(
                _collectionName,
                new Filter { Must = { Conditions.MatchKeyword(CellNamePayloadKey, children[0].Name) } },
                cancellationToken: ct);
        }
        catch (Exception)
        {
            // Collection may be empty or not yet created — delete is best-effort
        }

        // Build one point per child: embed the concatenated tool descriptions
        var points = new List<PointStruct>(children.Count);

        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];

            // Build a semantic description of this cell's capabilities
            var toolTexts = child.Tools
                .Where(toolDescriptions.ContainsKey)
                .Select(t => $"{t}: {toolDescriptions[t]}");
            var cellDescription = $"Domain: {child.Domain}. Tools: {string.Join(". ", toolTexts)}";

            var embedding = await _embedder.EmbedAsync(cellDescription, ct);

            points.Add(new PointStruct
            {
                Id = new PointId { Num = (ulong)(i + 1) },
                Vectors = embedding.ToArray(),
                Payload =
                {
                    [CellNamePayloadKey] = child.Name,
                    [DomainPayloadKey] = child.Domain
                }
            });
        }

        await _client.UpsertAsync(_collectionName, points, cancellationToken: ct);
        _fallbackCell = children[0].Name;
    }

    private async Task EnsureCollectionAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            var exists = await _client.CollectionExistsAsync(_collectionName, ct);
            if (!exists)
            {
                await _client.CreateCollectionAsync(
                    _collectionName,
                    new VectorParams { Size = _vectorSize, Distance = Distance.Cosine },
                    cancellationToken: ct);
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
