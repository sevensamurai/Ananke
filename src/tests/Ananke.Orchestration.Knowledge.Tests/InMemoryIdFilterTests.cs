using Ananke.Orchestration.Knowledge.Embeddings;

namespace Ananke.Orchestration.Knowledge.Tests;

/// <summary><see cref="IdFilterCases"/> on <see cref="InMemoryKnowledgeStore"/>.</summary>
[TestFixture]
public class InMemoryIdFilterTests
{
    [Test]
    public Task ListSearchAndDelete_ById_TouchExactlyThoseChunks() =>
        IdFilterCases.ListSearchAndDelete_ById_TouchExactlyThoseChunks(new InMemoryKnowledgeStore(new InMemoryEmbedder()));

    [Test]
    public Task LinkedSearch_DocumentsIndexedThroughTheProcessor_ReturnsTheLinkedChunk() =>
        IdFilterCases.LinkedSearch_DocumentsIndexedThroughTheProcessor_ReturnsTheLinkedChunk(new InMemoryKnowledgeStore(new InMemoryEmbedder()));

    [Test]
    public Task LinkedSearch_LinkedChunkWithNegativeCosine_IsReturned() =>
        IdFilterCases.LinkedSearch_LinkedChunkWithNegativeCosine_IsReturned(embedder => new InMemoryKnowledgeStore(embedder));
}
