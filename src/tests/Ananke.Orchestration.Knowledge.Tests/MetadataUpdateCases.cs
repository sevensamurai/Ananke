using Shouldly;

#pragma warning disable ANNKEEXP004 // updates through the experimental IListableKnowledgeStore

namespace Ananke.Orchestration.Knowledge.Tests;

/// <summary>
/// <see cref="IListableKnowledgeStore.UpdateMetadataAsync"/>, shared by each store's tests and the stores that wrap one:
/// the metadata of a chunk still as it was listed is replaced and its text kept; a chunk written again or deleted since
/// it was listed is left as that write or delete made it.
/// </summary>
public static class MetadataUpdateCases
{
    private const string Id = "doc-a:note:1";
    private const string Text = "The release ships on Friday";

    /// <summary>A key holding characters a store may read as a path (Qdrant's <c>.</c>).</summary>
    private const string DottedKey = "entity:document:notes/a.md";

    /// <summary>A key holding a character a store may refuse in a filter (Qdrant's <c>"</c>), kept by the update.</summary>
    private const string QuotedKey = "say \"when\"";

    private static Dictionary<string, string> Listed() => new()
    {
        ["source"] = "doc-a",
        ["kind"] = "a",
        ["quote"] = "ships on Friday",
        [DottedKey] = "1",
        [QuotedKey] = "1",
    };

    private static readonly Dictionary<string, string> Marked = new()
    {
        ["source"] = "doc-a",
        ["kind"] = "b",
        ["passage_gone"] = "true",
        [QuotedKey] = "1",
    };

    private static async Task<KnowledgeChunk> UpsertAndListAsync(IListableKnowledgeStore store)
    {
        await store.UpsertAsync([new KnowledgeDocument { Id = Id, Text = Text, Metadata = Listed() }]).ConfigureAwait(false);
        return (await store.ListAsync(new KnowledgeFilter { ["source"] = "doc-a" }).ConfigureAwait(false)).Single();
    }

    private static async Task<KnowledgeChunk?> StoredAsync(IListableKnowledgeStore store) =>
        (await store.ListAsync(new KnowledgeFilter { ["source"] = "doc-a" }).ConfigureAwait(false)).SingleOrDefault();

    /// <summary>A chunk as it was listed gets the new metadata (a key changed, one added, two removed) and keeps its text.</summary>
    public static async Task UpdateMetadata_AsListed_ReplacesTheMetadataAndKeepsTheText(IListableKnowledgeStore store)
    {
        var listed = await UpsertAndListAsync(store);

        await store.UpdateMetadataAsync(listed, Marked);

        var stored = await StoredAsync(store);
        stored.ShouldNotBeNull();
        stored.Text.ShouldBe(Text);
        stored.Metadata.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ShouldBe(Marked.OrderBy(kv => kv.Key, StringComparer.Ordinal));
    }

    /// <summary>A chunk written again since it was listed, with new text or a new value, keeps that write.</summary>
    public static async Task UpdateMetadata_WrittenSinceListed_KeepsTheWrite(IListableKnowledgeStore store)
    {
        var listed = await UpsertAndListAsync(store);
        await store.UpsertAsync([new KnowledgeDocument { Id = Id, Text = "The release ships on Monday", Metadata = Listed() }]);

        await store.UpdateMetadataAsync(listed, Marked);

        var edited = await StoredAsync(store);
        edited.ShouldNotBeNull();
        edited.Text.ShouldBe("The release ships on Monday");
        edited.Metadata.ShouldNotContainKey("passage_gone");

        listed = edited;
        var rewritten = Listed();
        rewritten["kind"] = "c";
        await store.UpsertAsync([new KnowledgeDocument { Id = Id, Text = listed.Text, Metadata = rewritten }]);

        await store.UpdateMetadataAsync(listed, Marked);

        var stored = await StoredAsync(store);
        stored.ShouldNotBeNull();
        stored.Metadata["kind"].ShouldBe("c");
        stored.Metadata.ShouldNotContainKey("passage_gone");
        stored.Metadata.ShouldContainKey("quote");
    }

    /// <summary>A chunk deleted since it was listed stays deleted.</summary>
    public static async Task UpdateMetadata_DeletedSinceListed_StaysDeleted(IListableKnowledgeStore store)
    {
        var listed = await UpsertAndListAsync(store);
        await store.DeleteAsync(new KnowledgeFilter { ["source"] = "doc-a", ["kind"] = "a" });

        await store.UpdateMetadataAsync(listed, Marked);

        (await StoredAsync(store)).ShouldBeNull();
    }
}
