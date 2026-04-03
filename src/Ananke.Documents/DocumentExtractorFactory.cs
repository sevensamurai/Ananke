using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Documents;

namespace Ananke.Documents;

/// <summary>
/// Registry that resolves the correct <see cref="IDocumentExtractor"/> for a given
/// file extension. Ships with built-in extractors for PDF, Markdown, and plain text;
/// additional extractors (HTML, XML, etc.) can be registered at construction time.
/// </summary>
public sealed class DocumentExtractorFactory
{
    private readonly IReadOnlyList<IDocumentExtractor> _extractors;

    /// <summary>
    /// Creates a factory pre-loaded with the built-in extractors
    /// (<see cref="PdfExtractor"/>, <see cref="MarkdownExtractor"/>,
    /// <see cref="PlainTextExtractor"/>).
    /// </summary>
    public DocumentExtractorFactory()
        : this([])
    {
    }

    /// <summary>
    /// Creates a factory with the built-in extractors plus any additional
    /// <paramref name="additionalExtractors"/> (e.g. future HTML or XML support).
    /// Custom extractors are evaluated first, so they can override built-in behaviour.
    /// </summary>
    public DocumentExtractorFactory(IEnumerable<IDocumentExtractor> additionalExtractors)
    {
        ArgumentNullException.ThrowIfNull(additionalExtractors);

        var list = new List<IDocumentExtractor>(additionalExtractors)
        {
            new PdfExtractor(),
            new MarkdownExtractor(),
            new PlainTextExtractor()
        };

        _extractors = list;
    }

    /// <summary>
    /// Returns the first extractor that can handle <paramref name="fileExtension"/>
    /// (e.g. <c>".pdf"</c>, <c>".md"</c>), or <see langword="null"/> if none matches.
    /// </summary>
    public IDocumentExtractor? GetExtractor(string fileExtension)
    {
        ArgumentNullException.ThrowIfNull(fileExtension);

        foreach (var extractor in _extractors)
        {
            if (extractor.CanExtract(fileExtension))
                return extractor;
        }

        return null;
    }

    /// <summary>
    /// Resolves an extractor from a file name, path, or URL by inspecting the extension.
    /// Query strings and fragments on HTTP(S) URLs are stripped before extraction.
    /// </summary>
    public IDocumentExtractor? GetExtractorForFile(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var extension = Path.GetExtension(GetPathFromUri(fileName));
        if (string.IsNullOrEmpty(extension))
            return null;

        return GetExtractor(extension);
    }

    /// <summary>
    /// Returns <see langword="true"/> if any registered extractor can handle
    /// <paramref name="fileExtension"/>.
    /// </summary>
    public bool CanExtract(string fileExtension) => GetExtractor(fileExtension) is not null;

    /// <summary>The full list of registered extractors, in evaluation order.</summary>
    public IReadOnlyList<IDocumentExtractor> Extractors => _extractors;

    private static string GetPathFromUri(string fileName)
    {
        // Strip query string and fragment so Path.GetExtension works on URLs
        if (Uri.TryCreate(fileName, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return uri.AbsolutePath;
        }

        return fileName;
    }
}
