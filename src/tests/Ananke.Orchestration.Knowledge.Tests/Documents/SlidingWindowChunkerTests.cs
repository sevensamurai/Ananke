using Ananke.Orchestration.Knowledge.Documents;
using Shouldly;

namespace Ananke.Orchestration.Knowledge.Tests.Documents;

[TestFixture]
public class SlidingWindowChunkerTests
{
    private readonly SlidingWindowChunker _chunker = new();

    [Test]
    public void Chunk_NullDocument_Throws() =>
        Should.Throw<ArgumentNullException>(() => _chunker.Chunk(null!));

    [Test]
    public void Chunk_EmptyDocument_ReturnsNoChunks()
    {
        var document = new ExtractedDocument([]);

        var chunks = _chunker.Chunk(document);

        chunks.ShouldBeEmpty();
    }

    [Test]
    public void Chunk_WhitespaceOnlySection_IsSkipped()
    {
        var document = new ExtractedDocument([new ExtractedSection("   \n\t  ")]);

        var chunks = _chunker.Chunk(document);

        chunks.ShouldBeEmpty();
    }

    [Test]
    public void Chunk_TextShorterThanOneWindow_ReturnsSingleChunk()
    {
        var document = new ExtractedDocument([new ExtractedSection("A short paragraph of text.")]);

        var chunks = _chunker.Chunk(document, new ChunkingOptions(MaxTokens: 512, OverlapTokens: 64));

        chunks.Count.ShouldBe(1);
        chunks[0].Text.ShouldBe("A short paragraph of text.");
    }

    [Test]
    public void Chunk_TextLongerThanWindow_SplitsIntoMultipleChunks()
    {
        // MaxTokens=10 -> maxChars=40. Each paragraph is well under that, but together they overflow.
        var paragraphs = Enumerable.Range(0, 5).Select(i => new string('a', 30) + i).ToArray();
        var text = string.Join("\n\n", paragraphs);
        var document = new ExtractedDocument([new ExtractedSection(text)]);

        var chunks = _chunker.Chunk(document, new ChunkingOptions(MaxTokens: 10, OverlapTokens: 0));

        chunks.Count.ShouldBeGreaterThan(1);
    }

    [Test]
    public void Chunk_AParagraphLongerThanTheWindow_IsSplitAtSentenceEnds()
    {
        // MaxTokens=100 -> maxChars=400; one paragraph of 30 sentences of about 60 characters each.
        var sentences = Enumerable.Range(0, 30).Select(i => $"Sentence number {i:D2} says something about the topic at hand.").ToList();
        var document = new ExtractedDocument([new ExtractedSection(string.Join(" ", sentences))]);

        var chunks = _chunker.Chunk(document, new ChunkingOptions(MaxTokens: 100, OverlapTokens: 16));

        chunks.Count.ShouldBeGreaterThan(4);
        chunks.ShouldAllBe(c => c.Text.Length <= 400);
        chunks.ShouldAllBe(c => c.Text.EndsWith('.'));
        sentences.ShouldAllBe(sentence => chunks.Any(c => c.Text.Contains(sentence)));
    }

    [Test]
    public void Chunk_ASentenceLongerThanTheWindow_IsSplitAtSpaces()
    {
        var words = Enumerable.Range(0, 300).Select(i => $"word{i}").ToList();
        var document = new ExtractedDocument([new ExtractedSection(string.Join(" ", words))]);

        var chunks = _chunker.Chunk(document, new ChunkingOptions(MaxTokens: 50, OverlapTokens: 0));

        chunks.ShouldAllBe(c => c.Text.Length <= 200);
        words.ShouldAllBe(word => chunks.Any(c => c.Text.Split(' ', StringSplitOptions.None).Contains(word)));
    }

