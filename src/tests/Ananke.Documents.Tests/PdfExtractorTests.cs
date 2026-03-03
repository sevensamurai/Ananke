using Shouldly;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Ananke.Documents.Tests;

[TestFixture]
public class PdfExtractorTests
{
    private readonly PdfExtractor _extractor = new();

    // ── CanExtract ───────────────────────────────────────────────────

    [Test]
    public void CanExtract_PdfExtension_ReturnsTrue()
    {
        _extractor.CanExtract(".pdf").ShouldBeTrue();
    }

    [Test]
    public void CanExtract_CaseInsensitive()
    {
        _extractor.CanExtract(".PDF").ShouldBeTrue();
    }

    [Test]
    public void CanExtract_MdExtension_ReturnsFalse()
    {
        _extractor.CanExtract(".md").ShouldBeFalse();
    }

    [Test]
    public void CanExtract_TxtExtension_ReturnsFalse()
    {
        _extractor.CanExtract(".txt").ShouldBeFalse();
    }

    // ── ExtractAsync: contract ───────────────────────────────────────

    [Test]
    public async Task ExtractAsync_NullStream_Throws()
    {
        await Should.ThrowAsync<ArgumentNullException>(async () =>
            await _extractor.ExtractAsync(null!));
    }

    // ── ExtractAsync: minimal PDF ────────────────────────────────────

    [Test]
    public async Task ExtractAsync_MinimalPdfWithText_ExtractsContent()
    {
        var pdfBytes = BuildMinimalPdf("Hello from PdfPig!");

        using var stream = new MemoryStream(pdfBytes);
        var result = await _extractor.ExtractAsync(stream);

        result.Sections.Count.ShouldBeGreaterThanOrEqualTo(1);
        result.Sections[0].Text.ShouldContain("Hello from PdfPig");
        result.Sections[0].Page.ShouldBe(1);
    }

    [Test]
    public async Task ExtractAsync_MinimalPdf_HasPageCountMetadata()
    {
        var pdfBytes = BuildMinimalPdf("Test content.");

        using var stream = new MemoryStream(pdfBytes);
        var result = await _extractor.ExtractAsync(stream);

        result.Metadata.ShouldNotBeNull();
        result.Metadata!["page_count"].ShouldBe("1");
    }

    [Test]
    public async Task ExtractAsync_MultiPagePdf_ExtractsAllPages()
    {
        var pdfBytes = BuildMultiPagePdf("Page one content.", "Page two content.");

        using var stream = new MemoryStream(pdfBytes);
        var result = await _extractor.ExtractAsync(stream);

        result.Sections.Count.ShouldBe(2);
        result.Sections[0].Text.ShouldContain("Page one");
        result.Sections[0].Page.ShouldBe(1);
        result.Sections[1].Text.ShouldContain("Page two");
        result.Sections[1].Page.ShouldBe(2);

        result.Metadata!["page_count"].ShouldBe("2");
    }

    [Test]
    public async Task ExtractAsync_EmptyPage_SkipsEmptySection()
    {
        // A PDF with no text content on its single page
        var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.A4);
        var pdfBytes = builder.Build();

        using var stream = new MemoryStream(pdfBytes);
        var result = await _extractor.ExtractAsync(stream);

        result.Sections.Count.ShouldBe(0);
        result.Metadata!["page_count"].ShouldBe("1");
    }

    [Test]
    public async Task ExtractAsync_NonSeekableStream_StillWorks()
    {
        var pdfBytes = BuildMinimalPdf("Non-seekable test.");

        // Wrap in a non-seekable stream to verify PdfExtractor copies to MemoryStream
        using var inner = new MemoryStream(pdfBytes);
        using var nonSeekable = new NonSeekableStream(inner);
        var result = await _extractor.ExtractAsync(nonSeekable);

        result.Sections.Count.ShouldBeGreaterThanOrEqualTo(1);
        result.Sections[0].Text.ShouldContain("Non-seekable test");
    }

    [Test]
    public async Task ExtractAsync_LargerText_DetectsBodyFontSize()
    {
        // Multiple lines of text to exercise the font size detection and line grouping logic
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        page.AddText("Line one of body text.", 12, new PdfPoint(50, 700), font);
        page.AddText("Line two of body text.", 12, new PdfPoint(50, 680), font);
        page.AddText("Line three of body text.", 12, new PdfPoint(50, 660), font);

        var pdfBytes = builder.Build();

        using var stream = new MemoryStream(pdfBytes);
        var result = await _extractor.ExtractAsync(stream);

        result.Sections.Count.ShouldBeGreaterThanOrEqualTo(1);
        var text = result.Sections[0].Text;
        text.ShouldContain("Line one");
        text.ShouldContain("Line two");
        text.ShouldContain("Line three");
    }

    [Test]
    public async Task ExtractAsync_HeadingFontSize_DetectedAsMarkdownHeading()
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        // Large text → heading, small text → body
        page.AddText("Big Heading", 24, new PdfPoint(50, 750), font);
        page.AddText("Normal body text.", 12, new PdfPoint(50, 700), font);
        page.AddText("More body text.", 12, new PdfPoint(50, 680), font);

        var pdfBytes = builder.Build();

        using var stream = new MemoryStream(pdfBytes);
        var result = await _extractor.ExtractAsync(stream);

        result.Sections.Count.ShouldBeGreaterThanOrEqualTo(1);
        var text = result.Sections[0].Text;
        // Heading should be prefixed with # in Markdown
        text.ShouldContain("# ");
        text.ShouldContain("Big Heading");
        text.ShouldContain("Normal body text.");
    }

    [Test]
    public async Task ExtractAsync_ParagraphGap_InsertsBlankLine()
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        // Two groups of text separated by a large vertical gap
        page.AddText("Paragraph one.", 12, new PdfPoint(50, 700), font);
        page.AddText("Paragraph two.", 12, new PdfPoint(50, 600), font); // big gap

        var pdfBytes = builder.Build();

        using var stream = new MemoryStream(pdfBytes);
        var result = await _extractor.ExtractAsync(stream);

        result.Sections.Count.ShouldBeGreaterThanOrEqualTo(1);
        // The two paragraphs should be separated by a blank line in the output
        var text = result.Sections[0].Text;
        text.ShouldContain("Paragraph one.");
        text.ShouldContain("Paragraph two.");
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static byte[] BuildMinimalPdf(string text)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText(text, 12, new PdfPoint(50, 700), font);
        return builder.Build();
    }

    private static byte[] BuildMultiPagePdf(params string[] pageTexts)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (var text in pageTexts)
        {
            var page = builder.AddPage(PageSize.A4);
            page.AddText(text, 12, new PdfPoint(50, 700), font);
        }

        return builder.Build();
    }

    /// <summary>
    /// Wraps a stream to remove seek capability, verifying that PdfExtractor
    /// handles non-seekable input streams correctly.
    /// </summary>
    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            inner.ReadAsync(buffer, offset, count, ct);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct) =>
            inner.ReadAsync(buffer, ct);
    }
}
