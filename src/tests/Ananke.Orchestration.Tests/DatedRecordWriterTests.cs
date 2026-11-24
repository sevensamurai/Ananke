using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Documents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Orchestration.Knowledge.Records;
using Shouldly;

#pragma warning disable ANNKEEXP004

namespace Ananke.Orchestration.Tests;

/// <summary>
/// <see cref="DatedRecordWriter{TRecord}"/>: records stored with no model call beside the extracted ones, left alone when
/// their document is indexed again, and marked while their quote is gone from it. The model is scripted.
/// </summary>
[TestFixture]
public class DatedRecordWriterTests
{
    public sealed record Decision(string Statement, string? Quote, string? Date, string? Replaces, string? Subject);

    private const string Reply = """
        {"records": [{"Statement": "Telemetry goes to SigNoz", "Quote": "telemetry goes to SigNoz", "Date": "2026-11-02", "Replaces": null, "Subject": null}]}
        """;

    private static readonly ExtractedDocument Document = new([
        new ExtractedSection("We agreed telemetry goes to SigNoz.", SectionTitle: "Telemetry"),
        new ExtractedSection("Core.Identity stays on OpenIddict for now.", SectionTitle: "Identity"),
    ]);

    private static readonly ExtractedDocument Rewritten = new([
        new ExtractedSection("We agreed telemetry goes to SigNoz.", SectionTitle: "Telemetry"),
        new ExtractedSection("Core.Identity moves to Keycloak.", SectionTitle: "Identity"),
    ]);

    private static readonly Decision Note = new(
        "OpenIddict was chosen to avoid per-token fees", "stays on OpenIddict", "2026-11-05", "Duende", "sub-1");

    private InMemoryKnowledgeStore _records = null!;
    private Scripted _model = null!;

    [SetUp]
    public void SetUp()
    {
        _records = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        _model = new Scripted(Reply);
    }

    private static DatedRecordOptions<Decision> Options(string? recordType = "decision") => new()
    {
        Instructions = "Extract the decisions, each with its words quoted.",
        Text = d => d.Statement,
        Quote = d => d.Quote,
        Date = d => d.Date,
        Replaces = d => d.Replaces,
        Fields = d => d.Subject is { } subject ? new Dictionary<string, string> { ["subject"] = subject } : new Dictionary<string, string>(),
        RecordType = recordType,
    };

    private DatedRecordExtractor<Decision> Extractor(string? recordType = "decision") => new(_model, _records, Options(recordType));

    private DatedRecordWriter<Decision> Writer(string? recordType = "decision") => new(_records, Options(recordType));

    private DatedRecordReader<Decision> Reader() => new(_records, recordType: "decision");

    private Task<ProcessingResult> IndexAsync(ExtractedDocument document) =>
        new DocumentProcessor(new HttpClient(), [new OneDocument(document)], new SlidingWindowChunker(), new InMemoryKnowledgeStore(new InMemoryEmbedder()))
        {
            Records = new CompositeDocumentRecords(Extractor(), Writer()),
        }.ProcessAsync(new MemoryStream(), ".md", "0007.md");

    private async Task<IReadOnlyList<string>> StoredIdsAsync() =>
        [.. (await _records.ListAsync(new KnowledgeFilter { ["source"] = "0007.md" }, 100).ConfigureAwait(false)).Select(c => c.Id).Order(StringComparer.Ordinal)];

    private async Task<DatedRecord<Decision>> WrittenAsync(string id = "note-1") => (await Reader().ListAsync().ConfigureAwait(false)).Single(r => r.Id == id);

    [Test]
    public async Task WriteAsync_ARecord_IsReadBackBesideTheExtractedOnes()
    {
        await Extractor().ExtractAsync("0007.md", Document, new Dictionary<string, string>());

        await Writer().WriteAsync("0007.md", "note-1", Note);

        _model.Calls.ShouldBe(1);
        var listed = await Reader().ListAsync();
        listed.Select(r => r.Id).ShouldBe(["note-1", "0007.md:record:decision:0"]);
        var written = listed[0];
        written.Record.ShouldBe(Note);
        written.Source.ShouldBe("0007.md");
        written.Text.ShouldBe("OpenIddict was chosen to avoid per-token fees");
        written.EffectiveAt.ShouldBe(new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero));
        written.Replaces.ShouldBe("Duende");
        written.Quote.ShouldBe("stays on OpenIddict");
        written.PassageGone.ShouldBeFalse();
        (await Reader().ListAsync(new KnowledgeFilter { ["subject"] = "sub-1" })).Select(r => r.Id).ShouldBe(["note-1"]);

