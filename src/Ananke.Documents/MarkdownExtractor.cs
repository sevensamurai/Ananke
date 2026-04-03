using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Documents;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Ananke.Documents;

/// <summary>
/// Markdown document extractor powered by Markdig. Parses the Markdown AST to split
/// content into sections at heading boundaries and extracts inline links and images
/// into structured metadata.
/// </summary>
/// <remarks>
/// <para>
/// Since Markdown is already the canonical internal representation for the knowledge pipeline,
/// this extractor performs structural parsing rather than format conversion. It splits the
/// document at heading boundaries so the chunker receives well-delineated sections with
/// proper <see cref="ExtractedSection.SectionTitle"/> metadata.
/// </para>
/// <para>
/// Supports file extensions <c>.md</c> and <c>.markdown</c>.
/// </para>
/// </remarks>
public sealed class MarkdownExtractor : IDocumentExtractor
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    /// <inheritdoc />
    public bool CanExtract(string fileExtension) =>
        string.Equals(fileExtension, ".md", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fileExtension, ".markdown", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<ExtractedDocument> ExtractAsync(
        Stream data, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        using var reader = new StreamReader(data, leaveOpen: true);
        var content = await reader.ReadToEndAsync(ct);

        return ExtractFromString(content);
    }

    /// <summary>
    /// Extracts structured content directly from a Markdown string.
    /// Convenience overload for scenarios where the content is already in memory
    /// (tests, inline templates, etc.).
    /// </summary>
    public ExtractedDocument ExtractFromString(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (string.IsNullOrWhiteSpace(content))
            return new ExtractedDocument([]);

        var document = Markdown.Parse(content, Pipeline);

        var sections = BuildSections(document, content);
        var metadata = ExtractDocumentMetadata(document);

        return new ExtractedDocument(sections, metadata.Count > 0 ? metadata : null);
    }

    /// <summary>
    /// Walks the Markdig AST and groups top-level blocks into sections split at headings.
    /// Each heading and the blocks below it (up to the next heading) form one section.
    /// The original Markdown source is preserved as the section text.
    /// </summary>
    private static List<ExtractedSection> BuildSections(MarkdownDocument document, string source)
    {
        var sections = new List<ExtractedSection>();
        var currentBlocks = new List<Block>();
        string? currentTitle = null;

        foreach (var block in document)
        {
            // Skip phantom blocks with empty spans (e.g. LinkReferenceDefinitionGroup)
            if (block.Span.Length <= 0)
                continue;

            if (block is HeadingBlock heading)
            {
                if (currentBlocks.Count > 0)
                    AddSection(sections, currentBlocks, currentTitle, source);

                currentBlocks = [block];
                currentTitle = heading.Inline is not null
                    ? ExtractPlainText(heading.Inline)
                    : null;
                continue;
            }

            currentBlocks.Add(block);
        }

        if (currentBlocks.Count > 0)
            AddSection(sections, currentBlocks, currentTitle, source);

        return sections;
    }

    private static void AddSection(
        List<ExtractedSection> sections,
        List<Block> blocks,
        string? sectionTitle,
        string source)
    {
        var start = blocks.Min(b => b.Span.Start);
        var end = blocks.Max(b => b.Span.End);

        if (start < 0 || end < start || end >= source.Length)
            return;

        var text = source[start..(end + 1)].Trim();

        if (text.Length == 0) return;

        var links = new List<ExtractedLink>();
        var images = new List<ExtractedImage>();

        foreach (var block in blocks)
            CollectInlines(block, links, images);

        sections.Add(new ExtractedSection(
            Text: text,
            SectionTitle: sectionTitle,
            Links: links.Count > 0 ? links : null,
            Images: images.Count > 0 ? images : null));
    }

    /// <summary>
    /// Recursively walks all inline elements in a block to collect links and images.
    /// </summary>
    private static void CollectInlines(Block block, List<ExtractedLink> links, List<ExtractedImage> images)
    {
        if (block is LeafBlock leaf && leaf.Inline is not null)
        {
            foreach (var inline in leaf.Inline)
                ProcessInline(inline, links, images);
        }

        if (block is ContainerBlock container)
        {
            foreach (var child in container)
            {
                if (child is not null)
                    CollectInlines(child, links, images);
            }
        }
    }

    private static void ProcessInline(
        Inline inline, List<ExtractedLink> links, List<ExtractedImage> images)
    {
        switch (inline)
        {
            case LinkInline { IsImage: true } image:
                images.Add(new ExtractedImage(
                    Reference: image.Url ?? string.Empty,
                    Width: 0,
                    Height: 0));
                break;

            case LinkInline link when link.Url is { Length: > 0 }:
                links.Add(new ExtractedLink(
                    Text: ExtractPlainText(link),
                    Uri: link.Url));
                break;
        }

        if (inline is ContainerInline container)
        {
            foreach (var child in container)
                ProcessInline(child, links, images);
        }
    }

    private static string ExtractPlainText(ContainerInline container)
    {
        var parts = new List<string>();
        foreach (var inline in container)
        {
            if (inline is LiteralInline literal)
                parts.Add(literal.Content.ToString());
            else if (inline is ContainerInline nested)
                parts.Add(ExtractPlainText(nested));
        }
        return string.Join("", parts);
    }

    private static Dictionary<string, string> ExtractDocumentMetadata(MarkdownDocument document)
    {
        var metadata = new Dictionary<string, string>();

        foreach (var block in document)
        {
            if (block is HeadingBlock { Level: 1 } heading && heading.Inline is not null)
            {
                metadata["title"] = ExtractPlainText(heading.Inline);
                break;
            }

            // Stop at the first non-heading content block
            if (block is not HeadingBlock)
                break;
        }

        return metadata;
    }
}
