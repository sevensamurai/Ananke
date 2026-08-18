using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

namespace Ananke.Orchestration.Knowledge.Tests;

[TestFixture]
public class InMemoryKnowledgeStoreTests
{
    private InMemoryKnowledgeStore _store = null!;

    [SetUp]
    public void SetUp() => _store = new InMemoryKnowledgeStore(new InMemoryEmbedder());

    [Test]
    public void Constructor_NullEmbedder_Throws() =>
        Should.Throw<ArgumentNullException>(() => new InMemoryKnowledgeStore(null!));

    [Test]
    public void Constructor_NonPositiveMaxDocuments_Throws() =>
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new InMemoryKnowledgeStore(new InMemoryEmbedder(), maxDocuments: 0));

    [Test]
    public async Task SearchAsync_BlankQuery_Throws() =>
        await Should.ThrowAsync<ArgumentException>(() => _store.SearchAsync("   "));

    [Test]
    public async Task UpsertAsync_NullDocuments_Throws() =>
        await Should.ThrowAsync<ArgumentNullException>(() => _store.UpsertAsync(null!));

    [Test]
    public async Task UpsertAsync_EmptyCollection_IsANoOp()
    {
        await _store.UpsertAsync([]);

        _store.Count.ShouldBe(0);
    }

    [Test]
    public async Task UpsertAsync_ThenSearch_FindsUpsertedDocument()
    {
        await _store.UpsertAsync([new KnowledgeDocument { Id = "doc-1", Text = "the quick brown fox" }]);

        var results = await _store.SearchAsync("quick brown fox");

        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe("doc-1");
        results[0].Text.ShouldBe("the quick brown fox");
    }

    [Test]
    public async Task UpsertAsync_SameId_OverwritesExistingDocument()
    {
        await _store.UpsertAsync([new KnowledgeDocument { Id = "doc-1", Text = "original text" }]);
        await _store.UpsertAsync([new KnowledgeDocument { Id = "doc-1", Text = "replaced text" }]);

        _store.Count.ShouldBe(1);
        var results = await _store.SearchAsync("replaced text");
        results.Single().Text.ShouldBe("replaced text");
    }

    [Test]
    public async Task UpsertAsync_ExceedsMaxDocuments_Throws()
    {
        var smallStore = new InMemoryKnowledgeStore(new InMemoryEmbedder(), maxDocuments: 1);
        await smallStore.UpsertAsync([new KnowledgeDocument { Id = "doc-1", Text = "one" }]);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            smallStore.UpsertAsync([new KnowledgeDocument { Id = "doc-2", Text = "two" }]));
    }

    [Test]
    public async Task UpsertAsync_OverwritingSameIdAtCapacity_DoesNotThrow()
    {
        // Re-upserting an existing ID is not "net new", so it must not trip the capacity guard.
        var smallStore = new InMemoryKnowledgeStore(new InMemoryEmbedder(), maxDocuments: 1);
        await smallStore.UpsertAsync([new KnowledgeDocument { Id = "doc-1", Text = "one" }]);

        await Should.NotThrowAsync(() =>
            smallStore.UpsertAsync([new KnowledgeDocument { Id = "doc-1", Text = "one, updated" }]));
    }

    [Test]
    public async Task SearchAsync_TopK_LimitsResultCount()
    {
        await _store.UpsertAsync(
        [
            new KnowledgeDocument { Id = "a", Text = "shared topic alpha" },
            new KnowledgeDocument { Id = "b", Text = "shared topic beta" },
            new KnowledgeDocument { Id = "c", Text = "shared topic gamma" }
        ]);

        var results = await _store.SearchAsync("shared topic", new SearchOptions { TopK = 2 });

        results.Count.ShouldBe(2);
    }

    [Test]
    public async Task SearchAsync_ScoreThreshold_ExcludesLowScoringResults()
    {
        await _store.UpsertAsync([new KnowledgeDocument { Id = "a", Text = "completely unrelated content" }]);

        var results = await _store.SearchAsync(
            "totally different search terms", new SearchOptions { ScoreThreshold = 0.99f });

        results.ShouldBeEmpty();
    }

    [Test]
    public async Task SearchAsync_MetadataFilter_OnlyMatchesDocumentsWithExactValue()
    {
        await _store.UpsertAsync(
        [
            new KnowledgeDocument { Id = "a", Text = "shared topic", Metadata = new Dictionary<string, string> { ["team"] = "platform" } },
            new KnowledgeDocument { Id = "b", Text = "shared topic", Metadata = new Dictionary<string, string> { ["team"] = "infra" } }
        ]);

        var results = await _store.SearchAsync(
            "shared topic", new SearchOptions { Filter = new KnowledgeFilter { ["team"] = "platform" } });

        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe("a");
    }

    [Test]
    public async Task SearchAsync_MetadataFilter_ExcludesDocumentsMissingTheKey()
    {
        await _store.UpsertAsync([new KnowledgeDocument { Id = "a", Text = "shared topic" }]);

        var results = await _store.SearchAsync(
            "shared topic", new SearchOptions { Filter = new KnowledgeFilter { ["team"] = "platform" } });

        results.ShouldBeEmpty();
    }

    [Test]
    public async Task SearchAsync_ResultsAreOrderedByDescendingScore()
    {
        await _store.UpsertAsync(
        [
            new KnowledgeDocument { Id = "exact", Text = "apple banana cherry" },
            new KnowledgeDocument { Id = "partial", Text = "apple durian elderberry" }
        ]);

        var results = await _store.SearchAsync("apple banana cherry");

        results.Count.ShouldBe(2);
        results[0].Score.ShouldBeGreaterThanOrEqualTo(results[1].Score);
        results[0].Id.ShouldBe("exact");
    }

    [Test]
    public async Task DeleteAsync_NullFilter_Throws() =>
        await Should.ThrowAsync<ArgumentNullException>(() => _store.DeleteAsync(null!));

    [Test]
    public async Task DeleteAsync_MatchingFilter_RemovesOnlyMatchingDocuments()
    {
        await _store.UpsertAsync(
        [
            new KnowledgeDocument { Id = "a", Text = "text a", Metadata = new Dictionary<string, string> { ["source"] = "doc-1" } },
            new KnowledgeDocument { Id = "b", Text = "text b", Metadata = new Dictionary<string, string> { ["source"] = "doc-2" } }
        ]);

        await _store.DeleteAsync(new KnowledgeFilter { ["source"] = "doc-1" });

        _store.Count.ShouldBe(1);
        var remaining = await _store.SearchAsync("text b");
        remaining.Single().Id.ShouldBe("b");
    }

    [Test]
    public async Task DeleteAsync_EmptyFilter_MatchesEverything()
    {
        await _store.UpsertAsync(
        [
            new KnowledgeDocument { Id = "a", Text = "text a" },
            new KnowledgeDocument { Id = "b", Text = "text b" }
        ]);

        await _store.DeleteAsync(new KnowledgeFilter());

        _store.Count.ShouldBe(0);
    }

    [Test]
    public async Task UpsertAsync_DocumentWithoutExplicitMetadata_DefaultsToEmptyDictionary()
    {
        await _store.UpsertAsync([new KnowledgeDocument { Id = "a", Text = "no metadata set" }]);

        var results = await _store.SearchAsync("no metadata set");

        results.Single().Metadata.ShouldBeEmpty();
    }
}
