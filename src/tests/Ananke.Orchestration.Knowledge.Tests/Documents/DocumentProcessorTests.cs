using Ananke.Orchestration.Knowledge.Documents;
using Shouldly;

namespace Ananke.Orchestration.Knowledge.Tests.Documents;

[TestFixture]
public class DocumentProcessorTests
{
    private static Stream EmptyStream() => new MemoryStream();

    private static ExtractedDocument OneSection(string text = "hello world") =>
        new([new ExtractedSection(text)]);

    [Test]
    public void Constructor_NullHttpClient_Throws() =>
        Should.Throw<ArgumentNullException>(() =>
            new DocumentProcessor(null!, [], new FakeChunker([]), new FakeKnowledgeStore()));

    [Test]
    public void Constructor_NullExtractors_Throws() =>
        Should.Throw<ArgumentNullException>(() =>
            new DocumentProcessor(new HttpClient(), null!, new FakeChunker([]), new FakeKnowledgeStore()));

    [Test]
    public void Constructor_NullChunker_Throws() =>
        Should.Throw<ArgumentNullException>(() =>
            new DocumentProcessor(new HttpClient(), [], null!, new FakeKnowledgeStore()));

    [Test]
    public void Constructor_NullStore_Throws() =>
        Should.Throw<ArgumentNullException>(() =>
            new DocumentProcessor(new HttpClient(), [], new FakeChunker([]), null!));

    [Test]
    public async Task ProcessAsync_NullData_Throws()
    {
        var processor = new DocumentProcessor(new HttpClient(), [], new FakeChunker([]), new FakeKnowledgeStore());

        await Should.ThrowAsync<ArgumentNullException>(() =>
            processor.ProcessAsync(null!, ".md", "source-1"));
    }

    [Test]
    public async Task ProcessAsync_BlankFileExtension_Throws()
    {
        var processor = new DocumentProcessor(new HttpClient(), [], new FakeChunker([]), new FakeKnowledgeStore());

        await Should.ThrowAsync<ArgumentException>(() =>
            processor.ProcessAsync(EmptyStream(), "  ", "source-1"));
    }

    [Test]
    public async Task ProcessAsync_BlankSourceId_Throws()
    {
        var processor = new DocumentProcessor(new HttpClient(), [], new FakeChunker([]), new FakeKnowledgeStore());

        await Should.ThrowAsync<ArgumentException>(() =>
            processor.ProcessAsync(EmptyStream(), ".md", ""));
    }

    [Test]
    public async Task ProcessAsync_NoMatchingExtractor_ThrowsNamingRegisteredExtractors()
    {
        var extractor = new FakeExtractor(canExtract: false, OneSection());
        var processor = new DocumentProcessor(
            new HttpClient(), [extractor], new FakeChunker([]), new FakeKnowledgeStore());

        var ex = await Should.ThrowAsync<NotSupportedException>(() =>
            processor.ProcessAsync(EmptyStream(), ".pdf", "source-1"));

        ex.Message.ShouldContain(".pdf");
        ex.Message.ShouldContain(nameof(FakeExtractor));
    }

    [Test]
    public async Task ProcessAsync_ExtractionYieldsZeroSections_Throws()
    {
        var extractor = new FakeExtractor(canExtract: true, new ExtractedDocument([]));
        var processor = new DocumentProcessor(
            new HttpClient(), [extractor], new FakeChunker([]), new FakeKnowledgeStore());

        await Should.ThrowAsync<InvalidOperationException>(() =>
            processor.ProcessAsync(EmptyStream(), ".md", "source-1"));
    }

    [Test]
    public async Task ProcessAsync_ChunkingYieldsZeroChunks_Throws()
    {
        var extractor = new FakeExtractor(canExtract: true, OneSection());
        var processor = new DocumentProcessor(
            new HttpClient(), [extractor], new FakeChunker([]), new FakeKnowledgeStore());

        await Should.ThrowAsync<InvalidOperationException>(() =>
            processor.ProcessAsync(EmptyStream(), ".md", "source-1"));
    }

    [Test]
    public async Task ProcessAsync_HappyPath_DeletesExistingChunksBeforeUpserting()
    {
        var extractor = new FakeExtractor(canExtract: true, OneSection());
        var chunker = new FakeChunker([new DocumentChunk("chunk text", new Dictionary<string, string>())]);
        var store = new FakeKnowledgeStore();
        var processor = new DocumentProcessor(new HttpClient(), [extractor], chunker, store);

        await processor.ProcessAsync(EmptyStream(), ".md", "source-1");

        store.DeleteCalls.Count.ShouldBe(1);
        store.DeleteCalls[0]["source"].ShouldBe("source-1");
        store.UpsertCalls.Count.ShouldBe(1);
    }

    [Test]
    public async Task ProcessAsync_HappyPath_ChunkIdsFollowSourceIdChunkIndexPattern()
    {
        var extractor = new FakeExtractor(canExtract: true, OneSection());
        var chunker = new FakeChunker(
        [
            new DocumentChunk("first", new Dictionary<string, string>()),
            new DocumentChunk("second", new Dictionary<string, string>())
        ]);
        var store = new FakeKnowledgeStore();
        var processor = new DocumentProcessor(new HttpClient(), [extractor], chunker, store);

        await processor.ProcessAsync(EmptyStream(), ".md", "source-1");

        var upserted = store.UpsertCalls[0].ToList();
        upserted[0].Id.ShouldBe("source-1:chunk:0");
        upserted[1].Id.ShouldBe("source-1:chunk:1");
        upserted[0].Text.ShouldBe("first");
        upserted[1].Text.ShouldBe("second");
    }

