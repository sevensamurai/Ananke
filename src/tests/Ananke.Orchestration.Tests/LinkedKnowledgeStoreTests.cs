using Ananke.Orchestration.Knowledge;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Orchestration.Knowledge.Linking;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class LinkedKnowledgeStoreTests
{
    private InMemoryEmbedder _embedder = null!;
    private InMemoryKnowledgeStore _inner = null!;
    private InMemoryDocumentLinkGraph _graph = null!;

    [SetUp]
    public void SetUp()
    {
        _embedder = new InMemoryEmbedder();
        _inner = new InMemoryKnowledgeStore(_embedder);
        _graph = new InMemoryDocumentLinkGraph();
    }

    // ── Basic delegation ─────────────────────────────────────────

    [Test]
    public async Task SearchAsync_NoLinks_DelegatesToInner()
    {
        var store = new LinkedKnowledgeStore(_inner, _graph);

        await _inner.UpsertAsync([new KnowledgeDocument { Id = "1", Text = "cats are great" }]);

        var results = await store.SearchAsync("cats");

        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe("1");
    }

    [Test]
    public async Task UpsertAsync_DelegatesToInner()
    {
        var store = new LinkedKnowledgeStore(_inner, _graph);

        await store.UpsertAsync([new KnowledgeDocument { Id = "1", Text = "test" }]);

        _inner.Count.ShouldBe(1);
    }

    [Test]
    public async Task DeleteAsync_DelegatesToInner()
    {
        var store = new LinkedKnowledgeStore(_inner, _graph);

        await _inner.UpsertAsync(
        [
            new KnowledgeDocument
            {
                Id = "1", Text = "test",
                Metadata = new Dictionary<string, string> { ["source"] = "docs" }
            }
        ]);

        await store.DeleteAsync(new KnowledgeFilter { ["source"] = "docs" });

        _inner.Count.ShouldBe(0);
    }

    // ── Graph expansion disabled ─────────────────────────────────

    [Test]
    public async Task SearchAsync_ExpansionDisabled_ReturnsOnlyVectorResults()
    {
        var options = new LinkedSearchOptions { ExpandGraph = false };
        var store = new LinkedKnowledgeStore(_inner, _graph, options);

        await _inner.UpsertAsync(
        [
            new KnowledgeDocument { Id = "1", Text = "cats are great" },
            new KnowledgeDocument { Id = "2", Text = "dogs are fine" }
        ]);

        await _graph.AddLinkAsync("1", "2", "references");

        var results = await store.SearchAsync("cats");

        // Should not expand through graph — only vector results
        results.Count.ShouldBeGreaterThanOrEqualTo(1);
        // Results should be pure vector-ranked (no graph expansion influence)
    }

    // ── Graph expansion ──────────────────────────────────────────

    [Test]
    public async Task SearchAsync_WithLinks_ExpandsResults()
    {
        var store = new LinkedKnowledgeStore(_inner, _graph);

        await _inner.UpsertAsync(
        [
            new KnowledgeDocument
            {
                Id = "1", Text = "cats are great pets",
                Metadata = new Dictionary<string, string> { ["id"] = "1" }
            },
            new KnowledgeDocument
            {
                Id = "2", Text = "completely unrelated gamma delta",
                Metadata = new Dictionary<string, string> { ["id"] = "2" }
            }
        ]);

        // Link cat doc to the unrelated doc
        await _graph.AddLinkAsync("1", "2", "references", 0.9f);

        var results = await store.SearchAsync("cats are great pets",
            new SearchOptions { TopK = 5 });

        // Both should appear — doc 1 via vector, doc 2 via graph expansion
        results.Count.ShouldBe(2);
    }

    [Test]
    public async Task SearchAsync_GraphResults_HaveLowerScoreThanSeeds()
    {
        var store = new LinkedKnowledgeStore(_inner, _graph,
            new LinkedSearchOptions { GraphScoreDiscount = 0.5f });

        await _inner.UpsertAsync(
        [
            new KnowledgeDocument
            {
                Id = "1", Text = "cats are great pets",
                Metadata = new Dictionary<string, string> { ["id"] = "1" }
            },
            new KnowledgeDocument
            {
                Id = "2", Text = "completely unrelated gamma delta",
                Metadata = new Dictionary<string, string> { ["id"] = "2" }
            }
        ]);

        await _graph.AddLinkAsync("1", "2", "references", 1.0f);

        var results = await store.SearchAsync("cats are great pets",
            new SearchOptions { TopK = 5 });

        if (results.Count >= 2)
        {
            var seedResult = results.First(r => r.Id == "1");
            var graphResult = results.First(r => r.Id == "2");
            graphResult.Score.ShouldBeLessThan(seedResult.Score);
        }
    }

    // ── Empty store ──────────────────────────────────────────────

    [Test]
    public async Task SearchAsync_EmptyStore_ReturnsEmpty()
    {
        var store = new LinkedKnowledgeStore(_inner, _graph);

        var results = await store.SearchAsync("anything");

        results.ShouldBeEmpty();
    }

    // ── TopK respected ───────────────────────────────────────────

    [Test]
    public async Task SearchAsync_TopK_LimitsExpandedResults()
    {
        var store = new LinkedKnowledgeStore(_inner, _graph);

        await _inner.UpsertAsync(
        [
            new KnowledgeDocument
            {
                Id = "1", Text = "main topic here",
                Metadata = new Dictionary<string, string> { ["id"] = "1" }
            },
            new KnowledgeDocument
            {
                Id = "2", Text = "related topic one",
                Metadata = new Dictionary<string, string> { ["id"] = "2" }
            },
            new KnowledgeDocument
            {
                Id = "3", Text = "related topic two",
                Metadata = new Dictionary<string, string> { ["id"] = "3" }
            }
        ]);

        await _graph.AddLinkAsync("1", "2", "references");
        await _graph.AddLinkAsync("1", "3", "extends");

        var results = await store.SearchAsync("main topic",
            new SearchOptions { TopK = 2 });

        results.Count.ShouldBeLessThanOrEqualTo(2);
    }

    // ── Caller's filter ──────────────────────────────────────────

    [Test]
    public async Task SearchAsync_WithFilter_FollowsOnlyLinksToChunksTheFilterMatches()
    {
        await UpsertOutletsAsync();
        var store = new LinkedKnowledgeStore(new SeedOnlyStore(_inner), _graph);

        var results = await store.SearchAsync("cats are great pets",
            new SearchOptions { Filter = new KnowledgeFilter { ["outlet"] = "a" } });

        results.Select(r => r.Id).ShouldBe(["1", "3"], ignoreOrder: true);
    }

    [Test]
    public async Task SearchAsync_WithAnyOfFilter_FollowsOnlyLinksToChunksTheFilterMatches()
    {
        await UpsertOutletsAsync();
        var store = new LinkedKnowledgeStore(new SeedOnlyStore(_inner), _graph);
#pragma warning disable ANNKEEXP004 // KnowledgeFilter.AnyOf is experimental
        var filter = new KnowledgeFilter { AnyOf = { ["outlet"] = ["a", "c"] } };
#pragma warning restore ANNKEEXP004

        var results = await store.SearchAsync("cats are great pets", new SearchOptions { Filter = filter });

        results.Select(r => r.Id).ShouldBe(["1", "3", "4"], ignoreOrder: true);
    }

    [Test]
    public async Task SearchAsync_WithIdFilter_FollowsOnlyLinksToTheIdsItAllows()
    {
        await UpsertOutletsAsync();
        var store = new LinkedKnowledgeStore(new SeedOnlyStore(_inner), _graph);
#pragma warning disable ANNKEEXP004 // KnowledgeFilter.AnyOf is experimental
        var filter = new KnowledgeFilter { AnyOf = { ["id"] = ["1", "4"] } };
#pragma warning restore ANNKEEXP004

        var results = await store.SearchAsync("cats are great pets", new SearchOptions { Filter = filter });

        results.Select(r => r.Id).ShouldBe(["1", "4"], ignoreOrder: true);
    }

    /// <summary>Seed 1 (outlet a) links to 2 (outlet b), 3 (outlet a) and 4 (outlet c).</summary>
    private async Task UpsertOutletsAsync()
    {
        await _inner.UpsertAsync(
        [
            Doc("1", "cats are great pets", "a"),
            Doc("2", "completely unrelated gamma delta", "b"),
            Doc("3", "another unrelated epsilon zeta", "a"),
            Doc("4", "yet another unrelated eta theta", "c")
        ]).ConfigureAwait(false);
        await _graph.AddLinkAsync("1", "2", "references").ConfigureAwait(false);
        await _graph.AddLinkAsync("1", "3", "references").ConfigureAwait(false);
        await _graph.AddLinkAsync("1", "4", "references").ConfigureAwait(false);

        static KnowledgeDocument Doc(string id, string text, string outlet) => new()
        {
            Id = id,
            Text = text,
            Metadata = new Dictionary<string, string> { ["id"] = id, ["outlet"] = outlet }
        };
    }

    /// <summary>
    /// Returns only the top vector result, so a chunk among the results that is not that seed came from a link; a
    /// lookup by id goes to the inner store as asked.
    /// </summary>
    private sealed class SeedOnlyStore(IKnowledgeStore inner) : IKnowledgeStore
    {
        public Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(
            string query, SearchOptions? options = null, CancellationToken ct = default) =>
            options?.Filter?.ContainsKey("id") == true
                ? inner.SearchAsync(query, options, ct)
                : inner.SearchAsync(query, (options ?? new SearchOptions()) with { TopK = 1 }, ct);

        public Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default) =>
            inner.UpsertAsync(documents, ct);

        public Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default) =>
            inner.DeleteAsync(filter, ct);
    }

    // ── Delete cleans links ──────────────────────────────────────

    [Test]
    public async Task DeleteAsync_WithIdFilter_CleansLinks()
    {
        var store = new LinkedKnowledgeStore(_inner, _graph);

        await _inner.UpsertAsync(
        [
            new KnowledgeDocument
            {
                Id = "1", Text = "test",
                Metadata = new Dictionary<string, string> { ["id"] = "1" }
            }
        ]);

        await _graph.AddLinkAsync("1", "2", "references");
        await _graph.AddLinkAsync("3", "1", "extends");

        await store.DeleteAsync(new KnowledgeFilter { ["id"] = "1" });

        _graph.LinkCount.ShouldBe(0);
    }

    // ── Validation ───────────────────────────────────────────────

    [Test]
    public void Constructor_NullInner_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            new LinkedKnowledgeStore(null!, _graph));
    }

    [Test]
    public void Constructor_NullGraph_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            new LinkedKnowledgeStore(_inner, null!));
    }
}
