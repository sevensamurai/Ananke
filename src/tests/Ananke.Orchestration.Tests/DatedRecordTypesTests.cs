using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Documents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Orchestration.Knowledge.Linking;
using Ananke.Orchestration.Knowledge.Records;
using Shouldly;

#pragma warning disable ANNKEEXP004

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Two record types on one document, in one store: each <see cref="DatedRecordExtractor{TRecord}"/> names its
/// <see cref="DatedRecordOptions{TRecord}.RecordType"/>, both go to <see cref="DocumentProcessor.Records"/> as a
/// <see cref="CompositeDocumentRecords"/>, and each type is replaced and read back on its own. The model is scripted per
/// type, by its instructions.
/// </summary>
[TestFixture]
public class DatedRecordTypesTests
{
    public sealed record Decision(string Statement, string? Date);

    public sealed record Question(string Asked, string Owner);

    private const string DecisionInstructions = "Extract the decisions.";
    private const string QuestionInstructions = "Extract the open questions.";

    private static readonly ExtractedDocument Document = new([new ExtractedSection("# Meeting\n\nWe decided and asked things.")]);

    private InMemoryKnowledgeStore _records = null!;
    private Scripted _model = null!;

    [SetUp]
    public void SetUp()
    {
        _records = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        _model = new Scripted(new Dictionary<string, string>
        {
            [DecisionInstructions] = """{"records": [{"Statement": "Telemetry goes to SigNoz", "Date": "2026-11-02"}, {"Statement": "Core.Identity is built on OpenIddict", "Date": null}]}""",
            [QuestionInstructions] = """{"records": [{"Asked": "Who runs the telemetry collector?", "Owner": "ops"}]}""",
        });
    }

    private DatedRecordExtractor<Decision> Decisions(Func<Decision, bool>? keep = null) => new(_model, _records, new DatedRecordOptions<Decision>
    {
        Instructions = DecisionInstructions,
        Text = d => d.Statement,
        Date = d => d.Date,
        RecordType = "decision",
        Keep = keep,
    });

    private DatedRecordExtractor<Question> Questions(Func<Question, bool>? keep = null) => new(_model, _records, new DatedRecordOptions<Question>
    {
        Instructions = QuestionInstructions,
        Text = q => q.Asked,
        Fields = q => new Dictionary<string, string> { ["owner"] = q.Owner },
        RecordType = "question",
        Keep = keep,
    });

    private DocumentProcessor Processor() =>
        new(new HttpClient(), [new OneDocument(Document)], new SlidingWindowChunker(), new InMemoryKnowledgeStore(new InMemoryEmbedder()))
        {
            Records = new CompositeDocumentRecords(Decisions(), Questions()),
        };

    private Task<ProcessingResult> ProcessAsync() => Processor().ProcessAsync(new MemoryStream(), ".md", "meeting.md");

    private async Task<IReadOnlyList<string>> StoredIdsAsync() =>
        [.. (await _records.ListAsync(new KnowledgeFilter { ["source"] = "meeting.md" }, 100).ConfigureAwait(false)).Select(c => c.Id).Order(StringComparer.Ordinal)];

    [Test]
    public async Task ProcessAsync_TwoRecordTypesInOneStore_EachIsReadBackByItsOwnReader()
    {
        var result = await ProcessAsync();

        result.Records.ShouldBe(3);
        var decisions = await new DatedRecordReader<Decision>(_records, recordType: "decision").ListAsync();
        decisions.Select(d => d.Record.Statement).ShouldBe(["Telemetry goes to SigNoz", "Core.Identity is built on OpenIddict"]);
        var questions = await new DatedRecordReader<Question>(_records, recordType: "question").ListAsync();
        questions.Select(q => q.Record).ShouldBe([new Question("Who runs the telemetry collector?", "ops")]);

        var found = await new DatedRecordReader<Question>(_records, recordType: "question")
            .SearchAsync("telemetry", new SearchOptions { Mode = SearchMode.Sparse, TopK = 10 });
        found.Select(q => q.Text).ShouldBe(["Who runs the telemetry collector?"]);
        var owned = await new DatedRecordReader<Question>(_records, recordType: "question")
            .ListAsync(new KnowledgeFilter { ["owner"] = "ops" });
        owned.Count.ShouldBe(1);
    }

    [Test]
    public async Task ProcessAsync_Again_ReplacesEachTypesRecordsAndLeavesTheOthersAlone()
    {
        await ProcessAsync();
        (await StoredIdsAsync()).ShouldBe([
            "meeting.md:record:decision:0", "meeting.md:record:decision:1", "meeting.md:record:question:0"]);

        _model.Replies[DecisionInstructions] = """{"records": [{"Statement": "Telemetry goes to Better Stack", "Date": null}]}""";
        await Decisions().ExtractAsync("meeting.md", Document, new Dictionary<string, string>());

        (await StoredIdsAsync()).ShouldBe(["meeting.md:record:decision:0", "meeting.md:record:question:0"]);
        (await new DatedRecordReader<Decision>(_records, recordType: "decision").ListAsync())
            .Select(d => d.Text).ShouldBe(["Telemetry goes to Better Stack"]);
        (await new DatedRecordReader<Question>(_records, recordType: "question").ListAsync()).Count.ShouldBe(1);

        _model.Replies[QuestionInstructions] = """{"records": []}""";
        await ProcessAsync();

        (await StoredIdsAsync()).ShouldBe(["meeting.md:record:decision:0"]);
    }

