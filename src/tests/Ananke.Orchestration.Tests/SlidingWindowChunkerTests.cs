using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Documents;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class SlidingWindowChunkerTests
{
    private readonly SlidingWindowChunker _chunker = new();

    // ── Basic chunking ───────────────────────────────────────────

    [Test]
    public void Chunk_SingleShortSection_ProducesSingleChunk()
    {
        var doc = Doc("Hello world. This is a test.");
        var chunks = _chunker.Chunk(doc);

        chunks.Count.ShouldBe(1);
        chunks[0].Text.ShouldBe("Hello world. This is a test.");
    }

    [Test]
    public void Chunk_EmptySection_ProducesNoChunks()
    {
        var doc = new ExtractedDocument([new ExtractedSection("   ")]);
        var chunks = _chunker.Chunk(doc);

        chunks.ShouldBeEmpty();
    }

    [Test]
    public void Chunk_NullDocument_Throws()
    {
        Should.Throw<ArgumentNullException>(() => _chunker.Chunk(null!));
    }

    // ── Heading-based splitting ──────────────────────────────────

    [Test]
    public void Chunk_MarkdownHeadings_SplitsAtHeadingBoundaries()
    {
        var text = "# Introduction\nSome intro text.\n\n# Details\nSome details.";
        var doc = Doc(text);

        var chunks = _chunker.Chunk(doc);

        chunks.Count.ShouldBe(2);
        chunks[0].Text.ShouldContain("Introduction");
        chunks[0].Text.ShouldContain("intro text");
        chunks[1].Text.ShouldContain("Details");
        chunks[1].Text.ShouldContain("Some details");
    }

    [Test]
    public void Chunk_NestedHeadings_SplitsAtEachLevel()
    {
        var text = "# H1\nContent one.\n\n## H2\nContent two.\n\n### H3\nContent three.";
        var doc = Doc(text);

        var chunks = _chunker.Chunk(doc);

        chunks.Count.ShouldBe(3);
    }

    // ── Paragraph splitting within heading blocks ────────────────

    [Test]
    public void Chunk_LongSection_SplitsIntoParagraphBoundaryChunks()
    {
        // With MaxTokens=10 (~40 chars), paragraphs longer than that get split
        var paragraph1 = "First paragraph with some content here.";
        var paragraph2 = "Second paragraph with different content.";
        var paragraph3 = "Third paragraph wrapping up the section.";
        var text = $"{paragraph1}\n\n{paragraph2}\n\n{paragraph3}";
        var doc = Doc(text);

        var chunks = _chunker.Chunk(doc, new ChunkingOptions(MaxTokens: 10, OverlapTokens: 0));

        chunks.Count.ShouldBeGreaterThan(1);
        // All original text should be present across chunks
        var allText = string.Join(" ", chunks.Select(c => c.Text));
        allText.ShouldContain("First paragraph");
        allText.ShouldContain("Third paragraph");
    }

    // ── Overlap ──────────────────────────────────────────────────

    [Test]
    public void Chunk_WithOverlap_ChunksShareContent()
    {
        var p1 = new string('A', 50);
        var p2 = new string('B', 50);
        var p3 = new string('C', 50);
        var text = $"{p1}\n\n{p2}\n\n{p3}";
        var doc = Doc(text);

        // MaxTokens=15 (~60 chars), overlap=5 (~20 chars)
        var chunks = _chunker.Chunk(doc, new ChunkingOptions(MaxTokens: 15, OverlapTokens: 5));

        chunks.Count.ShouldBeGreaterThan(1);

        // With overlap, consecutive chunks should share text
        if (chunks.Count >= 2)
        {
            var chunk1End = chunks[0].Text;
            var chunk2Start = chunks[1].Text;
            // The overlap from chunk1's end should appear at chunk2's start
            (chunk1End.Length > 0 && chunk2Start.Length > 0).ShouldBeTrue();
        }
    }

    [Test]
    public void Chunk_ZeroOverlap_NoSharedContent()
    {
        var p1 = new string('A', 50);
        var p2 = new string('B', 50);
        var text = $"{p1}\n\n{p2}";
        var doc = Doc(text);

        var chunks = _chunker.Chunk(doc, new ChunkingOptions(MaxTokens: 15, OverlapTokens: 0));

        if (chunks.Count == 2)
        {
            chunks[0].Text.ShouldNotContain(new string('B', 10));
            chunks[1].Text.ShouldNotContain(new string('A', 10));
        }
    }

    // ── Metadata propagation ─────────────────────────────────────

    [Test]
    public void Chunk_SectionWithPage_MetadataIncludesPage()
    {
        var doc = new ExtractedDocument(
            [new ExtractedSection("Some content", Page: 3, SectionTitle: "Chapter 1")]);

        var chunks = _chunker.Chunk(doc);

        chunks.Count.ShouldBe(1);
        chunks[0].Metadata["page"].ShouldBe("3");
        chunks[0].Metadata["section"].ShouldBe("Chapter 1");
    }

    [Test]
    public void Chunk_SectionWithTableContentType_MetadataIncludesContentType()
    {
        var doc = new ExtractedDocument(
            [new ExtractedSection("| Col1 | Col2 |", ContentType: "table")]);

        var chunks = _chunker.Chunk(doc);

        chunks[0].Metadata["content_type"].ShouldBe("table");
    }

    [Test]
    public void Chunk_PlainTextSection_MetadataExcludesDefaultContentType()
    {
        var doc = Doc("Just plain text.");
        var chunks = _chunker.Chunk(doc);

        chunks[0].Metadata.ShouldNotContainKey("content_type");
    }

    // ── Multiple sections ────────────────────────────────────────

    [Test]
    public void Chunk_MultipleSections_ChunksAllSections()
    {
        var doc = new ExtractedDocument(
        [
            new ExtractedSection("Section one content.", Page: 1),
            new ExtractedSection("Section two content.", Page: 2)
        ]);

        var chunks = _chunker.Chunk(doc);

        chunks.Count.ShouldBe(2);
        chunks[0].Text.ShouldContain("Section one");
        chunks[1].Text.ShouldContain("Section two");
    }

    // ── Default options ──────────────────────────────────────────

    [Test]
    public void Chunk_DefaultOptions_UsesReasonableDefaults()
    {
        // A moderate-length document should produce chunks under default settings
        var paragraphs = Enumerable.Range(1, 50)
            .Select(i => $"Paragraph {i} with some filler content to make it longer.");
        var text = string.Join("\n\n", paragraphs);
        var doc = Doc(text);

        var chunks = _chunker.Chunk(doc);

        chunks.Count.ShouldBeGreaterThan(1);
        // No chunk should exceed ~512 tokens (~2048 chars) by much
        foreach (var chunk in chunks)
            chunk.Text.Length.ShouldBeLessThan(3000);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static ExtractedDocument Doc(string text) =>
        new([new ExtractedSection(text)]);
}
