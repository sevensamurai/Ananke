using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Orchestration.Knowledge.Tests;

namespace Ananke.Qdrant.Tests.Containers;

/// <summary><see cref="IdFilterCases"/> on <see cref="QdrantKnowledgeStore"/>.</summary>
[TestFixture]
public class QdrantIdFilterTests
{
    private static QdrantKnowledgeStore NewStore() =>
        new(QdrantServer.Require(), new InMemoryEmbedder(), QdrantServer.NewCollection("ids"), vectorSize: 64);

    [Test]
    public Task ListSearchAndDelete_ById_TouchExactlyThoseChunks() =>
        IdFilterCases.ListSearchAndDelete_ById_TouchExactlyThoseChunks(NewStore());

    [Test]
    public Task LinkedSearch_DocumentsIndexedThroughTheProcessor_ReturnsTheLinkedChunk() =>
        IdFilterCases.LinkedSearch_DocumentsIndexedThroughTheProcessor_ReturnsTheLinkedChunk(NewStore());

    [Test]
    public Task LinkedSearch_LinkedChunkWithNegativeCosine_IsReturned() =>
        IdFilterCases.LinkedSearch_LinkedChunkWithNegativeCosine_IsReturned(embedder => new QdrantKnowledgeStore(
            QdrantServer.Require(), embedder, QdrantServer.NewCollection("ids"), vectorSize: IdFilterCases.OppositeEmbedder.Dimensions));
}
