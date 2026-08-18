using System.Text.Json;
using Ananke.Abstractions.Agents;


namespace Ananke.Orchestration.Knowledge.Linking;

/// <summary>
/// LLM-based link extractor that discovers cross-document relationships after ingestion.
/// For each chunk in a newly ingested source, finds semantically related chunks in other
/// sources and asks the LLM to classify the relationship type.
/// </summary>
/// <remarks>
/// Follows the same pattern as <see cref="Catalog.CatalogKeywordExtractor"/>: an optional
/// post-ingestion enrichment step that enhances the knowledge graph without modifying
/// the core <see cref="IKnowledgeStore"/> contract.
/// </remarks>
public sealed class DocumentLinkExtractor
{
    private const string ClassificationPrompt =
        """
        You are a knowledge engineer analyzing relationships between document chunks.
        Given a SOURCE chunk and a CANDIDATE chunk, determine if they are meaningfully related.

        If related, classify the relationship as one of:
        - "references" — source explicitly or implicitly references the candidate's content
        - "extends" — source builds upon or elaborates on the candidate's topic
        - "prerequisite" — understanding the candidate is needed before the source
        - "example-of" — source is a concrete example of a concept in the candidate (or vice versa)
        - "contradicts" — source and candidate make conflicting claims
        - "none" — no meaningful relationship

        Also provide a confidence score from 0.0 to 1.0.
        Respond in the specified JSON format.
        """;

    private static readonly string ResponseSchema = JsonSerializer.Serialize(new
    {
        type = "object",
        properties = new
        {
            relationship = new
            {
                type = "string",
                @enum = new[] { "references", "extends", "prerequisite", "example-of", "contradicts", "none" },
                description = "The classified relationship type"
            },
            confidence = new
            {
                type = "number",
                description = "Confidence score from 0.0 to 1.0"
            }
        },
        required = new[] { "relationship", "confidence" },
        additionalProperties = false
    });

    private readonly IAgentModel _model;
    private readonly IKnowledgeStore _store;
    private readonly IDocumentLinkGraph _graph;
    private readonly float _similarityThreshold;
    private readonly int _maxCandidates;
    private readonly int _maxTextLength;

    /// <summary>
    /// Creates a document link extractor.
    /// </summary>
    /// <param name="model">Agent model used for relationship classification.</param>
    /// <param name="store">Knowledge store to search for candidate chunks.</param>
    /// <param name="graph">Link graph to store discovered relationships.</param>
    /// <param name="similarityThreshold">
    /// Minimum vector similarity score for a chunk to be considered a link candidate.
    /// Default is <c>0.7</c>.
    /// </param>
    /// <param name="maxCandidates">
    /// Maximum number of candidate chunks to evaluate per source chunk.
    /// Default is <c>5</c>.
    /// </param>
    /// <param name="maxTextLength">
    /// Maximum number of characters per chunk sent to the model.
    /// Default is <c>2000</c>.
    /// </param>
    public DocumentLinkExtractor(
        IAgentModel model,
        IKnowledgeStore store,
        IDocumentLinkGraph graph,
        float similarityThreshold = 0.7f,
        int maxCandidates = 5,
        int maxTextLength = 2000)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(graph);

        _model = model;
        _store = store;
        _graph = graph;
        _similarityThreshold = similarityThreshold;
        _maxCandidates = maxCandidates;
        _maxTextLength = maxTextLength;
    }

    /// <summary>
    /// For each chunk belonging to <paramref name="sourceId"/>, finds related chunks
    /// in other sources, classifies the relationship via LLM, and stores links in the graph.
    /// </summary>
    /// <param name="sourceId">
    /// The source identifier (matching the <c>source</c> metadata key) of the
    /// newly ingested document to link.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task LinkSourceAsync(string sourceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        // 1. Get all chunks for this source
        var sourceChunks = await _store.SearchAsync(
            "",
            new SearchOptions
            {
                TopK = 1000,
                Filter = new KnowledgeFilter { ["source"] = sourceId }
            },
            ct).ConfigureAwait(false);

        if (sourceChunks.Count == 0)
            return;

        // 2. For each source chunk, find related chunks in other sources
        foreach (var chunk in sourceChunks)
        {
            var candidates = await _store.SearchAsync(
                chunk.Text,
                new SearchOptions
                {
                    TopK = _maxCandidates + 1, // +1 to account for self-match
                    ScoreThreshold = _similarityThreshold
                },
                ct).ConfigureAwait(false);

            foreach (var candidate in candidates)
            {
                // Skip self-matches and same-source matches
                if (candidate.Id == chunk.Id)
                    continue;

                var candidateSource = candidate.Metadata.GetValueOrDefault("source");
                if (candidateSource == sourceId)
                    continue;

                // 3. Ask LLM to classify the relationship
                var classification = await ClassifyRelationshipAsync(
                    chunk.Text, candidate.Text, ct).ConfigureAwait(false);

                if (classification.Relationship == "none" || classification.Confidence < 0.5f)
                    continue;

                // 4. Store the link
                var weight = classification.Confidence * candidate.Score;
                await _graph.AddLinkAsync(
                    chunk.Id, candidate.Id, classification.Relationship,
                    Math.Clamp(weight, 0f, 1f), ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<LinkClassification> ClassifyRelationshipAsync(
        string sourceText, string candidateText, CancellationToken ct)
    {
        var truncatedSource = sourceText.Length > _maxTextLength
            ? sourceText[.._maxTextLength] : sourceText;
        var truncatedCandidate = candidateText.Length > _maxTextLength
            ? candidateText[.._maxTextLength] : candidateText;

        var userMessage = $"""
            SOURCE CHUNK:
            {truncatedSource}

            CANDIDATE CHUNK:
            {truncatedCandidate}
            """;

        var request = new AgentRequest
        {
            SystemPrompt = ClassificationPrompt,
            Messages = [AgentMessage.User(userMessage)],
            ResponseFormat = new AgentResponseFormat("link_classification", ResponseSchema)
        };

        var response = await _model.GenerateAsync(request, ct).ConfigureAwait(false);
        return ParseResponse(response.Text ?? "{}");
    }

    private static LinkClassification ParseResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var relationship = root.TryGetProperty("relationship", out var rel)
                ? rel.GetString() ?? "none"
                : "none";

            var confidence = root.TryGetProperty("confidence", out var conf)
                && conf.TryGetSingle(out var confValue)
                    ? confValue
                    : 0f;

            return new LinkClassification(relationship, confidence);
        }
        catch (JsonException)
        {
            return new LinkClassification("none", 0f);
        }
    }

    private readonly record struct LinkClassification(string Relationship, float Confidence);
}
