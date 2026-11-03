using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Documents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Orchestration.Knowledge.Records;
using Shouldly;

#pragma warning disable ANNKEEXP004

namespace Ananke.Orchestration.Tests;

/// <summary>
/// <see cref="DatedRecordExtractor{TRecord}"/>: one call per document, held to the record type's schema, and the
/// records stored by source so a document's next version replaces them. The model is scripted.
/// </summary>
[TestFixture]
public class DatedRecordExtractorTests
{
    public enum DecisionStatus { Decided, Proposed, Replaced }

    public sealed record Decision(string Statement, DecisionStatus Status, string? Date, string? Replaces);

    private const string TwoDecisions = """
        {"records": [
          {"Statement": "Core.Identity is built on OpenIddict", "Status": "Decided", "Date": "2026-10-30", "Replaces": null},
          {"Statement": "Telemetry goes to SigNoz", "Status": "Decided", "Date": null, "Replaces": "Grafana Cloud"}
        ]}
        """;

    private static readonly IReadOnlyDictionary<string, string> Header = new Dictionary<string, string>
    {
        ["Title"] = "0002 – Bridging the product and the backend",
        [DocumentHeaderOptions.EffectiveAtKey] = "2026-11-01T00:00:00.0000000+00:00",
    };

    private static readonly ExtractedDocument Document = new([new ExtractedSection("# 0002\n\nWe decided things.")]);

    private InMemoryKnowledgeStore _records = null!;
    private Scripted _model = null!;

    [SetUp]
    public void SetUp()
    {
        _records = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        _model = new Scripted(TwoDecisions);
    }

    private DatedRecordExtractor<Decision> Extractor(Func<Decision, bool>? keep = null) => new(_model, _records, new DatedRecordOptions<Decision>
    {
        Instructions = "Extract the decisions this document records.",
        Text = d => d.Statement,
        Date = d => d.Date,
        Replaces = d => d.Replaces,
        Keep = keep,
    });

    private Task<IReadOnlyList<KnowledgeChunk>> StoredAsync() =>
        _records.SearchAsync("Core.Identity telemetry duende", new SearchOptions { Mode = SearchMode.Sparse, TopK = 20 });

    [Test]
    public async Task ExtractAsync_StoresEachRecordWithItsSourceDateAndHeader()
    {
        var count = await Extractor().ExtractAsync("0002.md", Document, Header);

        count.ShouldBe(2);
        var stored = (await StoredAsync()).OrderBy(c => c.Id).ToList();
        stored.Select(c => c.Id).ShouldBe(["0002.md:record:0", "0002.md:record:1"]);
        stored[0].Text.ShouldBe("Core.Identity is built on OpenIddict");
        stored.ShouldAllBe(c => c.Metadata["source"] == "0002.md");
        stored.ShouldAllBe(c => c.Metadata["Title"] == "0002 – Bridging the product and the backend");
        stored[0].Metadata[DocumentHeaderOptions.EffectiveAtKey].ShouldBe("2026-10-30T00:00:00.0000000+00:00");
        stored[1].Metadata["replaces"].ShouldBe("Grafana Cloud");
    }

    [Test]
    public async Task ExtractAsync_ARecordWithoutADate_TakesTheDocumentsDate()
    {
        await Extractor().ExtractAsync("0002.md", Document, Header);

        var signoz = (await StoredAsync()).Single(c => c.Id == "0002.md:record:1");
        signoz.Metadata[DocumentHeaderOptions.EffectiveAtKey].ShouldBe("2026-11-01T00:00:00.0000000+00:00");
    }

    [Test]
    public async Task Read_ReturnsTheTypedRecord()
    {
        await Extractor().ExtractAsync("0002.md", Document, Header);

        var record = DatedRecordExtractor<Decision>.Read((await StoredAsync()).Single(c => c.Id == "0002.md:record:1"));

        record.ShouldBe(new Decision("Telemetry goes to SigNoz", DecisionStatus.Decided, null, "Grafana Cloud"));
    }

    [Test]
    public async Task ExtractAsync_SendsTheRecordTypesSchemaAndTheDocument()
    {
        await Extractor().ExtractAsync("0002.md", Document, Header);

        var request = _model.Requests.Single();
        request.SystemPrompt.ShouldBe("Extract the decisions this document records.");
        request.ResponseFormat.ShouldNotBeNull();
        request.ResponseFormat.JsonSchema.ShouldContain("\"records\"");
        request.ResponseFormat.JsonSchema.ShouldContain("\"Statement\"");
        request.ResponseFormat.JsonSchema.ShouldContain("\"Replaced\"");
        request.MaxOutputTokens.ShouldBe(8000);
        request.Messages.Single().Content!.ShouldContain("We decided things.");
        request.Messages.Single().Content!.ShouldContain("0002 – Bridging the product and the backend");
    }

