using Ananke.Orchestration.Knowledge.Embeddings;

namespace Ananke.Orchestration.Knowledge.Tests;

/// <summary><see cref="ChunkIdCases"/> on <see cref="InMemoryKnowledgeStore"/>.</summary>
[TestFixture]
public class InMemoryChunkIdTests
{
    [Test]
    public Task SearchAndList_ADocument_ReturnTheIdUpserted() =>
        ChunkIdCases.SearchAndList_ADocument_ReturnTheIdUpserted(new InMemoryKnowledgeStore(new InMemoryEmbedder()));
}
