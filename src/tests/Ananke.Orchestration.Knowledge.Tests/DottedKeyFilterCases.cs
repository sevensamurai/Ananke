using Ananke.Orchestration.Knowledge.Documents;
using Shouldly;

#pragma warning disable ANNKEEXP004 // filters with the experimental AnyOf and lists through IListableKnowledgeStore

namespace Ananke.Orchestration.Knowledge.Tests;

/// <summary>
/// A metadata key that holds a dot, shared by each store's tests: a link to a document whose source id ends in
/// <c>.md</c>, as <see cref="DocumentProcessor.Hyperlinks"/> writes it, is matched by an exact filter, by an AnyOf and by
/// a delete on that key.
/// </summary>
public static class DottedKeyFilterCases
{
    private static readonly string LinkKey = HyperlinkOptions.KeyFor("findings/tokens.md");
    private static readonly string LinkValue = HyperlinkOptions.DocumentKind + ":findings/tokens.md";

    private static Task UpsertAsync(IListableKnowledgeStore store) =>
        store.UpsertAsync(
        [
            new KnowledgeDocument
            {
                Id = "audit",
                Text = "The audit links to the token findings",
                Metadata = new Dictionary<string, string> { ["source"] = "findings/audit.md", [LinkKey] = LinkValue },
            },
            new KnowledgeDocument
            {
                Id = "plan",
                Text = "The plan links to nothing",
                Metadata = new Dictionary<string, string> { ["source"] = "plans/plan.md" },
            },
        ]);

    /// <summary>An exact filter and an AnyOf on the dotted key list the document that holds it, and only that one.</summary>
    public static async Task List_ADottedKey_MatchesTheDocumentHoldingIt(IListableKnowledgeStore store)
    {
        await UpsertAsync(store);

        (await store.ListAsync(new KnowledgeFilter { [LinkKey] = LinkValue })).Select(c => c.Id).ShouldBe(["audit"]);
        (await store.ListAsync(new KnowledgeFilter { AnyOf = { [LinkKey] = [LinkValue, "document:other.md"] } }))
            .Select(c => c.Id).ShouldBe(["audit"]);
    }

    /// <summary><see cref="IKnowledgeStore.DeleteAsync"/> with an exact filter on the dotted key removes the document holding it.</summary>
    public static async Task Delete_ADottedKey_RemovesTheDocumentHoldingIt(IListableKnowledgeStore store)
    {
        await UpsertAsync(store);

        await store.DeleteAsync(new KnowledgeFilter { [LinkKey] = LinkValue });

        (await store.ListAsync(new KnowledgeFilter())).Select(c => c.Id).ShouldBe(["plan"]);
    }
}
