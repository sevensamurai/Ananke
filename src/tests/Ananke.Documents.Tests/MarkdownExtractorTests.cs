using Ananke.Orchestration.Knowledge;
using Shouldly;

namespace Ananke.Documents.Tests;

[TestFixture]
public class MarkdownExtractorTests
{
    private readonly MarkdownExtractor _extractor = new();

    // ── CanExtract ───────────────────────────────────────────────────

    [Test]
    public void CanExtract_MdExtension_ReturnsTrue()
    {
        _extractor.CanExtract(".md").ShouldBeTrue();
    }

    [Test]
    public void CanExtract_MarkdownExtension_ReturnsTrue()
    {
        _extractor.CanExtract(".markdown").ShouldBeTrue();
    }

    [Test]
    public void CanExtract_CaseInsensitive()
    {
        _extractor.CanExtract(".MD").ShouldBeTrue();
    }

    [Test]
    public void CanExtract_PdfExtension_ReturnsFalse()
    {
        _extractor.CanExtract(".pdf").ShouldBeFalse();
    }

    [Test]
    public void CanExtract_TxtExtension_ReturnsFalse()
    {
        _extractor.CanExtract(".txt").ShouldBeFalse();
    }

    // ── ExtractFromString: empty / whitespace ────────────────────────

    [Test]
    public void ExtractFromString_EmptyString_ReturnsEmptyDocument()
    {
        var result = _extractor.ExtractFromString("");

        result.Sections.Count.ShouldBe(0);
    }

    [Test]
    public void ExtractFromString_WhitespaceOnly_ReturnsEmptyDocument()
    {
        var result = _extractor.ExtractFromString("   \n  \n  ");

        result.Sections.Count.ShouldBe(0);
    }

