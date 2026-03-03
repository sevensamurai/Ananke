using Shouldly;

namespace Ananke.Documents.Tests;

[TestFixture]
public class PlainTextExtractorTests
{
    private readonly PlainTextExtractor _extractor = new();

    // ── CanExtract ───────────────────────────────────────────────────

    [Test]
    public void CanExtract_TxtExtension_ReturnsTrue()
    {
        _extractor.CanExtract(".txt").ShouldBeTrue();
    }

    [Test]
    public void CanExtract_TextExtension_ReturnsTrue()
    {
        _extractor.CanExtract(".text").ShouldBeTrue();
    }

    [Test]
    public void CanExtract_CaseInsensitive()
    {
        _extractor.CanExtract(".TXT").ShouldBeTrue();
    }

    [Test]
    public void CanExtract_PdfExtension_ReturnsFalse()
    {
        _extractor.CanExtract(".pdf").ShouldBeFalse();
    }

    [Test]
    public void CanExtract_MdExtension_ReturnsFalse()
    {
        _extractor.CanExtract(".md").ShouldBeFalse();
    }

    // ── ExtractAsync: contract ───────────────────────────────────────

    [Test]
    public async Task ExtractAsync_NullStream_Throws()
    {
        await Should.ThrowAsync<ArgumentNullException>(async () =>
            await _extractor.ExtractAsync(null!));
    }

    // ── ExtractAsync: content ────────────────────────────────────────

    [Test]
    public async Task ExtractAsync_SimpleText_ExtractsContent()
    {
        using var stream = ToStream("Hello, world!");
        var result = await _extractor.ExtractAsync(stream);

        result.Sections.Count.ShouldBe(1);
        result.Sections[0].Text.ShouldBe("Hello, world!");
    }

    [Test]
    public async Task ExtractAsync_MultiLineText_PreservesLines()
    {
        var text = "Line one.\nLine two.\nLine three.";
        using var stream = ToStream(text);
        var result = await _extractor.ExtractAsync(stream);

        result.Sections.Count.ShouldBe(1);
        result.Sections[0].Text.ShouldContain("Line one.");
        result.Sections[0].Text.ShouldContain("Line two.");
        result.Sections[0].Text.ShouldContain("Line three.");
    }

    [Test]
    public async Task ExtractAsync_EmptyStream_ReturnsEmptyDocument()
    {
        using var stream = ToStream("");
        var result = await _extractor.ExtractAsync(stream);

        result.Sections.Count.ShouldBe(0);
    }

    [Test]
    public async Task ExtractAsync_WhitespaceOnly_ReturnsEmptyDocument()
    {
        using var stream = ToStream("   \n  \n  ");
        var result = await _extractor.ExtractAsync(stream);

        result.Sections.Count.ShouldBe(0);
    }

    [Test]
    public async Task ExtractAsync_TrimsWhitespace()
    {
        using var stream = ToStream("  some text  \n  ");
        var result = await _extractor.ExtractAsync(stream);

        result.Sections.Count.ShouldBe(1);
        result.Sections[0].Text.ShouldBe("some text");
    }

    [Test]
    public async Task ExtractAsync_NoMetadata()
    {
        using var stream = ToStream("Just text.");
        var result = await _extractor.ExtractAsync(stream);

        result.Metadata.ShouldBeNull();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static MemoryStream ToStream(string text) =>
        new(System.Text.Encoding.UTF8.GetBytes(text));
}
