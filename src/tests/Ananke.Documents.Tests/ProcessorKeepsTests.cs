using System.Text;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Documents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

#pragma warning disable ANNKEEXP004

namespace Ananke.Documents.Tests;

/// <summary>
/// What <see cref="DocumentProcessor"/> keeps: header fields only when named, tags filling the fields a document doesn't
/// give, sections dropped by heading, and text cleaned before chunking.
/// </summary>
[TestFixture]
public class ProcessorKeepsTests
{
    private const string Pep = """
        PEP: 8
        Title: Style Guide for Python Code
        Author: Guido van Rossum <guido@python.org>
        Status: Active
        Created: 05-Jul-2001

        Introduction

        This document gives coding conventions. Fielding, et al. [Page 3]
        """;

    private const string Rfc = """
        # HTTP Semantics

        The protocol is stateless.

        ## Authors' Addresses

        Roy T. Fielding, Adobe, fielding@gbiv.com
        """;

    private const string EveryChunk = "coding conventions protocol stateless authors fielding semantics";

    private InMemoryKnowledgeStore _store = null!;
    private Captured _records = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        _records = new Captured();
    }

    private DocumentProcessor Processor(
        DocumentHeaderOptions? headers = null, IReadOnlyList<string>? drop = null, Func<string, string>? clean = null) =>
        new(new HttpClient(), [new MarkdownExtractor(), new PlainTextExtractor()], new SlidingWindowChunker(), _store)
        {
            Headers = headers,
            Records = _records,
            DropSections = drop,
            CleanText = clean,
        };

    private static MemoryStream Stream(string text) => new(Encoding.UTF8.GetBytes(text));

    private Task<IReadOnlyList<KnowledgeChunk>> ChunksAsync() =>
        _store.SearchAsync(EveryChunk, new SearchOptions { Mode = SearchMode.Sparse, TopK = 50 });

    [Test]
    public async Task ProcessAsync_NoFieldsNamed_KeepsNoneButStillReadsTheDate()
    {
        await Processor(new DocumentHeaderOptions { Read = DocumentHeaders.FromFieldBlock, DateField = "Created" })
            .ProcessAsync(Stream(Pep), ".txt", "pep-8");

        var chunks = await ChunksAsync();
        chunks.ShouldNotBeEmpty();
        chunks.ShouldAllBe(c => !c.Text.StartsWith('['));
        chunks.ShouldAllBe(c => !c.Metadata.ContainsKey("Author"));
        chunks.ShouldAllBe(c => c.Metadata[DocumentHeaderOptions.EffectiveAtKey] == "2001-07-05T00:00:00.0000000+00:00");
        _records.Header!.ShouldNotContainKey("Author");
    }

    [Test]
    public async Task ProcessAsync_FieldsNamed_KeepsOnlyThoseOnTheChunkAndForRecords()
    {
        await Processor(new DocumentHeaderOptions { Read = DocumentHeaders.FromFieldBlock, Fields = ["Title", "Status"] })
            .ProcessAsync(Stream(Pep), ".txt", "pep-8");

        var chunks = await ChunksAsync();
        chunks.ShouldAllBe(c => c.Text.StartsWith("[Title: Style Guide for Python Code · Status: Active]\n"));
        chunks.ShouldAllBe(c => c.Metadata["Status"] == "Active" && !c.Metadata.ContainsKey("Author") && !c.Metadata.ContainsKey("Created"));
        _records.Header!.Keys.ShouldBe(["Title", "Status"], ignoreOrder: true);
    }

    [Test]
    public async Task ProcessAsync_FieldsNamed_MarksWhichOfTheChunksMetadataAreHeaderFields()
    {
        await Processor(new DocumentHeaderOptions { Read = DocumentHeaders.FromFieldBlock, Fields = ["Title", "Status"] })
            .ProcessAsync(Stream(Pep), ".txt", "pep-8");

        (await ChunksAsync()).ShouldAllBe(c => c.Metadata[DocumentHeaderOptions.HeaderFieldsKey] == "[\"Title\",\"Status\"]");
        _records.Header!.ShouldNotContainKey(DocumentHeaderOptions.HeaderFieldsKey);
    }

    [Test]
    public async Task ProcessAsync_NoFieldsNamed_MarksNone()
    {
        await Processor(new DocumentHeaderOptions { Read = DocumentHeaders.FromFieldBlock }).ProcessAsync(Stream(Pep), ".txt", "pep-8");

        (await ChunksAsync()).ShouldAllBe(c => !c.Metadata.ContainsKey(DocumentHeaderOptions.HeaderFieldsKey));
    }

    [Test]
    public async Task ProcessAsync_Tags_FillTheFieldsTheDocumentDoesntGive()
    {
        var tags = new Dictionary<string, string> { ["status"] = "replaced", ["creation-date"] = "2019-07-1" };

        await Processor(new DocumentHeaderOptions
        {
            Read = DocumentHeaders.FromMarkdown,
            Fields = ["status"],
            DateField = "creation-date",
            SupersededBy = f => f.TryGetValue("status", out var s) && s == "replaced" ? ["keps/sig-node/2133"] : null,
        }).ProcessAsync(Stream(Rfc), ".md", "kep-1", tags: tags);

        var chunks = await ChunksAsync();
        chunks.ShouldAllBe(c => c.Text.StartsWith("[status: replaced]\n"));
        chunks.ShouldAllBe(c => c.Metadata[DocumentHeaderOptions.EffectiveAtKey] == "2019-07-01T00:00:00.0000000+00:00");
        chunks.ShouldAllBe(c => c.Metadata[DocumentHeaderOptions.SupersededByKey] == "keps/sig-node/2133");
    }

    [Test]
    public async Task ProcessAsync_ReplacesUpdatesAndSeveralSupersededBy_AreWrittenOnTheChunks()
    {
        await Processor(new DocumentHeaderOptions
        {
            Read = DocumentHeaders.FromFieldBlock,
            SupersededBy = _ => ["pep-9001", "pep-9002"],
            Replaces = _ => ["pep-0007", "pep-0006"],
            Updates = _ => ["pep-0001"],
        }).ProcessAsync(Stream(Pep), ".txt", "pep-8");

        var chunks = await ChunksAsync();
        chunks.ShouldNotBeEmpty();
        chunks.ShouldAllBe(c => c.Metadata[DocumentHeaderOptions.SupersededByKey] == "pep-9001, pep-9002");
        chunks.ShouldAllBe(c => c.Metadata[DocumentHeaderOptions.ReplacesSourcesKey] == "[\"pep-0007\",\"pep-0006\"]");
        chunks.ShouldAllBe(c => c.Metadata["entity:updates:pep-0001"] == "updates:pep-0001");
        _records.Header![DocumentHeaderOptions.ReplacesSourcesKey].ShouldBe("[\"pep-0007\",\"pep-0006\"]");
    }

    [Test]
    public async Task ProcessAsync_ParseDate_IsUsedForTheDateField()
    {
        await Processor(new DocumentHeaderOptions
        {
            Read = DocumentHeaders.FromFieldBlock,
            DateField = "Created",
            ParseDate = _ => new DateTimeOffset(1999, 1, 2, 0, 0, 0, TimeSpan.Zero),
        }).ProcessAsync(Stream(Pep), ".txt", "pep-8");

        (await ChunksAsync()).ShouldAllBe(c => c.Metadata[DocumentHeaderOptions.EffectiveAtKey] == "1999-01-02T00:00:00.0000000+00:00");
    }

    [Test]
    public async Task ProcessAsync_DropSections_LeavesThemOutOfChunksAndRecords()
    {
        await Processor(drop: ["authors' addresses"]).ProcessAsync(Stream(Rfc), ".md", "rfc-9110");

        var chunks = await ChunksAsync();
        chunks.ShouldNotBeEmpty();
        chunks.ShouldAllBe(c => !c.Text.Contains("fielding@gbiv.com"));
        _records.Document!.Sections.ShouldAllBe(s => s.SectionTitle != "Authors' Addresses");
    }

    [Test]
    public async Task ProcessAsync_CleanText_IsAppliedBeforeChunkingAndRecords()
    {
        await Processor(clean: t => t.Replace("Fielding, et al. [Page 3]", ""))
            .ProcessAsync(Stream(Pep), ".txt", "pep-8");

        (await ChunksAsync()).ShouldAllBe(c => !c.Text.Contains("[Page 3]"));
        _records.Document!.Sections.ShouldAllBe(s => !s.Text.Contains("[Page 3]"));
    }

    /// <summary>Keeps what the records step was handed.</summary>
    private sealed class Captured : IDocumentRecords
    {
        public ExtractedDocument? Document { get; private set; }

        public IReadOnlyDictionary<string, string>? Header { get; private set; }

        public Task<int> ExtractAsync(string sourceId, ExtractedDocument document, IReadOnlyDictionary<string, string> header, CancellationToken ct = default)
        {
            Document = document;
            Header = header;
            return Task.FromResult(0);
        }
    }
}
