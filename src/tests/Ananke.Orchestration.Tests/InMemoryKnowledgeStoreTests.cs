using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class InMemoryKnowledgeStoreTests
{
    private InMemoryEmbedder _embedder = null!;
    private InMemoryKnowledgeStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _embedder = new InMemoryEmbedder();
        _store = new InMemoryKnowledgeStore(_embedder);
    }

    // ── Upsert + Search ──────────────────────────────────────────

    [Test]
    public async Task Upsert_ThenSearch_FindsDocument()
    {
        await _store.UpsertAsync([new KnowledgeDocument { Id = "1", Text = "cats are great" }]);

        var results = await _store.SearchAsync("cats");

        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe("1");
        results[0].Text.ShouldBe("cats are great");
        results[0].Score.ShouldBeGreaterThan(0f);
    }

    [Test]
    public async Task Upsert_MultipleDocs_SearchReturnsRankedResults()
    {
        await _store.UpsertAsync(
        [
            new KnowledgeDocument { Id = "a", Text = "the quick brown fox" },
            new KnowledgeDocument { Id = "b", Text = "the lazy dog sleeps" },
            new KnowledgeDocument { Id = "c", Text = "unrelated gamma delta" }
        ]);

        var results = await _store.SearchAsync("the quick brown fox");

        results.Count.ShouldBeGreaterThanOrEqualTo(1);
        // The exact-match document should rank first
        results[0].Id.ShouldBe("a");
    }

    [Test]
    public async Task Upsert_SameId_OverwritesPrevious()
    {
        await _store.UpsertAsync([new KnowledgeDocument { Id = "1", Text = "old text" }]);
        await _store.UpsertAsync([new KnowledgeDocument { Id = "1", Text = "new text" }]);

        _store.Count.ShouldBe(1);
        var results = await _store.SearchAsync("new text");
        results[0].Text.ShouldBe("new text");
    }

    [Test]
    public async Task Upsert_EmptyList_DoesNothing()
    {
        await _store.UpsertAsync([]);
        _store.Count.ShouldBe(0);
    }

    // ── TopK ─────────────────────────────────────────────────────

    [Test]
    public async Task Search_TopK_LimitsResults()
    {
        await _store.UpsertAsync(
        [
            new KnowledgeDocument { Id = "1", Text = "one" },
            new KnowledgeDocument { Id = "2", Text = "two" },
            new KnowledgeDocument { Id = "3", Text = "three" }
        ]);

        var results = await _store.SearchAsync("query", new SearchOptions { TopK = 2 });

        results.Count.ShouldBe(2);
    }

    // ── ScoreThreshold ───────────────────────────────────────────

    [Test]
    public async Task Search_ScoreThreshold_ExcludesLowScores()
    {
        await _store.UpsertAsync(
        [
            new KnowledgeDocument { Id = "1", Text = "very relevant" },
            new KnowledgeDocument { Id = "2", Text = "somewhat relevant" }
        ]);

        // With a very high threshold, only perfect or near-perfect matches pass
        var results = await _store.SearchAsync("very relevant",
            new SearchOptions { ScoreThreshold = 0.99f });

        results.Count.ShouldBeLessThanOrEqualTo(1);
    }

    // ── Metadata filter ──────────────────────────────────────────

    [Test]
    public async Task Search_WithFilter_OnlyMatchingDocuments()
    {
        await _store.UpsertAsync(
        [
            new KnowledgeDocument
            {
                Id = "1", Text = "doc one",
                Metadata = new Dictionary<string, string> { ["source"] = "wiki" }
            },
            new KnowledgeDocument
            {
                Id = "2", Text = "doc two",
                Metadata = new Dictionary<string, string> { ["source"] = "faq" }
            }
        ]);

        var results = await _store.SearchAsync("doc",
            new SearchOptions { Filter = new KnowledgeFilter { ["source"] = "faq" } });

        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe("2");
    }

    [Test]
    public async Task Search_FilterNoMatch_ReturnsEmpty()
    {
        await _store.UpsertAsync(
        [
            new KnowledgeDocument
            {
                Id = "1", Text = "content",
                Metadata = new Dictionary<string, string> { ["tag"] = "A" }
            }
        ]);

        var results = await _store.SearchAsync("content",
            new SearchOptions { Filter = new KnowledgeFilter { ["tag"] = "Z" } });

        results.ShouldBeEmpty();
    }

    // ── Delete ────────────────────────────────────────────────────

    [Test]
    public async Task Delete_WithFilter_RemovesMatchingDocuments()
    {
        await _store.UpsertAsync(
        [
            new KnowledgeDocument
            {
                Id = "1", Text = "keep me",
                Metadata = new Dictionary<string, string> { ["category"] = "A" }
            },
            new KnowledgeDocument
            {
                Id = "2", Text = "delete me",
                Metadata = new Dictionary<string, string> { ["category"] = "B" }
            }
        ]);

        await _store.DeleteAsync(new KnowledgeFilter { ["category"] = "B" });

        _store.Count.ShouldBe(1);
        var results = await _store.SearchAsync("keep");
        results[0].Id.ShouldBe("1");
    }

    // ── Metadata on results ──────────────────────────────────────

    [Test]
    public async Task Search_ResultsIncludeMetadata()
    {
        await _store.UpsertAsync(
        [
            new KnowledgeDocument
            {
                Id = "1", Text = "test",
                Metadata = new Dictionary<string, string> { ["page"] = "5", ["source"] = "manual" }
            }
        ]);

        var results = await _store.SearchAsync("test");

        results[0].Metadata["page"].ShouldBe("5");
        results[0].Metadata["source"].ShouldBe("manual");
    }

    // ── Validation ───────────────────────────────────────────────

    [Test]
    public void Search_NullQuery_Throws()
    {
        Should.ThrowAsync<ArgumentException>(() => _store.SearchAsync(null!));
    }

    [Test]
    public void Upsert_NullDocuments_Throws()
    {
        Should.ThrowAsync<ArgumentNullException>(() => _store.UpsertAsync(null!));
    }
}