    [Test]
    public void Chunk_OverlapLargerThanWindow_DoesNotThrowAndStillProgresses()
    {
        var paragraphs = Enumerable.Range(0, 4).Select(i => new string('b', 20) + i).ToArray();
        var text = string.Join("\n\n", paragraphs);
        var document = new ExtractedDocument([new ExtractedSection(text)]);

        // OverlapTokens far exceeds MaxTokens (overlapChars > maxChars), and each paragraph is longer than the window.
        var chunks = _chunker.Chunk(document, new ChunkingOptions(MaxTokens: 5, OverlapTokens: 1000));

        chunks.ShouldNotBeEmpty();
        chunks.ShouldAllBe(c => c.Text.Length <= 20);
        // Nothing silently dropped: every character of every paragraph is still in the output.
        chunks.Sum(c => c.Text.Count(ch => ch == 'b')).ShouldBeGreaterThanOrEqualTo(paragraphs.Sum(p => p.Count(ch => ch == 'b')));
        for (var i = 0; i < paragraphs.Length; i++)
            chunks.Any(c => c.Text.Contains(i.ToString())).ShouldBeTrue();
    }

    [Test]
    public void Chunk_ConsecutiveChunks_OverlapContainsTailOfPreviousChunk()
    {
        var paragraphs = new[] { "alpha beta gamma delta epsilon", "zeta eta theta iota kappa", "lambda mu nu xi omicron pi" };
        var text = string.Join("\n\n", paragraphs);
        var document = new ExtractedDocument([new ExtractedSection(text)]);

        // maxChars=60, overlapChars=12 (a quarter of the window at most).
        var chunks = _chunker.Chunk(document, new ChunkingOptions(MaxTokens: 15, OverlapTokens: 3));

        chunks.Count.ShouldBeGreaterThan(1);
        chunks.ShouldAllBe(c => c.Text.Length <= 60);
        // Chunk 2 starts with the tail of chunk 1, from the start of a word.
        chunks[1].Text.ShouldStartWith("iota kappa\n\nlambda");
    }

    [Test]
    public void Chunk_MarkdownHeadings_ProduceSeparateBlocksWithinSameSection()
    {
        var text = "# Heading One\nBody one.\n\n# Heading Two\nBody two.";
        var document = new ExtractedDocument([new ExtractedSection(text)]);

        var chunks = _chunker.Chunk(document, new ChunkingOptions(MaxTokens: 512, OverlapTokens: 0));

        chunks.Count.ShouldBe(2);
        chunks[0].Text.ShouldContain("Heading One");
        chunks[1].Text.ShouldContain("Heading Two");
    }

    [Test]
    public void Chunk_SectionMetadata_IsCarriedOntoChunks()
    {
        var section = new ExtractedSection("Some text.", Page: 3, SectionTitle: "Intro", ContentType: "table");
        var document = new ExtractedDocument([section]);

        var chunks = _chunker.Chunk(document);

        chunks.Count.ShouldBe(1);
        chunks[0].Metadata["page"].ShouldBe("3");
        chunks[0].Metadata["section"].ShouldBe("Intro");
        chunks[0].Metadata["content_type"].ShouldBe("table");
    }

    [Test]
    public void Chunk_DefaultContentType_IsNotAddedToMetadata()
    {
        var document = new ExtractedDocument([new ExtractedSection("Text.", ContentType: "text")]);

        var chunks = _chunker.Chunk(document);

        chunks[0].Metadata.ShouldNotContainKey("content_type");
    }

    [Test]
    public void Chunk_MultipleSections_AreChunkedIndependently()
    {
        var document = new ExtractedDocument(
        [
            new ExtractedSection("First section text.", Page: 1),
            new ExtractedSection("Second section text.", Page: 2)
        ]);

        var chunks = _chunker.Chunk(document);

        chunks.Count.ShouldBe(2);
        chunks[0].Metadata["page"].ShouldBe("1");
        chunks[1].Metadata["page"].ShouldBe("2");
    }

    [Test]
    public void Chunk_DefaultOptions_AreUsedWhenNoneProvided()
    {
        var document = new ExtractedDocument([new ExtractedSection("Plain text with no options passed.")]);

        var chunks = _chunker.Chunk(document);

        chunks.Count.ShouldBe(1);
    }
}
