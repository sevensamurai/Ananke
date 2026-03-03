namespace Ananke.Orchestration.Knowledge;

/// <summary>
/// Default <see cref="IDocumentChunker"/> that splits Markdown text using a sliding window.
/// Prefers Markdown heading boundaries (<c># ...</c>) as natural break points, falling back
/// to paragraph boundaries within heading sections. This produces chunks that align with
/// the document's logical structure.
/// </summary>
public sealed class SlidingWindowChunker : IDocumentChunker
{
    /// <inheritdoc />
    public IReadOnlyList<DocumentChunk> Chunk(ExtractedDocument document, ChunkingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= new ChunkingOptions();

        var maxChars = options.MaxTokens * 4;
        var overlapChars = options.OverlapTokens * 4;
        var chunks = new List<DocumentChunk>();

        foreach (var section in document.Sections)
        {
            if (string.IsNullOrWhiteSpace(section.Text))
                continue;

            var sectionMetadata = BuildSectionMetadata(section);

            // Split by Markdown headings first, then by paragraphs within each block
            var headingBlocks = SplitByHeadings(section.Text);

            foreach (var block in headingBlocks)
            {
                var paragraphs = SplitParagraphs(block);
                ChunkParagraphs(paragraphs, maxChars, overlapChars, sectionMetadata, chunks);
            }
        }

        return chunks;
    }

    /// <summary>
    /// Splits Markdown text into blocks at heading boundaries. Each heading and the
    /// content below it form one block. Content before the first heading (if any) is
    /// its own block.
    /// </summary>
    private static List<string> SplitByHeadings(string text)
    {
        var blocks = new List<string>();
        var lines = text.Split('\n');
        var current = new List<string>();

        foreach (var line in lines)
        {
            if (IsMarkdownHeading(line) && current.Count > 0)
            {
                var blockText = string.Join("\n", current).Trim();
                if (blockText.Length > 0)
                    blocks.Add(blockText);
                current = [];
            }

            current.Add(line);
        }

        if (current.Count > 0)
        {
            var blockText = string.Join("\n", current).Trim();
            if (blockText.Length > 0)
                blocks.Add(blockText);
        }

        // If no headings were found, return the entire text as one block
        if (blocks.Count == 0 && text.Trim().Length > 0)
            blocks.Add(text.Trim());

        return blocks;
    }

    private static bool IsMarkdownHeading(string line) =>
        line.Length > 2 && line[0] == '#' && (line[1] == ' ' || line[1] == '#');

    private static void ChunkParagraphs(
        List<string> paragraphs,
        int maxChars,
        int overlapChars,
        Dictionary<string, string> metadata,
        List<DocumentChunk> chunks)
    {
        if (paragraphs.Count == 0) return;

        var currentChunk = new List<string>();
        var currentLength = 0;

        foreach (var para in paragraphs)
        {
            var paraLength = para.Length;

            if (currentLength + paraLength > maxChars && currentChunk.Count > 0)
            {
                chunks.Add(CreateChunk(currentChunk, metadata));

                var overlap = BuildOverlap(currentChunk, overlapChars);
                currentChunk = [];
                currentLength = 0;

                if (overlap.Length > 0)
                {
                    currentChunk.Add(overlap);
                    currentLength = overlap.Length;
                }
            }

            currentChunk.Add(para);
            currentLength += paraLength;
        }

        if (currentChunk.Count > 0)
            chunks.Add(CreateChunk(currentChunk, metadata));
    }

    private static DocumentChunk CreateChunk(
        List<string> paragraphs, Dictionary<string, string> metadata) =>
        new(string.Join("\n\n", paragraphs).Trim(), metadata);

    private static string BuildOverlap(List<string> paragraphs, int overlapChars)
    {
        if (overlapChars <= 0) return string.Empty;

        var totalLength = 0;
        var startIndex = paragraphs.Count;

        for (var i = paragraphs.Count - 1; i >= 0; i--)
        {
            totalLength += paragraphs[i].Length;
            startIndex = i;
            if (totalLength >= overlapChars)
                break;
        }

        return string.Join("\n\n", paragraphs.Skip(startIndex));
    }

    private static List<string> SplitParagraphs(string text)
    {
        var parts = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>(parts.Length);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
                result.Add(trimmed);
        }

        return result;
    }

    private static Dictionary<string, string> BuildSectionMetadata(ExtractedSection section)
    {
        var metadata = new Dictionary<string, string>();

        if (section.Page.HasValue)
            metadata["page"] = section.Page.Value.ToString();
        if (section.SectionTitle is not null)
            metadata["section"] = section.SectionTitle;
        if (section.ContentType != "text")
            metadata["content_type"] = section.ContentType;

        return metadata;
    }
}