    [Test]
    public void ExtractFromString_Null_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            _extractor.ExtractFromString(null!));
    }

    // ── ExtractFromString: simple paragraph ──────────────────────────

    [Test]
    public void ExtractFromString_SingleParagraph_OneSectionNoTitle()
    {
        var result = _extractor.ExtractFromString("Hello world.");

        result.Sections.Count.ShouldBe(1);
        result.Sections[0].Text.ShouldBe("Hello world.");
        result.Sections[0].SectionTitle.ShouldBeNull();
    }

    [Test]
    public void ExtractFromString_MultipleParagraphs_OneSectionWhenNoHeadings()
    {
        var md = """
            First paragraph.

            Second paragraph.
            """;

        var result = _extractor.ExtractFromString(md);

        result.Sections.Count.ShouldBe(1);
        result.Sections[0].Text.ShouldContain("First paragraph.");
        result.Sections[0].Text.ShouldContain("Second paragraph.");
    }

    // ── ExtractFromString: headings → section splits ─────────────────

    [Test]
    public void ExtractFromString_SingleHeading_OneSectionWithTitle()
    {
        var md = """
            # Introduction

            Some content here.
            """;

        var result = _extractor.ExtractFromString(md);

        result.Sections.Count.ShouldBe(1);
        result.Sections[0].SectionTitle.ShouldBe("Introduction");
        result.Sections[0].Text.ShouldContain("Some content here.");
    }

    [Test]
    public void ExtractFromString_MultipleHeadings_SplitsIntoSections()
    {
        var md = """
            # First

            Content A.

            ## Second

            Content B.
            """;

        var result = _extractor.ExtractFromString(md);

        result.Sections.Count.ShouldBe(2);
        result.Sections[0].SectionTitle.ShouldBe("First");
        result.Sections[0].Text.ShouldContain("Content A.");
        result.Sections[1].SectionTitle.ShouldBe("Second");
        result.Sections[1].Text.ShouldContain("Content B.");
    }

    [Test]
    public void ExtractFromString_ContentBeforeFirstHeading_SeparateSection()
    {
        var md = """
            Preamble text.

            # Chapter 1

            Chapter content.
            """;

        var result = _extractor.ExtractFromString(md);

        result.Sections.Count.ShouldBe(2);
        result.Sections[0].SectionTitle.ShouldBeNull();
        result.Sections[0].Text.ShouldContain("Preamble text.");
        result.Sections[1].SectionTitle.ShouldBe("Chapter 1");
    }

    [Test]
    public void ExtractFromString_HeadingOnly_OneSectionWithTitle()
    {
        var result = _extractor.ExtractFromString("# Title Only");

        result.Sections.Count.ShouldBe(1);
        result.Sections[0].SectionTitle.ShouldBe("Title Only");
    }

    // ── ExtractFromString: links ─────────────────────────────────────

    [Test]
    public void ExtractFromString_InlineLink_ExtractedAsStructuredData()
    {
        var md = "Check [Ananke docs](https://example.com/docs) for details.";

        var result = _extractor.ExtractFromString(md);

        result.Sections.Count.ShouldBe(1);
        result.Sections[0].Links.ShouldNotBeNull();
        result.Sections[0].Links!.Count.ShouldBe(1);
        result.Sections[0].Links![0].Text.ShouldBe("Ananke docs");
        result.Sections[0].Links![0].Uri.ShouldBe("https://example.com/docs");
    }

    [Test]
    public void ExtractFromString_MultipleLinks_AllExtracted()
    {
        var md = "[A](https://a.com) and [B](https://b.com)";

        var result = _extractor.ExtractFromString(md);

        result.Sections[0].Links!.Count.ShouldBe(2);
    }

    [Test]
    public void ExtractFromString_NoLinks_LinksIsNull()
    {
        var result = _extractor.ExtractFromString("No links here.");

        result.Sections[0].Links.ShouldBeNull();
    }

    // ── ExtractFromString: images ────────────────────────────────────

    [Test]
    public void ExtractFromString_Image_ExtractedAsStructuredData()
    {
        var md = "![diagram](https://example.com/arch.png)";

        var result = _extractor.ExtractFromString(md);

        result.Sections.Count.ShouldBe(1);
        result.Sections[0].Images.ShouldNotBeNull();
        result.Sections[0].Images!.Count.ShouldBe(1);
        result.Sections[0].Images![0].Reference.ShouldBe("https://example.com/arch.png");
    }

    [Test]
    public void ExtractFromString_NoImages_ImagesIsNull()
    {
        var result = _extractor.ExtractFromString("Just text.");

        result.Sections[0].Images.ShouldBeNull();
    }

    // ── ExtractFromString: document metadata ─────────────────────────

    [Test]
    public void ExtractFromString_H1Title_ExtractedAsMetadata()
    {
        var md = """
            # My Document Title

            Content here.
            """;

        var result = _extractor.ExtractFromString(md);

        result.Metadata.ShouldNotBeNull();
        result.Metadata!["title"].ShouldBe("My Document Title");
    }

    [Test]
    public void ExtractFromString_NoH1_NoTitleMetadata()
    {
        var md = """
            ## Not a top-level heading

            Content here.
            """;

        var result = _extractor.ExtractFromString(md);

        // Metadata is null when empty
        if (result.Metadata is not null)
            result.Metadata.ShouldNotContainKey("title");
    }

    [Test]
    public void ExtractFromString_H1AfterContent_NoTitleMetadata()
    {
        var md = """
            Some preamble.

            # Late Title

            Content.
            """;

        var result = _extractor.ExtractFromString(md);

        // H1 after non-heading content is not treated as document title
        if (result.Metadata is not null)
            result.Metadata.ShouldNotContainKey("title");
    }

    // ── ExtractFromString: nested / complex structures ───────────────

    [Test]
    public void ExtractFromString_BoldAndItalic_PreservedInText()
    {
        var md = "This is **bold** and *italic*.";

        var result = _extractor.ExtractFromString(md);

        result.Sections[0].Text.ShouldContain("**bold**");
        result.Sections[0].Text.ShouldContain("*italic*");
    }

    [Test]
    public void ExtractFromString_CodeBlock_PreservedInText()
    {
        var md = """
            # Code Example

            ```csharp
            var x = 42;
            ```
            """;

        var result = _extractor.ExtractFromString(md);

        result.Sections[0].Text.ShouldContain("var x = 42;");
    }

    [Test]
    public void ExtractFromString_LinksInHeading_SectionTitleIsPlainText()
    {
        var md = "# [Linked Title](https://example.com)";

        var result = _extractor.ExtractFromString(md);

        result.Sections[0].SectionTitle.ShouldBe("Linked Title");
    }

    [Test]
    public void ExtractFromString_NestedList_PreservedInText()
    {
        var md = """
            - Item 1
              - Sub-item A
              - Sub-item B
            - Item 2
            """;

        var result = _extractor.ExtractFromString(md);

        result.Sections[0].Text.ShouldContain("Item 1");
        result.Sections[0].Text.ShouldContain("Sub-item A");
    }

    [Test]
    public void ExtractFromString_LinkInsideListItem_Extracted()
    {
        var md = "- See [docs](https://example.com)";

        var result = _extractor.ExtractFromString(md);

        result.Sections[0].Links.ShouldNotBeNull();
        result.Sections[0].Links!.Count.ShouldBe(1);
        result.Sections[0].Links![0].Uri.ShouldBe("https://example.com");
    }

    // ── ExtractAsync: stream overload ────────────────────────────────

    [Test]
    public async Task ExtractAsync_FromStream_MatchesStringOverload()
    {
        var md = """
            # Title

            Content with [link](https://example.com).
            """;

        var fromString = _extractor.ExtractFromString(md);

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(md));
        var fromStream = await _extractor.ExtractAsync(stream);

        fromStream.Sections.Count.ShouldBe(fromString.Sections.Count);
        fromStream.Sections[0].SectionTitle.ShouldBe(fromString.Sections[0].SectionTitle);
        fromStream.Sections[0].Text.ShouldBe(fromString.Sections[0].Text);
    }

    [Test]
    public async Task ExtractAsync_NullStream_Throws()
    {
        await Should.ThrowAsync<ArgumentNullException>(async () =>
            await _extractor.ExtractAsync(null!));
    }
}
