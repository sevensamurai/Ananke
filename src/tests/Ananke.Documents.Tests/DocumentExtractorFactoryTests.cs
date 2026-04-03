using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Documents;
using Shouldly;

namespace Ananke.Documents.Tests;

[TestFixture]
public class DocumentExtractorFactoryTests
{
    private readonly DocumentExtractorFactory _factory = new();

    // ── Built-in extractors ──────────────────────────────────────────

    [Test]
    public void GetExtractor_PdfExtension_ReturnsPdfExtractor()
    {
        _factory.GetExtractor(".pdf").ShouldBeOfType<PdfExtractor>();
    }

    [Test]
    public void GetExtractor_MdExtension_ReturnsMarkdownExtractor()
    {
        _factory.GetExtractor(".md").ShouldBeOfType<MarkdownExtractor>();
    }

    [Test]
    public void GetExtractor_MarkdownExtension_ReturnsMarkdownExtractor()
    {
        _factory.GetExtractor(".markdown").ShouldBeOfType<MarkdownExtractor>();
    }

    [Test]
    public void GetExtractor_TxtExtension_ReturnsPlainTextExtractor()
    {
        _factory.GetExtractor(".txt").ShouldBeOfType<PlainTextExtractor>();
    }

    [Test]
    public void GetExtractor_TextExtension_ReturnsPlainTextExtractor()
    {
        _factory.GetExtractor(".text").ShouldBeOfType<PlainTextExtractor>();
    }

    [Test]
    public void GetExtractor_CaseInsensitive()
    {
        _factory.GetExtractor(".PDF").ShouldBeOfType<PdfExtractor>();
        _factory.GetExtractor(".TXT").ShouldBeOfType<PlainTextExtractor>();
    }

    // ── Unknown extensions ───────────────────────────────────────────

    [Test]
    public void GetExtractor_UnknownExtension_ReturnsNull()
    {
        _factory.GetExtractor(".xyz").ShouldBeNull();
    }

    [Test]
    public void CanExtract_KnownExtension_ReturnsTrue()
    {
        _factory.CanExtract(".txt").ShouldBeTrue();
    }

    [Test]
    public void CanExtract_UnknownExtension_ReturnsFalse()
    {
        _factory.CanExtract(".mp4").ShouldBeFalse();
    }

    // ── GetExtractorForFile ──────────────────────────────────────────

    [Test]
    public void GetExtractorForFile_MdFile_ReturnsMarkdownExtractor()
    {
        _factory.GetExtractorForFile("README.md").ShouldBeOfType<MarkdownExtractor>();
    }

    [Test]
    public void GetExtractorForFile_PdfPath_ReturnsPdfExtractor()
    {
        _factory.GetExtractorForFile("/docs/report.pdf").ShouldBeOfType<PdfExtractor>();
    }

    [Test]
    public void GetExtractorForFile_TxtFile_ReturnsPlainTextExtractor()
    {
        _factory.GetExtractorForFile("data.txt").ShouldBeOfType<PlainTextExtractor>();
    }

    [Test]
    public void GetExtractorForFile_HttpUrl_ReturnsCorrectExtractor()
    {
        _factory.GetExtractorForFile("https://example.com/docs/guide.md")
            .ShouldBeOfType<MarkdownExtractor>();
    }

    [Test]
    public void GetExtractorForFile_UrlWithQueryString_ReturnsCorrectExtractor()
    {
        _factory.GetExtractorForFile("https://example.com/guide.md?token=abc")
            .ShouldBeOfType<MarkdownExtractor>();
    }

    [Test]
    public void GetExtractorForFile_NoExtension_ReturnsNull()
    {
        _factory.GetExtractorForFile("Makefile").ShouldBeNull();
    }

    [Test]
    public void GetExtractorForFile_UnknownExtension_ReturnsNull()
    {
        _factory.GetExtractorForFile("script.py").ShouldBeNull();
    }

    // ── Custom extractors ────────────────────────────────────────────

    [Test]
    public void CustomExtractor_TakesPrecedenceOverBuiltIn()
    {
        var custom = new StubExtractor(".txt");
        var factory = new DocumentExtractorFactory([custom]);

        factory.GetExtractor(".txt").ShouldBe(custom);
    }

    [Test]
    public void CustomExtractor_NewExtension_IsResolvable()
    {
        var custom = new StubExtractor(".html");
        var factory = new DocumentExtractorFactory([custom]);

        factory.GetExtractor(".html").ShouldBe(custom);
        // Built-ins still work
        factory.GetExtractor(".pdf").ShouldBeOfType<PdfExtractor>();
    }

    [Test]
    public void Extractors_ContainsAllRegistered()
    {
        _factory.Extractors.Count.ShouldBe(3);
    }

    [Test]
    public void GetExtractor_NullExtension_Throws()
    {
        Should.Throw<ArgumentNullException>(() => _factory.GetExtractor(null!));
    }

    // ── Stub ─────────────────────────────────────────────────────────

    private sealed class StubExtractor(string extension) : IDocumentExtractor
    {
        public bool CanExtract(string fileExtension) =>
            string.Equals(fileExtension, extension, StringComparison.OrdinalIgnoreCase);

        public Task<ExtractedDocument> ExtractAsync(
            Stream data, CancellationToken ct = default) =>
            Task.FromResult(new ExtractedDocument([]));
    }
}
