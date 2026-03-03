using Ananke.Orchestration.Knowledge;

namespace Ananke.Documents;

/// <summary>
/// Plain-text document extractor. Reads the stream as UTF-8 text and produces a
/// single <see cref="ExtractedSection"/> with the content unchanged (plain text is
/// already valid Markdown).
/// </summary>
public sealed class PlainTextExtractor : IDocumentExtractor
{
    /// <inheritdoc />
    public bool CanExtract(string fileExtension) =>
        string.Equals(fileExtension, ".txt", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fileExtension, ".text", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<ExtractedDocument> ExtractAsync(
        Stream data, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        using var reader = new StreamReader(data, leaveOpen: true);
        var content = await reader.ReadToEndAsync(ct);

        if (string.IsNullOrWhiteSpace(content))
            return new ExtractedDocument([]);

        var section = new ExtractedSection(Text: content.Trim());
        return new ExtractedDocument([section]);
    }
}