    [Test]
    public async Task ProcessAsync_DescriptionAndTags_AreMergedIntoChunkMetadata()
    {
        var extractor = new FakeExtractor(canExtract: true, OneSection());
        var chunker = new FakeChunker(
            [new DocumentChunk("text", new Dictionary<string, string> { ["page"] = "1" })]);
        var store = new FakeKnowledgeStore();
        var processor = new DocumentProcessor(new HttpClient(), [extractor], chunker, store);

        await processor.ProcessAsync(
            EmptyStream(), ".md", "source-1",
            description: "a test doc",
            tags: new Dictionary<string, string> { ["team"] = "platform" });

        var metadata = store.UpsertCalls[0].Single().Metadata;
        metadata["source"].ShouldBe("source-1");
        metadata["page"].ShouldBe("1");
        metadata["description"].ShouldBe("a test doc");
        metadata["team"].ShouldBe("platform");
    }

    [Test]
    public async Task ProcessAsync_TagsDoNotOverwriteExistingChunkMetadata()
    {
        // The chunk itself already carries "source" from the chunker's own metadata (unusual,
        // but the merge order matters): DocumentProcessor's own "source" write happens first,
        // via TryAdd for tags — so a caller-supplied tag with the same key must lose.
        var extractor = new FakeExtractor(canExtract: true, OneSection());
        var chunker = new FakeChunker(
            [new DocumentChunk("text", new Dictionary<string, string> { ["page"] = "1" })]);
        var store = new FakeKnowledgeStore();
        var processor = new DocumentProcessor(new HttpClient(), [extractor], chunker, store);

        await processor.ProcessAsync(
            EmptyStream(), ".md", "source-1",
            tags: new Dictionary<string, string> { ["source"] = "should-not-win" });

        store.UpsertCalls[0].Single().Metadata["source"].ShouldBe("source-1");
    }

    [Test]
    public async Task ProcessAsync_HappyPath_ReturnsCorrectProcessingResult()
    {
        var extractor = new FakeExtractor(canExtract: true, OneSection());
        var chunker = new FakeChunker(
        [
            new DocumentChunk("a", new Dictionary<string, string>()),
            new DocumentChunk("b", new Dictionary<string, string>())
        ]);
        var store = new FakeKnowledgeStore();
        var processor = new DocumentProcessor(new HttpClient(), [extractor], chunker, store);

        var result = await processor.ProcessAsync(EmptyStream(), ".md", "source-1", description: "desc");

        result.Sections.ShouldBe(1);
        result.Chunks.ShouldBe(2);
        result.Source.ShouldBe("source-1");
        result.Description.ShouldBe("desc");
    }

    [Test]
    public async Task ProcessAsync_NoDescription_ReturnsEmptyStringNotNull()
    {
        var extractor = new FakeExtractor(canExtract: true, OneSection());
        var chunker = new FakeChunker([new DocumentChunk("a", new Dictionary<string, string>())]);
        var processor = new DocumentProcessor(new HttpClient(), [extractor], chunker, new FakeKnowledgeStore());

        var result = await processor.ProcessAsync(EmptyStream(), ".md", "source-1");

        result.Description.ShouldBe(string.Empty);
    }

    [Test]
    public async Task ProcessAsync_CancellationToken_IsForwardedToExtractorAndStore()
    {
        var extractor = new FakeExtractor(canExtract: true, OneSection());
        var chunker = new FakeChunker([new DocumentChunk("a", new Dictionary<string, string>())]);
        var store = new FakeKnowledgeStore();
        var processor = new DocumentProcessor(new HttpClient(), [extractor], chunker, store);
        using var cts = new CancellationTokenSource();

        await processor.ProcessAsync(EmptyStream(), ".md", "source-1", ct: cts.Token);

        extractor.LastCancellationToken.ShouldBe(cts.Token);
        store.LastUpsertCancellationToken.ShouldBe(cts.Token);
        store.LastDeleteCancellationToken.ShouldBe(cts.Token);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeExtractor(bool canExtract, ExtractedDocument result) : IDocumentExtractor
    {
        public CancellationToken LastCancellationToken { get; private set; }

        public bool CanExtract(string fileExtension) => canExtract;

        public Task<ExtractedDocument> ExtractAsync(Stream data, CancellationToken ct = default)
        {
            LastCancellationToken = ct;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeChunker(IReadOnlyList<DocumentChunk> chunks) : IDocumentChunker
    {
        public IReadOnlyList<DocumentChunk> Chunk(ExtractedDocument document, ChunkingOptions? options = null) =>
            chunks;
    }

    private sealed class FakeKnowledgeStore : IKnowledgeStore
    {
        public List<KnowledgeFilter> DeleteCalls { get; } = [];
        public List<IReadOnlyList<KnowledgeDocument>> UpsertCalls { get; } = [];
        public CancellationToken LastDeleteCancellationToken { get; private set; }
        public CancellationToken LastUpsertCancellationToken { get; private set; }

        public Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(
            string query, SearchOptions? options = null, CancellationToken ct = default) =>
            throw new NotSupportedException("Not exercised by DocumentProcessor.");

        public Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default)
        {
            UpsertCalls.Add(documents.ToList());
            LastUpsertCancellationToken = ct;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default)
        {
            DeleteCalls.Add(filter);
            LastDeleteCancellationToken = ct;
            return Task.CompletedTask;
        }
    }
}
