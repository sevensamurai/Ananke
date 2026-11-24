using Shouldly;

#pragma warning disable ANNKEEXP004 // lists through IListableKnowledgeStore

namespace Ananke.Orchestration.Knowledge.Tests;

/// <summary>
/// A delete with an empty filter, shared by each store's tests: every store refuses it rather than remove every
/// document.
/// </summary>
public static class EmptyFilterDeleteCases
{
    /// <summary><see cref="IKnowledgeStore.DeleteAsync"/> with an empty filter throws and deletes nothing.</summary>
    public static Task Delete_AnEmptyFilter_ThrowsAndDeletesNothing(IListableKnowledgeStore store) =>
        Delete_AnEmptyFilter_ThrowsAndDeletesNothing(store, store);

    /// <summary>
    /// The same through a store that wraps <paramref name="inner"/>: after a delete with an empty filter through
    /// <paramref name="store"/>, <paramref name="inner"/> holds what it held before.
    /// </summary>
    public static async Task Delete_AnEmptyFilter_ThrowsAndDeletesNothing(IKnowledgeStore store, IListableKnowledgeStore inner)
    {
        await store.UpsertAsync(
        [
            new KnowledgeDocument { Id = "a", Text = "text a", Metadata = new Dictionary<string, string> { ["source"] = "doc-a" } },
            new KnowledgeDocument { Id = "b", Text = "text b", Metadata = new Dictionary<string, string> { ["source"] = "doc-b" } },
        ]);

        var held = (await inner.ListAsync(new KnowledgeFilter())).Select(c => c.Id).Order().ToList();
        held.ShouldContain("a");
        held.ShouldContain("b");

        await Should.ThrowAsync<ArgumentException>(() => store.DeleteAsync(new KnowledgeFilter()));

        (await inner.ListAsync(new KnowledgeFilter())).Select(c => c.Id).Order().ShouldBe(held);
    }
}
