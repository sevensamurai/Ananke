using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge.Linking;
using Shouldly;

namespace Ananke.Orchestration.Knowledge.Tests.Linking;

[TestFixture]
public class DocumentLinkExtractorTests
{
    private FakeKnowledgeStore _store = null!;
    private InMemoryDocumentLinkGraph _graph = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new FakeKnowledgeStore();
        _graph = new InMemoryDocumentLinkGraph();
    }

    private static KnowledgeChunk Chunk(string id, string text, float score, string? source = null) => new()
    {
        Id = id,
        Text = text,
        Score = score,
        Metadata = source is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { ["source"] = source }
    };

    [Test]
    public void Constructor_NullModel_Throws() =>
        Should.Throw<ArgumentNullException>(() =>
            new DocumentLinkExtractor(null!, _store, _graph));

    [Test]
    public void Constructor_NullStore_Throws() =>
        Should.Throw<ArgumentNullException>(() =>
            new DocumentLinkExtractor(new FakeAgentModel("none", 0f), null!, _graph));

    [Test]
    public void Constructor_NullGraph_Throws() =>
        Should.Throw<ArgumentNullException>(() =>
            new DocumentLinkExtractor(new FakeAgentModel("none", 0f), _store, null!));

    [Test]
    public async Task LinkSourceAsync_BlankSourceId_Throws()
    {
        var extractor = new DocumentLinkExtractor(new FakeAgentModel("none", 0f), _store, _graph);

        await Should.ThrowAsync<ArgumentException>(() => extractor.LinkSourceAsync("   "));
    }

    [Test]
    public async Task LinkSourceAsync_NoSourceChunks_ReturnsWithoutCallingModel()
    {
        // Source-chunk lookup uses an empty query; leaving it unmapped in FakeKnowledgeStore
        // yields an empty result, matching a source with nothing ingested yet.
        var model = new FakeAgentModel("references", 0.9f);
        var extractor = new DocumentLinkExtractor(model, _store, _graph);

        await extractor.LinkSourceAsync("empty-source");

        model.CallCount.ShouldBe(0);
        _graph.LinkCount.ShouldBe(0);
    }

    [Test]
    public async Task LinkSourceAsync_SelfMatchCandidate_IsSkipped()
    {
        _store.SetSourceChunks("src-a", [Chunk("c1", "chunk one", 1f, "src-a")]);
        // The candidate search for "chunk one" returns the chunk itself as the only match.
        _store.SetCandidates("chunk one", [Chunk("c1", "chunk one", 1f, "src-a")]);

        var model = new FakeAgentModel("references", 0.9f);
        var extractor = new DocumentLinkExtractor(model, _store, _graph);

        await extractor.LinkSourceAsync("src-a");

        model.CallCount.ShouldBe(0);
        _graph.LinkCount.ShouldBe(0);
    }

    [Test]
    public async Task LinkSourceAsync_CandidateFromSameSource_IsSkipped()
    {
        _store.SetSourceChunks("src-a", [Chunk("c1", "chunk one", 1f, "src-a")]);
        _store.SetCandidates("chunk one", [Chunk("c2", "chunk two", 0.9f, "src-a")]);

        var model = new FakeAgentModel("references", 0.9f);
        var extractor = new DocumentLinkExtractor(model, _store, _graph);

        await extractor.LinkSourceAsync("src-a");

        model.CallCount.ShouldBe(0);
        _graph.LinkCount.ShouldBe(0);
    }

    [Test]
    public async Task LinkSourceAsync_RelationshipNone_DoesNotStoreLink()
    {
        _store.SetSourceChunks("src-a", [Chunk("c1", "chunk one", 1f, "src-a")]);
        _store.SetCandidates("chunk one", [Chunk("c2", "chunk two", 0.9f, "src-b")]);

        var model = new FakeAgentModel("none", 0.95f);
        var extractor = new DocumentLinkExtractor(model, _store, _graph);

        await extractor.LinkSourceAsync("src-a");

        model.CallCount.ShouldBe(1);
        _graph.LinkCount.ShouldBe(0);
    }

    [Test]
    public async Task LinkSourceAsync_ConfidenceBelowThreshold_DoesNotStoreLink()
    {
        _store.SetSourceChunks("src-a", [Chunk("c1", "chunk one", 1f, "src-a")]);
        _store.SetCandidates("chunk one", [Chunk("c2", "chunk two", 0.9f, "src-b")]);

        var model = new FakeAgentModel("references", 0.49f);
        var extractor = new DocumentLinkExtractor(model, _store, _graph);

        await extractor.LinkSourceAsync("src-a");

        _graph.LinkCount.ShouldBe(0);
    }

    [Test]
    public async Task LinkSourceAsync_ConfidenceAtThreshold_StoresLink()
    {
        _store.SetSourceChunks("src-a", [Chunk("c1", "chunk one", 1f, "src-a")]);
        _store.SetCandidates("chunk one", [Chunk("c2", "chunk two", 0.9f, "src-b")]);

        var model = new FakeAgentModel("extends", 0.5f);
        var extractor = new DocumentLinkExtractor(model, _store, _graph);

        await extractor.LinkSourceAsync("src-a");

        _graph.LinkCount.ShouldBe(1);
    }

    [Test]
    public async Task LinkSourceAsync_RelatedCandidate_StoresLinkWeightedByConfidenceAndScore()
    {
        _store.SetSourceChunks("src-a", [Chunk("c1", "chunk one", 1f, "src-a")]);
        _store.SetCandidates("chunk one", [Chunk("c2", "chunk two", 0.8f, "src-b")]);

        var model = new FakeAgentModel("extends", 0.5f);
        var extractor = new DocumentLinkExtractor(model, _store, _graph);

        await extractor.LinkSourceAsync("src-a");

        var links = await _graph.GetLinksAsync("c1");
        links.Count.ShouldBe(1);
        links[0].TargetId.ShouldBe("c2");
        links[0].Relationship.ShouldBe("extends");
        // weight = confidence(0.5) * candidate.Score(0.8) = 0.4
        links[0].Weight.ShouldBe(0.4f, tolerance: 0.001f);
    }

    [Test]
    public async Task LinkSourceAsync_MalformedModelResponse_IsTreatedAsNoRelationship()
    {
        _store.SetSourceChunks("src-a", [Chunk("c1", "chunk one", 1f, "src-a")]);
        _store.SetCandidates("chunk one", [Chunk("c2", "chunk two", 0.9f, "src-b")]);

        var model = new FakeAgentModel(rawText: "not valid json at all");
        var extractor = new DocumentLinkExtractor(model, _store, _graph);

        // Malformed model output must be swallowed into "no link", never thrown.
        await Should.NotThrowAsync(() => extractor.LinkSourceAsync("src-a"));
        _graph.LinkCount.ShouldBe(0);
    }

    [Test]
    public async Task LinkSourceAsync_MultipleCandidates_EvaluatesEachIndependently()
    {
        _store.SetSourceChunks("src-a", [Chunk("c1", "chunk one", 1f, "src-a")]);
        _store.SetCandidates("chunk one",
        [
            Chunk("c2", "related", 0.9f, "src-b"),
            Chunk("c3", "unrelated", 0.8f, "src-c")
        ]);

        // Relationship depends on which candidate text the model is asked about.
        var model = new FakeAgentModel(candidateText =>
            candidateText == "related" ? ("extends", 0.9f) : ("none", 0f));
        var extractor = new DocumentLinkExtractor(model, _store, _graph);

        await extractor.LinkSourceAsync("src-a");

        var links = await _graph.GetLinksAsync("c1");
        links.Count.ShouldBe(1);
        links[0].TargetId.ShouldBe("c2");
    }

    // ── fakes ────────────────────────────────────────────────────────────────
    // DocumentLinkExtractor's own boundaries (IAgentModel, IKnowledgeStore) are faked by hand,
    // matching the pattern used elsewhere in the solution (e.g. WorkflowActivatorTests.FakeModel)
    // rather than a mocking library — no mocking package is referenced by this test project.

    private sealed class FakeAgentModel : IAgentModel
    {
        private readonly string? _rawText;
        private readonly Func<string, (string Relationship, float Confidence)>? _byCandidateText;

        public int CallCount { get; private set; }

        public FakeAgentModel(string relationship, float confidence) =>
            _rawText = $$"""{"relationship":"{{relationship}}","confidence":{{confidence}}}""";

        public FakeAgentModel(Func<string, (string Relationship, float Confidence)> byCandidateText) =>
            _byCandidateText = byCandidateText;

        /// <summary>Constructs a fake that returns exactly <paramref name="rawText"/> as the response body.</summary>
        public FakeAgentModel(string rawText, bool _ = true) => _rawText = rawText;

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            CallCount++;

            if (_byCandidateText is not null)
            {
                // The user message embeds "CANDIDATE CHUNK:\n{text}" — extract the candidate text
                // back out so the fake can respond differently per candidate.
                var userText = request.Messages[0].Content!;
                var candidateMarker = "CANDIDATE CHUNK:\n";
                var idx = userText.IndexOf(candidateMarker, StringComparison.Ordinal) + candidateMarker.Length;
                var candidateText = userText[idx..].TrimEnd();
                var (relationship, confidence) = _byCandidateText(candidateText);
                return Task.FromResult(new AgentResponse
                {
                    Text = $$"""{"relationship":"{{relationship}}","confidence":{{confidence}}}"""
                });
            }

            return Task.FromResult(new AgentResponse { Text = _rawText });
        }
    }

    private sealed class FakeKnowledgeStore : IKnowledgeStore
    {
        private readonly Dictionary<string, IReadOnlyList<KnowledgeChunk>> _bySourceId = new();
        private readonly Dictionary<string, IReadOnlyList<KnowledgeChunk>> _byCandidateQuery = new();

        public void SetSourceChunks(string sourceId, IReadOnlyList<KnowledgeChunk> chunks) =>
            _bySourceId[sourceId] = chunks;

        public void SetCandidates(string query, IReadOnlyList<KnowledgeChunk> chunks) =>
            _byCandidateQuery[query] = chunks;

        public Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(
            string query, SearchOptions? options = null, CancellationToken ct = default)
        {
            // The extractor's first call per source always searches with an empty query and a
            // "source" filter — route it by the filter value. All later calls search by chunk text.
            if (query.Length == 0 && options?.Filter is { } filter && filter.TryGetValue("source", out var sourceId))
            {
                return Task.FromResult(_bySourceId.GetValueOrDefault(sourceId, []));
            }

            return Task.FromResult(_byCandidateQuery.GetValueOrDefault(query, []));
        }

        public Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default) =>
            throw new NotSupportedException("Not exercised by DocumentLinkExtractor.");

        public Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default) =>
            throw new NotSupportedException("Not exercised by DocumentLinkExtractor.");
    }
}
