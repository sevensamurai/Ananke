using Ananke.Abstractions.Agents;
using Shouldly;

namespace Ananke.Orchestration.Knowledge.Tests;

/// <summary>
/// <see cref="SearchOptions.Mode"/> on <see cref="InMemoryKnowledgeStore"/>. The embedder returns fixed vectors, so
/// the chunk nearest the query in embedding (<c>overview</c>) differs from the one that shares its rare terms
/// (<c>identity</c>), and each mode must pick its own.
/// </summary>
[TestFixture]
public class InMemoryKnowledgeStoreSearchModeTests
{
    private const string Query = "Which library is Core.Identity built on?";

    private static readonly KnowledgeDocument Overview = new()
    {
        Id = "overview",
        Text = "Token server overview: the service signs tokens for the apps",
    };

    private static readonly KnowledgeDocument Identity = new()
    {
        Id = "identity",
        Text = "Core.Identity is built on OpenIddict",
        Metadata = new Dictionary<string, string> { ["kind"] = "design" },
    };

    private static readonly KnowledgeDocument Unrelated = new()
    {
        Id = "unrelated",
        Text = "Release notes for the web app",
    };

    private InMemoryKnowledgeStore _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _store = new InMemoryKnowledgeStore(new FixedEmbedder(new Dictionary<string, float[]>
        {
            [Query] = [0.9f, 0.1f, 0f],
            [Overview.Text] = [1f, 0f, 0f],
            [Identity.Text] = [0f, 1f, 0f],
            [Unrelated.Text] = [0f, 0f, 1f],
        }));
        await _store.UpsertAsync([Overview, Identity, Unrelated]);
    }

    [Test]
    public async Task SearchAsync_DenseMode_RanksByEmbedding()
    {
        var results = await _store.SearchAsync(Query, new SearchOptions { Mode = SearchMode.Dense, TopK = 3 });

        results[0].Id.ShouldBe("overview");
    }

    [Test]
    public async Task SearchAsync_SparseMode_RanksBySharedTerms()
    {
        var results = await _store.SearchAsync(Query, new SearchOptions { Mode = SearchMode.Sparse, TopK = 3 });

        results.Select(r => r.Id).ShouldBe(["identity"]);
        results[0].Score.ShouldBeGreaterThan(0f);
    }

    [Test]
    public async Task SearchAsync_SparseMode_KeepsDottedTermsWhole()
    {
        var results = await _store.SearchAsync("Core.Identity", new SearchOptions { Mode = SearchMode.Sparse });

        results.Select(r => r.Id).ShouldBe(["identity"]);
    }

    [Test]
    public async Task SearchAsync_SparseMode_AppliesTheFilter()
    {
        var options = new SearchOptions
        {
            Mode = SearchMode.Sparse,
            Filter = new KnowledgeFilter { ["kind"] = "plan" },
        };

        var results = await _store.SearchAsync(Query, options);

        results.ShouldBeEmpty();
    }

    [Test]
    public async Task SearchAsync_HybridMode_FusesBothRankings()
    {
        var results = await _store.SearchAsync(Query, new SearchOptions { Mode = SearchMode.Hybrid, TopK = 2 });

        results.Select(r => r.Id).ShouldBe(["identity", "overview"]);
    }

    [Test]
    public async Task SearchAsync_HybridMode_ScoreThresholdAppliesToTheDenseRankingOnly()
    {
        var options = new SearchOptions { Mode = SearchMode.Hybrid, TopK = 3, ScoreThreshold = 0.5f };

        var results = await _store.SearchAsync(Query, options);

        results.Select(r => r.Id).ShouldBe(["identity", "overview"], ignoreOrder: true);
    }

    [Test]
    public async Task SearchAsync_SparseMode_AfterDelete_NoLongerFindsTheDocument()
    {
        await _store.DeleteAsync(new KnowledgeFilter { ["kind"] = "design" });

        var results = await _store.SearchAsync(Query, new SearchOptions { Mode = SearchMode.Sparse });

        results.ShouldBeEmpty();
    }

    private sealed class FixedEmbedder(IReadOnlyDictionary<string, float[]> vectors) : IEmbeddingModel
    {
        public Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default) =>
            Task.FromResult<ReadOnlyMemory<float>>(vectors[text]);

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
            IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>([.. texts.Select(t => (ReadOnlyMemory<float>)vectors[t])]);
    }
}