        var found = await Reader().SearchAsync("OpenIddict per-token fees", new SearchOptions { Mode = SearchMode.Sparse, TopK = 10 });
        found.Select(r => r.Id).ShouldContain("note-1");
        found.Single(r => r.Id == "note-1").Record.ShouldBe(Note);
        var stored = (await _records.ListAsync(new KnowledgeFilter { [DatedRecordWriter<Decision>.WrittenKey] = "true" }, 10)).Single();
        stored.Metadata[DatedRecordExtractor<Decision>.RecordTypeKey].ShouldBe("decision");
        stored.Metadata.ShouldNotContainKey(DatedRecordExtractor<Decision>.ExtractedKey);
    }

    [TestCase("decision")]
    [TestCase(null)]
    public async Task ExtractAsync_Again_ReplacesOnlyTheExtractedRecords(string? recordType)
    {
        await Extractor(recordType).ExtractAsync("0007.md", Document, new Dictionary<string, string>());
        await Writer(recordType).WriteAsync("0007.md", "note-1", Note);

        _model.Reply = """{"records": []}""";
        await Extractor(recordType).ExtractAsync("0007.md", Document, new Dictionary<string, string>());

        (await StoredIdsAsync()).ShouldBe(["note-1"]);
    }

    [Test]
    public void Records_AWriterOnTheProcessorsOwnStore_IsRefused()
    {
        DocumentProcessor Processor(IKnowledgeStore chunks, IDocumentRecords records) =>
            new(new HttpClient(), [new OneDocument(Document)], new SlidingWindowChunker(), chunks) { Records = records };
        var catalogAware = new CatalogAwareKnowledgeStore(_records, new InMemoryKnowledgeCatalog(new InMemoryEmbedder()));
        var others = new DatedRecordExtractor<Decision>(_model, new InMemoryKnowledgeStore(new InMemoryEmbedder()), Options());

        Should.Throw<ArgumentException>(() => Processor(_records, Writer()))
            .Message.ShouldContain(nameof(DatedRecordWriter<Decision>));
        Should.Throw<ArgumentException>(() => Processor(catalogAware, new CompositeDocumentRecords(others, Writer())));
        Should.Throw<ArgumentException>(() => Processor(_records, new DatedRecordWriter<Decision>(catalogAware, Options())));
    }

    [Test]
    public async Task ProcessAsync_QuoteGoneThenBack_MarksTheWrittenRecordThenClearsIt()
    {
        await IndexAsync(Document);
        await Writer().WriteAsync("0007.md", "note-1", Note);
        await Writer().WriteAsync("0007.md", "note-2", Note with { Statement = "OpenIddict needs no licence", Quote = null });

        await IndexAsync(Rewritten);

        (await StoredIdsAsync()).ShouldBe(["0007.md:record:decision:0", "note-1", "note-2"]);
        var gone = await WrittenAsync();
        gone.PassageGone.ShouldBeTrue();
        gone.Record.ShouldBe(Note);
        gone.Quote.ShouldBe("stays on OpenIddict");
        (await WrittenAsync("note-2")).PassageGone.ShouldBeFalse();
        (await Reader().ListAsync()).Single(r => r.Id == "0007.md:record:decision:0").PassageGone.ShouldBeFalse();

        await IndexAsync(Document);

        (await WrittenAsync()).PassageGone.ShouldBeFalse();
        (await WrittenAsync("note-2")).PassageGone.ShouldBeFalse();
    }

    [Test]
    public async Task ProcessAsync_AWrittenRecordWithNoQuote_IsNeverMarked()
    {
        await Writer().WriteAsync("0007.md", "note-1", Note with { Quote = "  " });

        await IndexAsync(Rewritten);

        var written = await WrittenAsync();
        written.Quote.ShouldBeNull();
        written.PassageGone.ShouldBeFalse();
    }

    [Test]
    public async Task WriteAsync_AnIdOfTheFormAnExtractorGives_Throws()
    {
        await Should.ThrowAsync<ArgumentException>(() => Writer().WriteAsync("0007.md", "0007.md:record:decision:0", Note));

        (await StoredIdsAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task WriteAsync_FieldsNamingTheMarks_DontSetThem()
    {
        var options = Options() with
        {
            Fields = _ => new Dictionary<string, string>
            {
                [DatedRecordExtractor<Decision>.ExtractedKey] = "true",
                [DatedRecordWriter<Decision>.PassageGoneKey] = "true",
            },
        };
        await new DatedRecordWriter<Decision>(_records, options).WriteAsync("0007.md", "note-1", Note);

        await Extractor().ExtractAsync("0007.md", Document, new Dictionary<string, string>());

        (await StoredIdsAsync()).ShouldContain("note-1");
        (await WrittenAsync()).PassageGone.ShouldBeFalse();
    }

    [Test]
    public async Task ExtractAsync_FieldsNamingWritten_DontMakeARecordWritten()
    {
        var options = Options() with
        {
            Fields = _ => new Dictionary<string, string> { [DatedRecordWriter<Decision>.WrittenKey] = "true" },
        };
        await new DatedRecordExtractor<Decision>(_model, _records, options)
            .ExtractAsync("0007.md", Document, new Dictionary<string, string>());

        await _records.DeleteAsync(new KnowledgeFilter { [DatedRecordWriter<Decision>.WrittenKey] = "true" });

        (await StoredIdsAsync()).ShouldBe(["0007.md:record:decision:0"]);
    }

    [Test]
    public async Task DeleteAsync_ByWrittenAndSubject_RemovesOnlyThoseWrittenRecords()
    {
        await IndexAsync(Document);
        await Writer().WriteAsync("0007.md", "note-1", Note);
        await Writer().WriteAsync("0007.md", "note-2", Note with { Subject = "sub-2" });

        await _records.DeleteAsync(new KnowledgeFilter { [DatedRecordWriter<Decision>.WrittenKey] = "true", ["subject"] = "sub-1" });

        (await StoredIdsAsync()).ShouldBe(["0007.md:record:decision:0", "note-2"]);
    }

    [Test]
    public async Task ExtractAsync_AWriteBetweenItsListingAndItsMark_IsKept()
    {
        await Writer().WriteAsync("0007.md", "note-1", Note);
        var edited = Note with { Statement = "OpenIddict was chosen to avoid per-token fees and licences" };
        var store = new AfterListing(_records, () => Writer().WriteAsync("0007.md", "note-1", edited));

        await new DatedRecordWriter<Decision>(store, Options()).ExtractAsync("0007.md", Rewritten, new Dictionary<string, string>());

        var written = await WrittenAsync();
        written.Record.ShouldBe(edited);
        written.Text.ShouldBe(edited.Statement);
    }

    [Test]
    public async Task ExtractAsync_AnErasureBetweenItsListingAndItsMark_StaysDeleted()
    {
        await Writer().WriteAsync("0007.md", "note-1", Note);
        var store = new AfterListing(_records, () =>
            _records.DeleteAsync(new KnowledgeFilter { [DatedRecordWriter<Decision>.WrittenKey] = "true", ["subject"] = "sub-1" }));

        await new DatedRecordWriter<Decision>(store, Options()).ExtractAsync("0007.md", Rewritten, new Dictionary<string, string>());

        (await StoredIdsAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task ExtractAsync_AMarkChange_EmbedsNothing()
    {
        var embedder = new CountingEmbedder();
        _records = new InMemoryKnowledgeStore(embedder);
        await Writer().WriteAsync("0007.md", "note-1", Note);
        var embedded = embedder.Texts;

        await Writer().ExtractAsync("0007.md", Rewritten, new Dictionary<string, string>());
        (await WrittenAsync()).PassageGone.ShouldBeTrue();
        await Writer().ExtractAsync("0007.md", Document, new Dictionary<string, string>());
        (await WrittenAsync()).PassageGone.ShouldBeFalse();

        embedder.Texts.ShouldBe(embedded);
    }

    /// <summary>Lists through <paramref name="inner"/>, then runs <paramref name="then"/> once before returning the listing.</summary>
    private sealed class AfterListing(InMemoryKnowledgeStore inner, Func<Task> then) : IListableKnowledgeStore
    {
        private Func<Task>? _then = then;

        public Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(string query, SearchOptions? options = null, CancellationToken ct = default) =>
            inner.SearchAsync(query, options, ct);

        public Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default) => inner.UpsertAsync(documents, ct);

        public Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default) => inner.DeleteAsync(filter, ct);

        public async Task<IReadOnlyList<KnowledgeChunk>> ListAsync(
            KnowledgeFilter filter, int limit = 1000, KnowledgeDateOrder? order = null, CancellationToken ct = default)
        {
            var listed = await inner.ListAsync(filter, limit, order, ct);
            if (Interlocked.Exchange(ref _then, null) is { } run)
                await run();
            return listed;
        }

        public Task UpdateMetadataAsync(KnowledgeChunk listed, IReadOnlyDictionary<string, string> metadata, CancellationToken ct = default) =>
            inner.UpdateMetadataAsync(listed, metadata, ct);
    }

    /// <summary>Embeds as <see cref="InMemoryEmbedder"/> does, counting the texts it embeds.</summary>
    private sealed class CountingEmbedder : IEmbeddingModel
    {
        private readonly InMemoryEmbedder _inner = new();

        public int Texts { get; private set; }

        public Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default)
        {
            Texts++;
            return _inner.EmbedAsync(text, ct);
        }

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            Texts += texts.Count;
            return _inner.EmbedBatchAsync(texts, ct);
        }
    }

    /// <summary>Answers every call with <see cref="Reply"/>.</summary>
    private sealed class Scripted(string reply) : IAgentModel
    {
        public string Reply { get; set; } = reply;

        public int Calls { get; private set; }

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new AgentResponse { Text = Reply });
        }
    }

    private sealed class OneDocument(ExtractedDocument document) : IDocumentExtractor
    {
        public bool CanExtract(string fileExtension) => true;

        public Task<ExtractedDocument> ExtractAsync(Stream data, CancellationToken ct = default) => Task.FromResult(document);
    }
}
