namespace Ananke.Orchestration.Knowledge;

/// <summary>
/// Splits an <see cref="ExtractedDocument"/> into smaller chunks suitable for embedding.
/// Implementations control chunking strategy (sliding window, structure-aware, semantic, etc.).
/// </summary>
public interface IDocumentChunker
{
    /// <summary>
    /// Splits the extracted document into chunks, each carrying metadata from the source section.
    /// </summary>
    IReadOnlyList<DocumentChunk> Chunk(ExtractedDocument document, ChunkingOptions? options = null);
}

/// <summary>A single chunk of text ready for embedding, with associated metadata.</summary>
/// <param name="Text">The chunk text content.</param>
/// <param name="Metadata">Metadata inherited from the source section (page, section title, etc.).</param>
public sealed record DocumentChunk(
    string Text,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>Options controlling how documents are split into chunks.</summary>
/// <param name="MaxTokens">Maximum estimated tokens per chunk. Default is 512.</param>
/// <param name="OverlapTokens">Number of overlapping tokens between consecutive chunks. Default is 64.</param>
public sealed record ChunkingOptions(
    int MaxTokens = 512,
    int OverlapTokens = 64);
