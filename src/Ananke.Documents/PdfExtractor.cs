using System.Text;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Documents;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace Ananke.Documents;

/// <summary>
/// PDF document extractor. Produces Markdown-formatted output by analyzing word-level
/// font sizes to detect headings and grouping words into lines and paragraphs based
/// on spatial positioning.
/// </summary>
/// <remarks>
/// Currently powered by PdfPig. The implementation library is an internal detail —
/// consuming code depends only on <see cref="IDocumentExtractor"/>.
/// </remarks>
public sealed class PdfExtractor : IDocumentExtractor
{
    private const double ParagraphGapMultiplier = 2.5;
    private const double HeadingFontSizeMultiplier = 1.15;

    /// <inheritdoc />
    public bool CanExtract(string fileExtension) =>
        string.Equals(fileExtension, ".pdf", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<ExtractedDocument> ExtractAsync(
        Stream data, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        // PdfPig requires a seekable stream
        using var memoryStream = new MemoryStream();
        await data.CopyToAsync(memoryStream, ct);
        memoryStream.Position = 0;

        using var document = PdfDocument.Open(memoryStream);

        var sections = new List<ExtractedSection>();
        var documentMetadata = new Dictionary<string, string>
        {
            ["page_count"] = document.NumberOfPages.ToString()
        };

        if (document.Information?.Title is { Length: > 0 } title)
            documentMetadata["title"] = title;
        if (document.Information?.Author is { Length: > 0 } author)
            documentMetadata["author"] = author;

        foreach (var page in document.GetPages())
        {
            ct.ThrowIfCancellationRequested();

            var result = ConvertPageToMarkdown(page);
            if (string.IsNullOrWhiteSpace(result.Markdown))
                continue;

            sections.Add(new ExtractedSection(
                Text: result.Markdown,
                Page: page.Number,
                Links: result.Links.Count > 0 ? result.Links : null,
                Images: result.Images.Count > 0 ? result.Images : null));
        }

        return new ExtractedDocument(sections, documentMetadata);
    }

    private static PageResult ConvertPageToMarkdown(Page page)
    {
        var emptyResult = new PageResult(string.Empty, [], []);

        var words = page.GetWords().ToList();
        if (words.Count == 0)
            return emptyResult;

        var lines = GroupWordsIntoLines(words);
        if (lines.Count == 0)
            return emptyResult;

        var hyperlinks = BuildHyperlinkLookup(page);
        var emittedLinks = new List<ExtractedLink>();
        var bodyFontSize = DetectBodyFontSize(lines);
        var averageLineHeight = DetectAverageLineHeight(lines);

        var sb = new StringBuilder();
        double? previousLineBottom = null;

        foreach (var line in lines)
        {
            var gap = previousLineBottom.HasValue
                ? previousLineBottom.Value - line.Top
                : 0;

            // Detect paragraph break from vertical gap
            if (previousLineBottom.HasValue && gap > averageLineHeight * ParagraphGapMultiplier)
                sb.AppendLine();

            var lineText = BuildLineText(line, hyperlinks, emittedLinks);
            var lineFontSize = line.FontSize;

            if (lineFontSize > bodyFontSize * HeadingFontSizeMultiplier)
            {
                // Determine heading level by font size ratio
                var ratio = lineFontSize / bodyFontSize;
                var headingLevel = ratio > 1.6 ? 1 : ratio > 1.3 ? 2 : 3;
                var prefix = new string('#', headingLevel);

                if (sb.Length > 0 && sb[^1] != '\n')
                    sb.AppendLine();
                sb.AppendLine();
                sb.Append(prefix).Append(' ').AppendLine(lineText);
                sb.AppendLine();
            }
            else
            {
                if (line.IsBold && !IsAllBold(lines, bodyFontSize))
                    sb.AppendLine($"**{lineText}**");
                else
                    sb.AppendLine(lineText);
            }

            previousLineBottom = line.Bottom;
        }

        // Extract images and append Markdown placeholders
        var extractedImages = ExtractImages(page, sb);

        return new PageResult(sb.ToString().Trim(), emittedLinks, extractedImages);
    }

    private static string BuildLineText(
        TextLine line, List<HyperlinkRegion> hyperlinks, List<ExtractedLink> emittedLinks)
    {
        if (hyperlinks.Count == 0)
            return string.Join(" ", line.Words.Select(w => w.Text));

        var parts = new List<string>();
        var i = 0;

        while (i < line.Words.Count)
        {
            var word = line.Words[i];
            var link = FindHyperlink(word, hyperlinks);

            if (link is null)
            {
                parts.Add(word.Text);
                i++;
                continue;
            }

            // Collect consecutive words within the same hyperlink region
            var linkWords = new List<string> { word.Text };
            i++;
            while (i < line.Words.Count && IsWithinBounds(line.Words[i], link.Bounds))
            {
                linkWords.Add(line.Words[i].Text);
                i++;
            }

            var linkText = string.Join(" ", linkWords);
            parts.Add($"[{linkText}]({link.Uri})");
            emittedLinks.Add(new ExtractedLink(linkText, link.Uri));
        }

        return string.Join(" ", parts);
    }

    private static List<ExtractedImage> ExtractImages(Page page, StringBuilder sb)
    {
        var images = page.GetImages().ToList();
        if (images.Count == 0)
            return [];

        var result = new List<ExtractedImage>();
        sb.AppendLine();

        for (var i = 0; i < images.Count; i++)
        {
            var image = images[i];
            var width = image.WidthInSamples;
            var height = image.HeightInSamples;
            var reference = $"embedded:page{page.Number}:{i}";

            sb.AppendLine($"![image ({width}×{height})]({reference})");

            ReadOnlyMemory<byte>? pngData = null;
            if (image.TryGetPng(out var pngBytes))
                pngData = pngBytes;

            result.Add(new ExtractedImage(reference, width, height, pngData));
        }

        return result;
    }

    private static List<HyperlinkRegion> BuildHyperlinkLookup(Page page)
    {
        try
        {
            return page.GetHyperlinks()
                .Where(h => !string.IsNullOrEmpty(h.Uri))
                .Select(h => new HyperlinkRegion(h.Uri!, h.Bounds))
                .ToList();
        }
        catch
        {
            // Some PDFs have malformed annotation data
            return [];
        }
    }

    private static HyperlinkRegion? FindHyperlink(Word word, List<HyperlinkRegion> hyperlinks)
    {
        foreach (var link in hyperlinks)
        {
            if (IsWithinBounds(word, link.Bounds))
                return link;
        }
        return null;
    }

    private static bool IsWithinBounds(Word word, PdfRectangle bounds)
    {
        var center = word.BoundingBox.Left + (word.BoundingBox.Right - word.BoundingBox.Left) / 2;
        var middle = word.BoundingBox.Bottom + (word.BoundingBox.Top - word.BoundingBox.Bottom) / 2;
        return center >= bounds.Left && center <= bounds.Right
            && middle >= bounds.Bottom && middle <= bounds.Top;
    }

    private static List<TextLine> GroupWordsIntoLines(List<Word> words)
    {
        if (words.Count == 0) return [];

        // Sort by vertical position (top to bottom), then left to right
        var sorted = words
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .ThenBy(w => w.BoundingBox.Left)
            .ToList();

        var lines = new List<TextLine>();
        var currentLineWords = new List<Word> { sorted[0] };
        var currentBaseline = sorted[0].BoundingBox.Bottom;
        var currentFontSize = GetFontSize(sorted[0]);

        for (var i = 1; i < sorted.Count; i++)
        {
            var word = sorted[i];
            var wordBaseline = word.BoundingBox.Bottom;
            var fontSize = GetFontSize(word);
            var tolerance = Math.Max(fontSize, currentFontSize) * 0.5;

            if (Math.Abs(wordBaseline - currentBaseline) <= tolerance)
            {
                currentLineWords.Add(word);
            }
            else
            {
                lines.Add(CreateTextLine(currentLineWords));
                currentLineWords = [word];
                currentBaseline = wordBaseline;
                currentFontSize = fontSize;
            }
        }

        if (currentLineWords.Count > 0)
            lines.Add(CreateTextLine(currentLineWords));

        return lines;
    }

    private static TextLine CreateTextLine(List<Word> words)
    {
        var sorted = words.OrderBy(w => w.BoundingBox.Left).ToList();
        var fontSize = sorted.Average(w => GetFontSize(w));
        var top = sorted.Max(w => w.BoundingBox.Top);
        var bottom = sorted.Min(w => w.BoundingBox.Bottom);
        var isBold = sorted.All(w =>
            w.FontName?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true);

        return new TextLine(sorted, fontSize, top, bottom, isBold);
    }

    private static double DetectBodyFontSize(List<TextLine> lines)
    {
        // The body font size is the most frequently occurring font size
        var sizeCounts = new Dictionary<int, int>();
        foreach (var line in lines)
        {
            var rounded = (int)Math.Round(line.FontSize);
            sizeCounts.TryGetValue(rounded, out var count);
            sizeCounts[rounded] = count + 1;
        }

        return sizeCounts.OrderByDescending(kv => kv.Value).First().Key;
    }

    private static double DetectAverageLineHeight(List<TextLine> lines)
    {
        if (lines.Count < 2) return 12;

        var gaps = new List<double>();
        for (var i = 1; i < lines.Count; i++)
        {
            var gap = lines[i - 1].Bottom - lines[i].Top;
            if (gap > 0)
                gaps.Add(gap);
        }

        return gaps.Count > 0 ? gaps.Average() : 12;
    }

    private static bool IsAllBold(List<TextLine> lines, double bodyFontSize) =>
        lines.Count(l => l.IsBold && Math.Abs(l.FontSize - bodyFontSize) < 1) > lines.Count * 0.8;

    private static double GetFontSize(Word word) =>
        word.Letters.Count > 0 ? word.Letters[0].FontSize : 12;

    private sealed record TextLine(
        List<Word> Words,
        double FontSize,
        double Top,
        double Bottom,
        bool IsBold);

    private sealed record HyperlinkRegion(string Uri, PdfRectangle Bounds);

    private sealed record PageResult(
        string Markdown,
        List<ExtractedLink> Links,
        List<ExtractedImage> Images);
}
