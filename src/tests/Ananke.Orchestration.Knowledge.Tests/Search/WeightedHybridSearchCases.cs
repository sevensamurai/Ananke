using Ananke.Abstractions.Agents;
using Shouldly;

namespace Ananke.Orchestration.Knowledge.Tests.Search;

/// <summary>
/// Hybrid search with weighted halves, shared by each store's tests: <c>dense-only</c> is first in the dense ranking and
/// holds no query term; <c>keyword-only</c> is first in the keyword ranking and is opposite the query, so dense search
/// keeps it out (cosine -1, under the default score threshold of 0). With equal weights their fused scores tie.
/// </summary>
public static class WeightedHybridSearchCases
{
    /// <summary>The query.</summary>
    public const string Query = "telemetry";

    private static readonly KnowledgeDocument[] Rows =
    [
        new() { Id = "dense-only", Text = "Traces go to the backend" },
        new() { Id = "keyword-only", Text = "The telemetry backend is SigNoz" },
    ];

    /// <summary>The embedder to build the store with.</summary>
    public static IEmbeddingModel Embedder { get; } = new FixedEmbedder();

    /// <summary>Stores the rows in <paramref name="store"/>.</summary>
    public static Task FillAsync(IKnowledgeStore store) => store.UpsertAsync(Rows);

    /// <summary>With the default weights the tied hits are ordered by id, the dense-only hit first.</summary>
    public static async Task SearchAsync_HybridModeDefaultWeights_HitsAtTheSameRankTie(IKnowledgeStore store)
    {
        var results = await store.SearchAsync(Query, new SearchOptions { Mode = SearchMode.Hybrid });

        results.Select(r => r.Id).ShouldBe(["dense-only", "keyword-only"]);
        results[0].Score.ShouldBe(results[1].Score);
    }

    /// <summary>A keyword weight of 2 moves the keyword-only hit above the dense-only hit at the same rank.</summary>
    public static async Task SearchAsync_HybridModeKeywordWeight2_KeywordOnlyHitAboveDenseOnlyHit(IKnowledgeStore store)
    {
        var results = await store.SearchAsync(Query, new SearchOptions { Mode = SearchMode.Hybrid, KeywordWeight = 2 });

        results.Select(r => r.Id).ShouldBe(["keyword-only", "dense-only"]);
        results[0].Score.ShouldBe(2 * results[1].Score, tolerance: 1e-6f);
    }

    /// <summary>A keyword weight of 0 leaves the keyword ranking out: the keyword-only hit, which dense search keeps out, isn't returned.</summary>
    public static async Task SearchAsync_HybridModeKeywordWeight0_ReturnsOnlyTheDenseHit(IKnowledgeStore store)
    {
        var results = await store.SearchAsync(Query, new SearchOptions { Mode = SearchMode.Hybrid, KeywordWeight = 0 });

        results.Select(r => r.Id).ShouldBe(["dense-only"]);
    }

    /// <summary>Both weights 0 leave no ranking to run: the search is refused.</summary>
    public static Task SearchAsync_HybridModeBothWeights0_Throws(IKnowledgeStore store) =>
        Should.ThrowAsync<ArgumentException>(() =>
            store.SearchAsync(Query, new SearchOptions { Mode = SearchMode.Hybrid, DenseWeight = 0, KeywordWeight = 0 }));

    /// <summary>The weights only apply to <see cref="SearchMode.Hybrid"/>: a dense search ignores both being 0.</summary>
    public static async Task SearchAsync_DenseModeBothWeights0_ReturnsTheDenseHit(IKnowledgeStore store)
    {
        var results = await store.SearchAsync(Query, new SearchOptions { DenseWeight = 0, KeywordWeight = 0 });

        results.ShouldNotBeEmpty();
    }

    private sealed class FixedEmbedder : IEmbeddingModel
    {
        public EmbeddingModelIdentity? Identity { get; } = new("test", "weighted-hybrid-search", 3);

        public Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default) =>
            Task.FromResult<ReadOnlyMemory<float>>(Vector(text));

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>([.. texts.Select(t => (ReadOnlyMemory<float>)Vector(t))]);

        private static float[] Vector(string text) =>
            text == Query || text == Rows[0].Text ? [1f, 0f, 0f] : [-1f, 0f, 0f];
    }
}
