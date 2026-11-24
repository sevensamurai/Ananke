using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Orchestration.Knowledge.Tests;
using Qdrant.Client.Grpc;
using Shouldly;

namespace Ananke.Qdrant.Tests.Containers;

/// <summary><see cref="ChunkIdCases"/> on <see cref="QdrantKnowledgeStore"/>, and a point stored without the document's id.</summary>
[TestFixture]
public class QdrantChunkIdTests
{
    private static QdrantKnowledgeStore NewStore(string collection) =>
        new(QdrantServer.Require(), new InMemoryEmbedder(), collection, vectorSize: 64);

    [Test]
    public Task SearchAndList_ADocument_ReturnTheIdUpserted() =>
        ChunkIdCases.SearchAndList_ADocument_ReturnTheIdUpserted(NewStore(QdrantServer.NewCollection("ids")));

    [Test]
    public async Task SearchAndList_AMetadataKeyNamedLikeTheId_ReturnTheIdUpserted()
    {
        var store = NewStore(QdrantServer.NewCollection("reserved"));
        const string text = "Metadata that names the id";
        await store.UpsertAsync([new KnowledgeDocument
        {
            Id = "doc-b:record:0",
            Text = text,
            Metadata = new Dictionary<string, string> { ["source"] = "doc-b", ["_id"] = "other", ["_text"] = "other" },
        }]);

        var found = await store.SearchAsync(text, new SearchOptions { Mode = SearchMode.Dense });
        found.Select(c => c.Id).ShouldBe(["doc-b:record:0"]);
        found[0].Text.ShouldBe(text);
    }

    [Test]
    public async Task SearchAndList_APointWithoutTheIdInItsPayload_ReturnItWithItsPointId()
    {
        var collection = QdrantServer.NewCollection("legacy");
        var store = NewStore(collection);
        await store.UpsertAsync([]);
        var pointId = Guid.NewGuid().ToString("D");
        const string text = "Written before the id was kept";
        await QdrantServer.Require().UpsertAsync(collection, [new PointStruct
        {
            Id = new PointId { Uuid = pointId },
            Vectors = (await new InMemoryEmbedder().EmbedAsync(text)).ToArray(),
            Payload = { ["_text"] = text, ["source"] = "old.md" },
        }]);

        var found = await store.SearchAsync(text, new SearchOptions { Mode = SearchMode.Dense });
        found.Select(c => c.Id).ShouldBe([pointId]);
        found[0].Text.ShouldBe(text);

        var listed = await store.ListAsync(new KnowledgeFilter { ["source"] = "old.md" });
        listed.Select(c => c.Id).ShouldBe([pointId]);
    }
}
