using Ananke.Learning.EntityMemory;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

#pragma warning disable ANNKEEXP004 // KnowledgeFilter.AnyOf and listing are experimental

namespace Ananke.Learning.Tests;

/// <summary>
/// <see cref="EntityScopedKnowledgeStore"/> passes a filter's <see cref="KnowledgeFilter.AnyOf"/> to the store it
/// scopes, in search, listing and delete, and still keeps to its own entity.
/// </summary>
[TestFixture]
public class EntityScopedKnowledgeStoreAnyOfTests
{
    private InMemoryKnowledgeStore _shared = null!;
    private EntityScopedKnowledgeStore _mine = null!;

    [SetUp]
    public async Task SetUp()
    {
        _shared = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        _mine = new EntityScopedKnowledgeStore(_shared, "org-a");
        var theirs = new EntityScopedKnowledgeStore(_shared, "org-b");
        await _mine.UpsertAsync([Plan("a-accepted", "accepted"), Plan("a-proposed", "proposed"), Plan("a-parked", "parked")]);
        await theirs.UpsertAsync([Plan("b-accepted", "accepted")]);
    }

    private static KnowledgeDocument Plan(string id, string status) => new()
    {
        Id = id,
        Text = $"plan {status}",
        Metadata = new Dictionary<string, string> { ["status"] = status },
    };

    private static KnowledgeFilter AcceptedOrProposed() => new() { AnyOf = { ["status"] = ["accepted", "proposed"] } };

    [Test]
    public async Task SearchAsync_SeveralValuesForOneKey_PassesThemThrough()
    {
        var results = await _mine.SearchAsync("plan", new SearchOptions { Mode = SearchMode.Sparse, TopK = 10, Filter = AcceptedOrProposed() });

        results.Select(r => r.Id).Order().ShouldBe(["a-accepted", "a-proposed"]);
    }

    [Test]
    public async Task ListAsync_SeveralValuesForOneKey_PassesThemThrough()
    {
        (await _mine.ListAsync(AcceptedOrProposed())).Select(c => c.Id).ShouldBe(["a-accepted", "a-proposed"]);
    }

    [Test]
    public async Task DeleteAsync_SeveralValuesForOneKey_PassesThemThrough()
    {
        await _mine.DeleteAsync(AcceptedOrProposed());

        (await _shared.ListAsync(new KnowledgeFilter())).Select(c => c.Id).ShouldBe(["a-parked", "b-accepted"]);
    }

    [Test]
    public async Task ListAsync_SeveralEntitiesAsked_StillListsOnlyItsOwn()
    {
        var filter = new KnowledgeFilter { AnyOf = { [EntityScopedKnowledgeStore.EntityIdMetadataKey] = ["org-a", "org-b"] } };

        (await _mine.ListAsync(filter)).Select(c => c.Id).ShouldBe(["a-accepted", "a-parked", "a-proposed"]);
    }
}
