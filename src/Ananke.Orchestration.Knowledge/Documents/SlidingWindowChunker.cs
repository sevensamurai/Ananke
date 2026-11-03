namespace Ananke.Orchestration.Knowledge.Documents;

/// <summary>
/// Default <see cref="IDocumentChunker"/> that splits Markdown text using a sliding window.
/// Prefers Markdown heading boundaries (<c># ...</c>) as natural break points, falling back
/// to paragraph boundaries within heading sections. This produces chunks that align with
/// the document's logical structure. A paragraph longer than the window is split at sentence ends, and a sentence
/// longer than the window at spaces, so no chunk is longer than the window and an embedder reads all of it.
/// </summary>
public sealed partial class SlidingWindowChunker : IDocumentChunker
{
    /// <inheritdoc />
    public IReadOnlyList<DocumentChunk> Chunk(ExtractedDocument document, ChunkingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= new ChunkingOptions();

        var maxChars = options.MaxTokens * 4;
        // The overlap is at most a quarter of the window, so a chunk with it still fits the window.
        var overlapChars = Math.Min(options.OverlapTokens * 4, maxChars / 4);
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
        var onlyOverlap = false;

        var pieceChars = maxChars - overlapChars;
        foreach (var para in paragraphs.SelectMany(p => p.Length > pieceChars ? SplitLong(p, pieceChars) : [p]))
        {
            // The blank line that joins it to what is already in the chunk counts too.
            var paraLength = para.Length + (currentChunk.Count > 0 ? 2 : 0);

            if (currentLength + paraLength > maxChars && currentChunk.Count > 0)
            {
                // A chunk holding only the previous chunk's tail would repeat it; the tail is dropped instead.
                if (!onlyOverlap)
                    chunks.Add(CreateChunk(currentChunk, metadata));

                var overlap = onlyOverlap ? string.Empty : BuildOverlap(currentChunk, overlapChars);
                currentChunk = [];
                currentLength = 0;
                onlyOverlap = false;

                paraLength = para.Length;
                if (overlap.Length > 0 && overlap.Length + 2 + paraLength <= maxChars)
                {
                    currentChunk.Add(overlap);
                    currentLength = overlap.Length;
                    paraLength += 2;
                    onlyOverlap = true;
                }
            }

            currentChunk.Add(para);
            currentLength += paraLength;
            onlyOverlap = false;
        }

        if (currentChunk.Count > 0)
            chunks.Add(CreateChunk(currentChunk, metadata));
    }

    private static DocumentChunk CreateChunk(
        List<string> paragraphs, Dictionary<string, string> metadata) =>
        new(string.Join("\n\n", paragraphs).Trim(), metadata);

    /// <summary>The previous chunk's last <paramref name="overlapChars"/> characters at most, from the start of a word.</summary>
    private static string BuildOverlap(List<string> paragraphs, int overlapChars)
    {
        if (overlapChars <= 0) return string.Empty;

        var text = string.Join("\n\n", paragraphs);
        if (text.Length <= overlapChars)
            return text;

        var start = text.Length - overlapChars;
        var space = text.IndexOfAny([' ', '\n'], start);
        return (space < 0 ? text[start..] : text[(space + 1)..]).Trim();
    }

    /// <summary>
    /// A paragraph longer than <paramref name="maxChars"/> as pieces of whole sentences, each at most that long; a
    /// sentence longer than that is cut at spaces, or at the limit when it has none.
    /// </summary>
    private static IEnumerable<string> SplitLong(string paragraph, int maxChars)
    {
        string[] sentences;
        try
        {
            sentences = SentenceEnd().Split(paragraph);
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            sentences = [paragraph];
        }

        var piece = new System.Text.StringBuilder();
        foreach (var sentence in sentences.SelectMany(s => s.Length > maxChars ? SplitAtSpaces(s, maxChars) : [s]))
        {
            if (piece.Length > 0 && piece.Length + 1 + sentence.Length > maxChars)
            {
                yield return piece.ToString();
                piece.Clear();
            }
            if (piece.Length > 0)
                piece.Append(' ');
            piece.Append(sentence);
        }
        if (piece.Length > 0)
            yield return piece.ToString();
    }

    private static IEnumerable<string> SplitAtSpaces(string text, int maxChars)
    {
        var start = 0;
        while (start < text.Length)
        {
            var end = Math.Min(start + maxChars, text.Length);
            if (end < text.Length)
            {
                var space = text.LastIndexOf(' ', end, end - start);
                if (space > start)
                    end = space;
            }
            yield return text[start..end].Trim();
            start = end;
            while (start < text.Length && text[start] == ' ')
                start++;
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(?<=[.!?…][""'”’)\]]?)\s+(?=\S)", System.Text.RegularExpressions.RegexOptions.None, matchTimeoutMilliseconds: 250)]
    private static partial System.Text.RegularExpressions.Regex SentenceEnd();

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
