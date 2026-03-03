namespace Ananke.Orchestration.Knowledge;

/// <summary>
/// Extracts text content from a document stream (PDF, HTML, plain text, etc.)
/// and produces Markdown-formatted output. All extractors must output Markdown as
/// the canonical internal representation — this enables Markdown-aware chunking
/// and produces text that LLMs read well.
/// </summary>
/// <remarks>
/// Implementations are format-specific and selected by file extension via <see cref="CanExtract"/>.
/// </remarks>
public interface IDocumentExtractor
{
    /// <summary>
    /// Returns <see langword="true"/> if this extractor can handle the given file extension
    /// (e.g. <c>".pdf"</c>, <c>".md"</c>).
    /// </summary>
    bool CanExtract(string fileExtension);

    /// <summary>
    /// Extracts structured text content from <paramref name="data"/>.
    /// </summary>
    /// <param name="data">The document content stream.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ExtractedDocument> ExtractAsync(Stream data, CancellationToken ct = default);
}

/// <summary>The result of extracting content from a document.</summary>
/// <param name="Sections">Ordered sections of extracted text.</param>
/// <param name="Metadata">Optional document-level metadata (title, author, page count, etc.).</param>
public sealed record ExtractedDocument(
    IReadOnlyList<ExtractedSection> Sections,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>A single section of extracted content in Markdown format.</summary>
/// <param name="Text">The extracted text content formatted as Markdown.
/// Links and images are also represented inline in the Markdown, but their structured
/// data is available via <paramref name="Links"/> and <paramref name="Images"/> for
/// programmatic access (indexing, crawling, vision model description, etc.).</param>
/// <param name="Page">Optional page number (1-based) for page-oriented formats.</param>
/// <param name="SectionTitle">Optional section or heading title.</param>
/// <param name="ContentType">Content type hint (e.g. <c>"text"</c>, <c>"table"</c>). Default is <c>"text"</c>.</param>
/// <param name="Links">Hyperlinks found in this section, if any.</param>
/// <param name="Images">Embedded images found in this section, if any.</param>
public sealed record ExtractedSection(
    string Text,
    int? Page = null,
    string? SectionTitle = null,
    string ContentType = "text",
    IReadOnlyList<ExtractedLink>? Links = null,
    IReadOnlyList<ExtractedImage>? Images = null);

/// <summary>A hyperlink found in an extracted document section.</summary>
/// <param name="Text">The visible anchor text of the link.</param>
/// <param name="Uri">The link target URI.</param>
public sealed record ExtractedLink(string Text, string Uri);

/// <summary>An embedded image found in an extracted document section.</summary>
/// <param name="Reference">
/// Identifier matching the Markdown placeholder in the section text
/// (e.g. <c>"embedded:page3:0"</c>).
/// </param>
/// <param name="Width">Image width in pixels.</param>
/// <param name="Height">Image height in pixels.</param>
/// <param name="PngData">Optional PNG-encoded image bytes. Populated when the extractor can
/// decode the image; <see langword="null"/> when only the placeholder is available.</param>
public sealed record ExtractedImage(
    string Reference,
    int Width,
    int Height,
    ReadOnlyMemory<byte>? PngData = null);
