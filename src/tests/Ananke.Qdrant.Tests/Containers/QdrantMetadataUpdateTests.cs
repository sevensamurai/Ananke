using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Orchestration.Knowledge.Tests;

namespace Ananke.Qdrant.Tests.Containers;

/// <summary><see cref="MetadataUpdateCases"/> on <see cref="QdrantKnowledgeStore"/>.</summary>
[TestFixture]
public class QdrantMetadataUpdateTests
{
    private static QdrantKnowledgeStore NewStore() =>
        new(QdrantServer.Require(), new InMemoryEmbedder(), QdrantServer.NewCollection("updates"), vectorSize: 64);

    [Test]
    public Task UpdateMetadata_AsListed_ReplacesTheMetadataAndKeepsTheText() =>
        MetadataUpdateCases.UpdateMetadata_AsListed_ReplacesTheMetadataAndKeepsTheText(NewStore());

    [Test]
    public Task UpdateMetadata_WrittenSinceListed_KeepsTheWrite() =>
        MetadataUpdateCases.UpdateMetadata_WrittenSinceListed_KeepsTheWrite(NewStore());

    [Test]
    public Task UpdateMetadata_DeletedSinceListed_StaysDeleted() =>
        MetadataUpdateCases.UpdateMetadata_DeletedSinceListed_StaysDeleted(NewStore());
}
