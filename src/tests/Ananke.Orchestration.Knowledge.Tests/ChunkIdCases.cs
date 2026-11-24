using Ananke.Abstractions.Agents;
using Shouldly;

#pragma warning disable ANNKEEXP004 // lists through the experimental IListableKnowledgeStore

namespace Ananke.Orchestration.Knowledge.Tests;

/// <summary>A chunk's id, shared by each store's tests: the id a document was upserted with is the id it comes back with.</summary>
public static class ChunkIdCases
{
    /// <summary>The id upserted, as <c>DatedRecordExtractor</c> writes a record's.</summary>
    public const string Id = "doc-a:record:1";

    private const string Text = "The release ships on Friday";

    /// <summary><see cref="IKnowledgeStore.SearchAsync"/> and <see cref="IListableKnowledgeStore.ListAsync"/> return the id upserted.</summary>
    public static async Task SearchAndList_ADocument_ReturnTheIdUpserted(IListableKnowledgeStore store)
    {
        await store.UpsertAsync([new KnowledgeDocument
        {
            Id = Id,
            Text = Text,
            Metadata = new Dictionary<string, string> { ["source"] = "doc-a" },
        }]);

        var found = await store.SearchAsync(Text, new SearchOptions { Mode = SearchMode.Dense });
        found.Select(c => c.Id).ShouldBe([Id]);

        var listed = await store.ListAsync(new KnowledgeFilter { ["source"] = "doc-a" });
        listed.Select(c => c.Id).ShouldBe([Id]);
        listed[0].Metadata.Keys.ShouldBe(["source"]);
    }
}
