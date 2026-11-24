using System.Text;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge.Documents;
using Ananke.Orchestration.Knowledge.Linking;
using Shouldly;

#pragma warning disable ANNKEEXP004 // filters with the experimental AnyOf and lists through IListableKnowledgeStore

namespace Ananke.Orchestration.Knowledge.Tests;

/// <summary>
/// The key <c>id</c> in a filter, shared by each store's tests: it is the chunk's own id, whether or not the chunk's
/// metadata holds an <c>id</c>, so <see cref="LinkedKnowledgeStore"/> finds a linked chunk on every store.
/// </summary>
public static class IdFilterCases
{
    /// <summary>
    /// An exact filter and an AnyOf on <c>id</c> list, search and delete exactly the chunks with those ids, in whichever
    /// order the store lists.
    /// </summary>
    public static async Task ListSearchAndDelete_ById_TouchExactlyThoseChunks(IListableKnowledgeStore store)
    {
        await store.UpsertAsync(
        [
            Document("a", "alpha notes"),
            Document("b", "beta notes"),
            Document("c", "gamma notes"),
            // A metadata key named id is not the chunk's id.
            new KnowledgeDocument { Id = "d", Text = "delta notes", Metadata = new Dictionary<string, string> { ["source"] = "d.md", ["id"] = "b" } },
        ]);

        (await store.ListAsync(new KnowledgeFilter { ["id"] = "b" })).Select(c => c.Id).ShouldBe(["b"]);
        (await store.ListAsync(new KnowledgeFilter { AnyOf = { ["id"] = ["a", "c", "missing"] } })).Select(c => c.Id).ShouldBe(["a", "c"], ignoreOrder: true);
        (await store.ListAsync(new KnowledgeFilter { ["id"] = "a", ["source"] = "b.md" })).ShouldBeEmpty();
        (await store.ListAsync(new KnowledgeFilter { ["id"] = "a", AnyOf = { ["id"] = ["a", "b"] } })).Select(c => c.Id).ShouldBe(["a"]);
        (await store.ListAsync(new KnowledgeFilter { AnyOf = { ["id"] = [] } })).ShouldBeEmpty();

        var found = await store.SearchAsync("notes", new SearchOptions { TopK = 10, Filter = new KnowledgeFilter { ["id"] = "b" } });
        found.Select(c => c.Id).ShouldBe(["b"]);
        found = await store.SearchAsync("notes", new SearchOptions { TopK = 10, Filter = new KnowledgeFilter { AnyOf = { ["id"] = ["a", "c"] } } });
        found.Select(c => c.Id).ShouldBe(["a", "c"], ignoreOrder: true);

        await store.DeleteAsync(new KnowledgeFilter { ["id"] = "a" });
        (await store.ListAsync(new KnowledgeFilter())).Select(c => c.Id).ShouldBe(["b", "c", "d"], ignoreOrder: true);

        await store.DeleteAsync(new KnowledgeFilter { AnyOf = { ["id"] = ["b", "d"] } });
        (await store.ListAsync(new KnowledgeFilter())).Select(c => c.Id).ShouldBe(["c"]);

        static KnowledgeDocument Document(string id, string text) =>
            new() { Id = id, Text = text, Metadata = new Dictionary<string, string> { ["source"] = $"{id}.md" } };
    }

    /// <summary>
    /// Two documents indexed through <see cref="DocumentProcessor"/>, which writes no <c>id</c> into a chunk's metadata,
    /// the first's chunk linked to the second's: <see cref="LinkedKnowledgeStore.SearchAsync"/> for the first returns the
    /// second's chunk, which the search alone, held to the first by its score threshold, doesn't.
    /// </summary>
    public static async Task LinkedSearch_DocumentsIndexedThroughTheProcessor_ReturnsTheLinkedChunk(IListableKnowledgeStore store)
    {
        var processor = new DocumentProcessor(new HttpClient(), [new Plain()], new SlidingWindowChunker(), store);
        await processor.ProcessAsync(new MemoryStream(Encoding.UTF8.GetBytes("alpha beta gamma delta")), ".txt", "first.txt");
        await processor.ProcessAsync(new MemoryStream(Encoding.UTF8.GetBytes("alpha beta gamma epsilon")), ".txt", "second.txt");
        var first = (await store.ListAsync(new KnowledgeFilter { ["source"] = "first.txt" })).ShouldHaveSingleItem();
        var second = (await store.ListAsync(new KnowledgeFilter { ["source"] = "second.txt" })).ShouldHaveSingleItem();
        first.Metadata.ShouldNotContainKey("id");

        var graph = new InMemoryDocumentLinkGraph();
        await graph.AddLinkAsync(first.Id, second.Id, "references");
        var options = new SearchOptions { TopK = 5, ScoreThreshold = 0.999f };

        (await store.SearchAsync(first.Text, options)).Select(c => c.Id).ShouldBe([first.Id]);
        (await new LinkedKnowledgeStore(store, graph).SearchAsync(first.Text, options)).Select(c => c.Id).ShouldBe([first.Id, second.Id]);
    }

    /// <summary>
    /// A chunk linked from a seed chunk whose cosine to the query is -1, under the default score threshold of 0 that
    /// holds it out of the search: <see cref="LinkedKnowledgeStore.SearchAsync"/> still returns it. The store is built on
    /// the <see cref="OppositeEmbedder"/> it is given, of <see cref="OppositeEmbedder.Dimensions"/> dimensions.
    /// </summary>
    public static async Task LinkedSearch_LinkedChunkWithNegativeCosine_IsReturned(Func<IEmbeddingModel, IListableKnowledgeStore> create)
    {
        var store = create(new OppositeEmbedder());
        await store.UpsertAsync(
        [
            new KnowledgeDocument { Id = "near", Text = "near" },
            new KnowledgeDocument { Id = "far", Text = "opposite" },
        ]);
        var graph = new InMemoryDocumentLinkGraph();
        await graph.AddLinkAsync("near", "far", "references");
        var options = new SearchOptions { TopK = 5 };

        (await store.SearchAsync("near", options)).Select(c => c.Id).ShouldBe(["near"]);
        (await new LinkedKnowledgeStore(store, graph).SearchAsync("near", options)).Select(c => c.Id).ShouldBe(["near", "far"], ignoreOrder: true);
    }

    /// <summary>The text "opposite" embeds to the opposite of every other text's vector: a cosine of -1.</summary>
    public sealed class OppositeEmbedder : IEmbeddingModel
    {
        /// <summary>The vector size every store of the cases is built for.</summary>
        public const int Dimensions = 64;

        public EmbeddingModelIdentity? Identity { get; } = new("test", "opposite", Dimensions);

        public Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default) =>
            Task.FromResult<ReadOnlyMemory<float>>(Vector(text));

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>([.. texts.Select(t => (ReadOnlyMemory<float>)Vector(t))]);

        private static float[] Vector(string text)
        {
            var vector = new float[Dimensions];
            vector[0] = text == "opposite" ? -1f : 1f;
            return vector;
        }
    }

    /// <summary>The whole text as one section.</summary>
    private sealed class Plain : IDocumentExtractor
    {
        public bool CanExtract(string fileExtension) => true;

        public async Task<ExtractedDocument> ExtractAsync(Stream data, CancellationToken ct = default) =>
            new([new ExtractedSection(await new StreamReader(data).ReadToEndAsync(ct))]);
    }
}