    [Test]
    public async Task ExtractAsync_AgainForTheSameSource_ReplacesItsRecords()
    {
        await Extractor().ExtractAsync("0002.md", Document, Header);
        _model.Reply = """{"records": [{"Statement": "Core.Identity is built on Duende", "Status": "Decided", "Date": null, "Replaces": null}]}""";

        await Extractor().ExtractAsync("0002.md", Document, Header);

        var stored = await _records.SearchAsync("Core.Identity telemetry duende", new SearchOptions { Mode = SearchMode.Sparse, TopK = 20 });
        stored.Select(c => c.Text).ShouldBe(["Core.Identity is built on Duende"]);
    }

    [Test]
    public async Task ExtractAsync_LinkPatterns_LinkEachRecordToWhatItsTextNames()
    {
        _model.Reply = """{"records": [{"Statement": "ZEN-18 adds view filters", "Status": "Decided", "Date": null, "Replaces": null}]}""";
        var extractor = new DatedRecordExtractor<Decision>(_model, _records, new DatedRecordOptions<Decision>
        {
            Instructions = "Extract the decisions.",
            Text = d => d.Statement,
            LinkPatterns = new Dictionary<string, System.Text.RegularExpressions.Regex> { ["card"] = new(@"\b[A-Z]+-\d+\b") },
        });

        await extractor.ExtractAsync("0002.md", Document, Header);

        var record = (await _records.SearchAsync("view filters", new SearchOptions { Mode = SearchMode.Sparse })).Single();
        record.Metadata["entity:card:ZEN-18"].ShouldBe("card:ZEN-18");
    }

    [Test]
    public async Task ExtractAsync_Fields_AreWrittenIntoEachRecordsMetadata()
    {
        var extractor = new DatedRecordExtractor<Decision>(_model, _records, new DatedRecordOptions<Decision>
        {
            Instructions = "Extract the decisions.",
            Text = d => d.Statement,
            Fields = d => new Dictionary<string, string> { ["status"] = d.Status.ToString().ToLowerInvariant(), ["Title"] = "from the record" },
        });

        await extractor.ExtractAsync("0002.md", Document, Header);

        var stored = await StoredAsync();
        stored.ShouldAllBe(c => c.Metadata["status"] == "decided");
        stored.ShouldAllBe(c => c.Metadata["Title"] == "from the record");
    }

    [Test]
    public async Task ExtractAsync_Keep_LeavesOutTheRecordsItRefuses()
    {
        var count = await Extractor(keep: d => !d.Statement.Contains("SigNoz")).ExtractAsync("0002.md", Document, Header);

        count.ShouldBe(1);
        (await StoredAsync()).Select(c => c.Id).ShouldBe(["0002.md:record:0"]);
    }

    [Test]
    public async Task ExtractAsync_AReplyThatIsntTheSchema_ThrowsAndLeavesNoStaleRecords()
    {
        await Extractor().ExtractAsync("0002.md", Document, Header);
        _model.Reply = "Here are the decisions: none.";

        var error = await Should.ThrowAsync<RecordExtractionException>(() => Extractor().ExtractAsync("0002.md", Document, Header));

        error.Message.ShouldContain("0002.md");
        (await StoredAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task ProcessAsync_WithRecords_ExtractsThemAfterTheChunks()
    {
        var chunks = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        var processor = new DocumentProcessor(new HttpClient(), [new OneDocument(Document)], new SlidingWindowChunker(), chunks)
        {
            Records = Extractor(),
        };

        var result = await processor.ProcessAsync(new MemoryStream(), ".md", "0002.md");

        result.Records.ShouldBe(2);
        result.Chunks.ShouldBeGreaterThan(0);
        (await StoredAsync()).Count.ShouldBe(2);
    }

    /// <summary>Answers every call with <see cref="Reply"/>, keeping the requests.</summary>
    private sealed class Scripted(string reply) : IAgentModel
    {
        public string Reply { get; set; } = reply;

        public List<AgentRequest> Requests { get; } = [];

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(new AgentResponse { Text = Reply });
        }
    }

    private sealed class OneDocument(ExtractedDocument document) : IDocumentExtractor
    {
        public bool CanExtract(string fileExtension) => true;

        public Task<ExtractedDocument> ExtractAsync(Stream data, CancellationToken ct = default) => Task.FromResult(document);
    }
}