    [Test]
    public async Task ProcessAsync_OneTypesReplyCantBeRead_StoresTheOtherTypeAndThrows()
    {
        _model.Replies[DecisionInstructions] = "Here are the decisions: none.";

        var error = await Should.ThrowAsync<RecordExtractionException>(ProcessAsync);

        error.Message.ShouldContain("meeting.md");
        (await StoredIdsAsync()).ShouldBe(["meeting.md:record:question:0"]);
    }

    [Test]
    public async Task ExtractAsync_SeveralSteps_SumsWhatEachStoredAndRefused()
    {
        var steps = new CompositeDocumentRecords(
            Decisions(keep: d => !d.Statement.Contains("SigNoz")),
            Questions(keep: _ => false),
            new DatedRecordWriter<Decision>(_records, new DatedRecordOptions<Decision> { Text = d => d.Statement, RecordType = "decision" }));

        var result = await steps.ExtractAsync("meeting.md", Document, new Dictionary<string, string>());

        result.ShouldBe((1, 2));
    }

    [Test]
    public async Task ExtractAsync_TwoStepsThrow_ThrowsBothAfterRunningEach()
    {
        _model.Replies[DecisionInstructions] = "not JSON";
        _model.Replies[QuestionInstructions] = "not JSON either";
        var steps = new CompositeDocumentRecords(Decisions(), Questions());

        var error = await Should.ThrowAsync<AggregateException>(() => steps.ExtractAsync("meeting.md", Document, new Dictionary<string, string>()));

        error.InnerExceptions.Count.ShouldBe(2);
        error.InnerExceptions.ShouldAllBe(e => e is RecordExtractionException);
        _model.Calls.ShouldBe(2);
    }

    private DatedRecordExtractor<Decision> Untyped(IKnowledgeStore store) =>
        new(_model, store, new DatedRecordOptions<Decision> { Instructions = DecisionInstructions, Text = d => d.Statement });

    [Test]
    public void Constructor_UntypedExtractorSharesAStoreWithAnotherExtractor_Throws()
    {
        Should.Throw<ArgumentException>(() => new CompositeDocumentRecords(Untyped(_records), Questions()))
            .Message.ShouldContain("RecordType");
        Should.Throw<ArgumentException>(() => new CompositeDocumentRecords(Untyped(_records), Untyped(_records)));
    }

    [Test]
    public void Constructor_TwoExtractorsOfOneTypeOnOneStore_Throws()
    {
        Should.Throw<ArgumentException>(() => new CompositeDocumentRecords(Decisions(), Questions(), Decisions()))
            .Message.ShouldContain("'decision'");
    }

    [Test]
    public void Constructor_NestedStepsThatShareAType_Throws()
    {
        Should.Throw<ArgumentException>(() => new CompositeDocumentRecords(new CompositeDocumentRecords(Decisions()), Decisions()));
        Should.Throw<ArgumentException>(() => new CompositeDocumentRecords(new CompositeDocumentRecords(Untyped(_records)), Decisions()));
    }

    [Test]
    public void Constructor_ExtractorsOnOneStoreThroughLinkedOrCatalogAwareStores_Throws()
    {
        var linked = new LinkedKnowledgeStore(_records, new InMemoryDocumentLinkGraph());
        var catalogAware = new CatalogAwareKnowledgeStore(linked, new InMemoryKnowledgeCatalog(new InMemoryEmbedder()));
        var questions = new DatedRecordExtractor<Question>(_model, catalogAware, new DatedRecordOptions<Question>
        {
            Instructions = QuestionInstructions,
            Text = q => q.Asked,
            RecordType = "question",
        });

        Should.Throw<ArgumentException>(() => new CompositeDocumentRecords(Untyped(linked), Questions()))
            .Message.ShouldContain(nameof(InMemoryKnowledgeStore));
        Should.Throw<ArgumentException>(() => new CompositeDocumentRecords(Untyped(_records), questions));
        Should.Throw<ArgumentException>(() => new CompositeDocumentRecords(Questions(), questions))
            .Message.ShouldContain("'question'");
    }

    [Test]
    public void Constructor_ExtractorsOnDifferentStoresOrOfDifferentTypes_AreAccepted()
    {
        var other = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        var writer = new DatedRecordWriter<Decision>(_records, new DatedRecordOptions<Decision> { Text = d => d.Statement });

        new CompositeDocumentRecords(Untyped(_records), Untyped(other), writer).Owns.Count.ShouldBe(2);
        new CompositeDocumentRecords(
            Untyped(new LinkedKnowledgeStore(_records, new InMemoryDocumentLinkGraph())),
            Untyped(new CatalogAwareKnowledgeStore(other, new InMemoryKnowledgeCatalog(new InMemoryEmbedder())))).Owns.Count.ShouldBe(2);
        new CompositeDocumentRecords(Decisions(), Questions(), writer, writer).Owns
            .ShouldBe([(_records, "decision"), (_records, "question")]);
    }

    /// <summary>Answers each call with the reply kept for its system prompt.</summary>
    private sealed class Scripted(Dictionary<string, string> replies) : IAgentModel
    {
        public Dictionary<string, string> Replies { get; } = replies;

        public int Calls { get; private set; }

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new AgentResponse { Text = Replies[request.SystemPrompt!] });
        }
    }

    private sealed class OneDocument(ExtractedDocument document) : IDocumentExtractor
    {
        public bool CanExtract(string fileExtension) => true;

        public Task<ExtractedDocument> ExtractAsync(Stream data, CancellationToken ct = default) => Task.FromResult(document);
    }
}
