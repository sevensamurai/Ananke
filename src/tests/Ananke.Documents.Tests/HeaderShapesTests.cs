using Ananke.Orchestration.Knowledge.Documents;
using Shouldly;

#pragma warning disable ANNKEEXP004

namespace Ananke.Documents.Tests;

/// <summary>
/// Header shapes other than the product's own, as the note on other document sets found them: YAML front matter (MADR,
/// Docusaurus), an RFC-822 field block (PEPs), a status section and a plain date line (Nygard ADRs), <c>**Key**:</c>
/// (meeting notes), and dates past ISO.
/// </summary>
[TestFixture]
public class HeaderShapesTests
{
    private static ExtractedDocument Markdown(string text) => new MarkdownExtractor().ExtractFromString(text);

    private const string Madr = """
        ---
        status: superseded by 0009-use-junit-5
        date: 2024-03-02
        deciders:
          - Ana
          - Bo
        ---
        # Use plain JUnit

        ## Context and Problem Statement

        Which test framework?
        """;

    private const string Docusaurus = """
        ---
        id: adrs-adr001
        title: 'ADR001: Architecture Decision Record (ADR) log'
        ---

        ## Decision

        A decision was made to store ADRs in a log.
        """;

    private const string Pep = """
        PEP: 8
        Title: Style Guide for Python Code
        Author: Guido van Rossum <guido@python.org>,
                Barry Warsaw <barry@python.org>
        Status: Active
        Created: 05-Jul-2001
        Superseded-By: 9999

        Introduction
        ============

        This document gives coding conventions.
        """;

    private const string Nygard = """
        # 2. Implement as shell scripts

        Date: 2016-02-12

        ## Status

        Superseded by [5. Use Go](0005-use-go.md)

        ## Context

        ADRs are plain text files.
        """;

    [Test]
    public void ExtractFromString_FrontMatter_IsMetadataNotASection()
    {
        var document = Markdown(Madr);

        document.Sections[0].SectionTitle.ShouldBe("Use plain JUnit");
        document.Sections.ShouldAllBe(s => !s.Text.Contains("status:"));
        document.Metadata!["status"].ShouldBe("superseded by 0009-use-junit-5");
        document.Metadata["date"].ShouldBe("2024-03-02");
        document.Metadata["deciders"].ShouldBe("Ana, Bo");
        document.Metadata["title"].ShouldBe("Use plain JUnit");
    }

    [Test]
    public void ExtractFromString_FrontMatterTitle_IsTheTitleWhenThereIsNoHeading()
    {
        var document = Markdown(Docusaurus);

        document.Metadata!["title"].ShouldBe("ADR001: Architecture Decision Record (ADR) log");
        document.Sections[0].SectionTitle.ShouldBe("Decision");
    }

    [Test]
    public void FromFrontMatter_ReadsItsFields_WithoutCaringForCase()
    {
        var fields = DocumentHeaders.FromFrontMatter(Markdown(Madr));

        fields["Status"].ShouldBe("superseded by 0009-use-junit-5");
        fields["DATE"].ShouldBe("2024-03-02");
        fields["title"].ShouldBe("Use plain JUnit");
    }

    [Test]
    public void FromFieldBlock_ReadsAnRfc822BlockWithContinuationLines()
    {
        var fields = DocumentHeaders.FromFieldBlock(new ExtractedDocument([new ExtractedSection(Pep.Trim())]));

        fields["Title"].ShouldBe("Style Guide for Python Code");
        fields["Status"].ShouldBe("Active");
        fields["Created"].ShouldBe("05-Jul-2001");
        fields["Superseded-By"].ShouldBe("9999");
        fields["Author"].ShouldBe("Guido van Rossum <guido@python.org>, Barry Warsaw <barry@python.org>");
        fields.ShouldNotContainKey("Introduction");
    }

    [Test]
    public void FromStatusSection_ReadsTheStatusSectionAndADateLine()
    {
        var fields = DocumentHeaders.FromStatusSection(Markdown(Nygard));

        fields["Title"].ShouldBe("2. Implement as shell scripts");
        fields["Status"].ShouldBe("Superseded by [5. Use Go](0005-use-go.md)");
        fields["Date"].ShouldBe("2016-02-12");
    }

    [Test]
    public void FromMarkdown_ReadsBoldThenColon()
    {
        var fields = DocumentHeaders.FromMarkdown(Markdown("# Meeting\n\n* **Recording**: <https://example.org/r>\n* **Minutes**: notes"));

        fields["Recording"].ShouldBe("<https://example.org/r>");
        fields["Minutes"].ShouldBe("notes");
    }

    [Test]
    public void FromMarkdown_FieldsInTheSecondSection_AreRead()
    {
        var fields = DocumentHeaders.FromMarkdown(Markdown("""
            # Meeting 2025-10-15

            ## Links

            * **Recording**: <https://example.org/r>
            * **Minutes**: notes

            ## Agenda

            * **Announcements**: none
            """));

        fields["Title"].ShouldBe("Meeting 2025-10-15");
        fields["Recording"].ShouldBe("<https://example.org/r>");
        fields["Minutes"].ShouldBe("notes");
        fields.ShouldNotContainKey("Announcements");
    }

    [Test]
    public void Combine_TakesEachFieldFromTheFirstReaderThatHasIt()
    {
        var read = DocumentHeaders.Combine(DocumentHeaders.FromFrontMatter, DocumentHeaders.FromStatusSection);

        var fields = read(Markdown(Madr));

        fields["Status"].ShouldBe("superseded by 0009-use-junit-5");
        fields["Title"].ShouldBe("Use plain JUnit");
    }

    [TestCase("2026-11-02", 2026, 11, 2)]
    [TestCase("2019-07-1", 2019, 7, 1)]
    [TestCase("05-Jul-2001", 2001, 7, 5)]
    [TestCase("5 July 2001", 2001, 7, 5)]
    [TestCase("July 5, 2001", 2001, 7, 5)]
    [TestCase("Sept. 3, 2020", 2020, 9, 3)]
    [TestCase("June 2022", 2022, 6, 1)]
    [TestCase("Superseded 2026-11-02 by 0005", 2026, 11, 2)]
    public void DateIn_ReadsTheShapesDocumentsUse(string text, int year, int month, int day) =>
        DocumentHeaders.DateIn(text).ShouldBe(new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero));

    [TestCase("yyyy-mm-dd")]
    [TestCase("2026-13-40")]
    [TestCase("no date here")]
    public void DateIn_NoDate_IsNull(string text) => DocumentHeaders.DateIn(text).ShouldBeNull();

    [Test]
    public void FromMarkdown_APathologicalParagraph_ReturnsWithinItsTimeLimit()
    {
        // A long first section of bold labels with no colons, as an extractor could hand it over: each label is tried
        // as a field, and the reading must give up within its time limit rather than hold up ingestion. (Markdig itself
        // refuses Markdown nested this deeply, so the section is built directly.)
        var text = string.Concat(Enumerable.Repeat("**a ", 200_000));
        var document = new ExtractedDocument([new ExtractedSection(text, SectionTitle: "T")]);

        var read = Task.Run(() => DocumentHeaders.FromMarkdown(document));

        read.Wait(TimeSpan.FromSeconds(20)).ShouldBeTrue();
        read.Result.ShouldContainKey("Title");
    }
}
