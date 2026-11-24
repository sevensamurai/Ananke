using Ananke.Abstractions.Agents;
using Shouldly;

namespace Ananke.Orchestration.Knowledge.Tests.Search;

/// <summary>
/// Keyword search under a filter, shared by each store's tests: 40 rows, where <c>signoz</c> is in 2 (rare, at most 5%
/// of rows), <c>plan</c> in 3 and <c>the</c> in 5 (not rare). <c>signoz</c> is only in <c>telemetry.md</c>; two of
/// <c>roadmap.md</c>'s rows hold <c>the</c> and <c>plan</c>, the third <c>the</c> only. The query <c>SigNoz plan</c>
/// filtered to <c>roadmap.md</c> must still find the two.
/// </summary>
public static class FilteredKeywordSearchCases
{
    /// <summary>The query: one rare term and one common term.</summary>
    public const string Query = "SigNoz plan";

    /// <summary>The source holding <c>plan</c> and not <c>signoz</c>.</summary>
    public const string Roadmap = "roadmap.md";

    /// <summary>The source holding <c>signoz</c>.</summary>
    public const string Telemetry = "telemetry.md";

    private static readonly KnowledgeDocument[] Rows =
    [
        Row("signoz-1", "SigNoz is the telemetry backend", Telemetry),
        Row("signoz-2", "SigNoz replaced Grafana Cloud", Telemetry),
        Row("telemetry-plan", "The plan for traces", Telemetry),
        Row("roadmap-1", "The plan for the first quarter", Roadmap),
        Row("roadmap-2", "The plan for hiring", Roadmap),
        Row("roadmap-3", "The roadmap for the year", Roadmap),
        .. Enumerable.Range(0, 34).Select(i => Row($"note-{i:D2}", $"Note {i} about release {i}", "notes.md")),
    ];

    /// <summary>
    /// The embedder to build the store with. The query is opposite <see cref="Roadmap"/>'s rows, so dense search keeps
    /// none of them (cosine -1, under the default score threshold of 0) and Hybrid can find them only by keyword.
    /// </summary>
    public static IEmbeddingModel Embedder { get; } = new FixedEmbedder();

    /// <summary>Stores the rows in <paramref name="store"/>.</summary>
    public static Task FillAsync(IKnowledgeStore store) => store.UpsertAsync(Rows);

    /// <summary>Sparse search filtered to <see cref="Roadmap"/> returns its rows, which hold only the common term.</summary>
    public static async Task SearchAsync_SparseMode_FilterLeavesNoRowWithTheRareTerm_ReturnsRowsWithTheOtherTerms(IKnowledgeStore store)
    {
        var results = await store.SearchAsync(Query, Filtered(SearchMode.Sparse, Roadmap));

        results.Select(r => r.Id).ShouldBe(["roadmap-1", "roadmap-2"], ignoreOrder: true);
    }

    /// <summary>Hybrid search filtered to <see cref="Roadmap"/> returns its rows from its keyword half.</summary>
    public static async Task SearchAsync_HybridMode_FilterLeavesNoRowWithTheRareTerm_ReturnsRowsWithTheOtherTerms(IKnowledgeStore store)
    {
        (await store.SearchAsync(Query, Filtered(SearchMode.Dense, Roadmap))).ShouldBeEmpty();

        var results = await store.SearchAsync(Query, Filtered(SearchMode.Hybrid, Roadmap));

        results.Select(r => r.Id).ShouldBe(["roadmap-1", "roadmap-2"], ignoreOrder: true);
    }

    /// <summary>Search options in <paramref name="mode"/> keeping only <paramref name="source"/>'s rows.</summary>
    public static SearchOptions Filtered(SearchMode mode, string source) =>
        new() { Mode = mode, TopK = 10, Filter = new KnowledgeFilter { ["source"] = source } };

    private static KnowledgeDocument Row(string id, string text, string source) => new()
    {
        Id = id,
        Text = text,
        Metadata = new Dictionary<string, string> { ["source"] = source },
    };

    private sealed class FixedEmbedder : IEmbeddingModel
    {
        public EmbeddingModelIdentity? Identity { get; } = new("test", "filtered-keyword-search", 3);

        public Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default) =>
            Task.FromResult<ReadOnlyMemory<float>>(Vector(text));

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>([.. texts.Select(t => (ReadOnlyMemory<float>)Vector(t))]);

        private static float[] Vector(string text) =>
            text == Query ? [1f, 0f, 0f]
            : Rows.Any(r => r.Text == text && r.Metadata["source"] == Roadmap) ? [-1f, 0f, 0f]
            : [0f, 1f, 0f];
    }
}
