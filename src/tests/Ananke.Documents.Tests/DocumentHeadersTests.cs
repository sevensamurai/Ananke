using System.Text.RegularExpressions;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Documents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

#pragma warning disable ANNKEEXP004

namespace Ananke.Documents.Tests;

/// <summary>Reading a document's header: <see cref="DocumentHeaders.FromMarkdown"/>, and <see cref="DocumentProcessor.Headers"/>.</summary>
[TestFixture]
public class DocumentHeadersTests
{
    private const string Design = """
        # 0005 – The knowledge base

        **Status:** Proposed. D1 and D2 decided 2026-11-02: the common code ships in
        Ananke · **Date:** 2026-11-02

        ## Summary

        Text of the summary. **Note:** not a header field.
        """;

    /// <summary>A keyword query every chunk of <see cref="Design"/> matches, with or without its header.</summary>
    private const string EveryChunk = "0005 2026-11-02 summary";

    private static ExtractedDocument Extract(string markdown) => new MarkdownExtractor().ExtractFromString(markdown);

    [Test]
    public void FromMarkdown_ReadsTheTitleAndTheBoldFieldsOfTheFirstSection()
    {
        var fields = DocumentHeaders.FromMarkdown(Extract(Design));

        fields["Title"].ShouldBe("0005 – The knowledge base");
        fields["Status"].ShouldBe("Proposed. D1 and D2 decided 2026-11-02: the common code ships in Ananke");
        fields["Date"].ShouldBe("2026-11-02");
        fields.ShouldNotContainKey("Note");
    }

    [Test]
    public void FromMarkdown_NoHeader_ReadsOnlyTheTitle()
    {
        var fields = DocumentHeaders.FromMarkdown(Extract("# Notes\n\nJust text."));

        fields.Keys.ShouldBe(["Title"]);
    }

    [Test]
    public async Task ProcessAsync_WithHeaders_PutsTheFieldsOnEveryChunkAndInItsMetadata()
    {
        var store = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        var processor = new DocumentProcessor(new HttpClient(), [new MarkdownExtractor()], new SlidingWindowChunker(), store)
        {
            Headers = new DocumentHeaderOptions
            {
                Read = DocumentHeaders.FromMarkdown,
                Fields = ["Title", "Status", "Date"],
                DateField = "Date",
            },
        };

        await processor.ProcessAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Design)), ".md", "0005.md");

        var chunks = await store.SearchAsync(EveryChunk, new SearchOptions { Mode = SearchMode.Sparse, TopK = 10 });
        chunks.ShouldNotBeEmpty();
        chunks.ShouldAllBe(c => c.Text.StartsWith("[Title: 0005 – The knowledge base · Status: Proposed."));
        chunks.ShouldAllBe(c => c.Text.Contains("· Date: 2026-11-02]\n"));
        chunks.ShouldAllBe(c => c.Metadata["Status"].StartsWith("Proposed."));
        chunks.ShouldAllBe(c => c.Metadata[DocumentHeaderOptions.EffectiveAtKey] == "2026-11-02T00:00:00.0000000+00:00");
    }

    [Test]
    public async Task ProcessAsync_WithHeaders_PutsOnlyTheNamedFieldsOnTheChunk()
    {
        var store = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        var processor = new DocumentProcessor(new HttpClient(), [new MarkdownExtractor()], new SlidingWindowChunker(), store)
        {
            Headers = new DocumentHeaderOptions { Read = DocumentHeaders.FromMarkdown, Fields = ["Date"] },
        };

        await processor.ProcessAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Design)), ".md", "0005.md");

        var chunks = await store.SearchAsync(EveryChunk, new SearchOptions { Mode = SearchMode.Sparse, TopK = 10 });
        chunks.ShouldAllBe(c => c.Text.StartsWith("[Date: 2026-11-02]\n"));
        chunks.ShouldAllBe(c => !c.Metadata.ContainsKey(DocumentHeaderOptions.EffectiveAtKey));
    }

    [Test]
    public async Task ProcessAsync_SupersededBy_IsWrittenOnEveryChunk()
    {
        var store = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        var processor = new DocumentProcessor(new HttpClient(), [new MarkdownExtractor()], new SlidingWindowChunker(), store)
        {
            Headers = new DocumentHeaderOptions
            {
                Read = DocumentHeaders.FromMarkdown,
                SupersededBy = fields => fields.TryGetValue("Status", out var status) && status.StartsWith("Proposed") ? ["0006.md"] : null,
            },
        };

        await processor.ProcessAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Design)), ".md", "0005.md");

        var chunks = await store.SearchAsync(EveryChunk, new SearchOptions { Mode = SearchMode.Sparse, TopK = 10 });
        chunks.ShouldAllBe(c => c.Metadata[DocumentHeaderOptions.SupersededByKey] == "0006.md");
    }

    [Test]
    public void Find_EachMatch_IsALinkNamedForIt()
    {
        var links = DocumentLinks.Find(new Dictionary<string, Regex> { ["card"] = new(@"\b[A-Z]+-\d+\b") }, "ZEN-6 builds on ZEN-12, and ZEN-6 again.");

        links.ShouldBe(new Dictionary<string, string> { ["entity:card:ZEN-6"] = "card:ZEN-6", ["entity:card:ZEN-12"] = "card:ZEN-12" }, ignoreOrder: true);
    }

    [Test]
    public void Find_APatternWithAKeyGroup_LinksTheGroup()
    {
        var links = DocumentLinks.Find(new Dictionary<string, Regex> { ["document"] = new(@"see \[(?<key>[^\]]+)\]") }, "As decided, see [0002-bridge.md].");

        links.ShouldBe(new Dictionary<string, string> { ["entity:document:0002-bridge.md"] = "document:0002-bridge.md" });
    }

    [Test]
    public async Task ProcessAsync_LinkPatterns_WriteEachChunksMatchesAsLinks()
    {
        var store = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        var processor = new DocumentProcessor(new HttpClient(), [new MarkdownExtractor()], new SlidingWindowChunker(), store)
        {
            LinkPatterns = new Dictionary<string, Regex> { ["card"] = new(@"\b[A-Z]+-\d+\b") },
        };
        const string doc = "# Plan\n\nCarried out by ZEN-6 and ZEN-12.\n\n## Later\n\nNothing linked here.";

        await processor.ProcessAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(doc)), ".md", "plan.md");

        var linked = await store.SearchAsync("plan later carried", new SearchOptions
        {
            Mode = SearchMode.Sparse,
            TopK = 10,
            Filter = new KnowledgeFilter { ["entity:card:ZEN-6"] = "card:ZEN-6" },
        });
        linked.ShouldNotBeEmpty();
        linked.ShouldAllBe(c => c.Metadata["entity:card:ZEN-12"] == "card:ZEN-12");
    }

    [Test]
    public async Task ProcessAsync_WithoutHeaders_LeavesChunksAsTheyAre()
    {
        var store = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        var processor = new DocumentProcessor(new HttpClient(), [new MarkdownExtractor()], new SlidingWindowChunker(), store);

        await processor.ProcessAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Design)), ".md", "0005.md");

        var chunks = await store.SearchAsync(EveryChunk, new SearchOptions { Mode = SearchMode.Sparse, TopK = 10 });
        chunks.ShouldAllBe(c => !c.Text.StartsWith('['));
        chunks.ShouldAllBe(c => !c.Metadata.ContainsKey("Status"));
    }
}
