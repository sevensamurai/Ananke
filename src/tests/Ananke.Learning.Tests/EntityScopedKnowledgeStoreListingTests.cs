using Ananke.Learning.EntityMemory;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

#pragma warning disable ANNKEEXP004

namespace Ananke.Learning.Tests;

/// <summary><see cref="EntityScopedKnowledgeStore"/> lists only its own entity's chunks.</summary>
[TestFixture]
public class EntityScopedKnowledgeStoreListingTests
{
    [Test]
    public async Task ListAsync_ListsOnlyItsOwnEntitysChunks()
    {
        var shared = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        var mine = new EntityScopedKnowledgeStore(shared, "org-a");
        var theirs = new EntityScopedKnowledgeStore(shared, "org-b");
        await mine.UpsertAsync([new KnowledgeDocument { Id = "a1", Text = "a plan", Metadata = new Dictionary<string, string> { ["kind"] = "plan" } }]);
        await theirs.UpsertAsync([new KnowledgeDocument { Id = "b1", Text = "b plan", Metadata = new Dictionary<string, string> { ["kind"] = "plan" } }]);

        var listed = await mine.ListAsync(new KnowledgeFilter { ["kind"] = "plan" });

        listed.Select(c => c.Id).ShouldBe(["a1"]);
    }

    [Test]
    public async Task ListAsync_OverAStoreThatCantList_Throws()
    {
        var store = new EntityScopedKnowledgeStore(new SearchOnly(), "org-a");

        await Should.ThrowAsync<NotSupportedException>(() => store.ListAsync(new KnowledgeFilter()));
    }

    /// <summary>A store that searches but can't list.</summary>
    private sealed class SearchOnly : IKnowledgeStore
    {
        public Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(string query, SearchOptions? options = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<KnowledgeChunk>>([]);

        public Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default) => Task.CompletedTask;
    }
}
