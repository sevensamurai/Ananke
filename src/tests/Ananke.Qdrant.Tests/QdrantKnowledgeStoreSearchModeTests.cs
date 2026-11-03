using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Embeddings;
using Qdrant.Client;
using Shouldly;

namespace Ananke.Qdrant.Tests;

/// <summary>
/// <see cref="QdrantKnowledgeStore"/> searches only by embedding, so it refuses the keyword and hybrid modes before
/// anything is sent. No Qdrant connection is needed: the client connects on its first call.
/// </summary>
[TestFixture]
public class QdrantKnowledgeStoreSearchModeTests
{
    [TestCase(SearchMode.Sparse)]
    [TestCase(SearchMode.Hybrid)]
    public async Task SearchAsync_ModeOtherThanDense_ThrowsNotSupported(SearchMode mode)
    {
        using var client = new QdrantClient("localhost");
        var store = new QdrantKnowledgeStore(client, new InMemoryEmbedder(), vectorSize: 64);

        var error = await Should.ThrowAsync<NotSupportedException>(
            () => store.SearchAsync("query", new SearchOptions { Mode = mode }));

        error.Message.ShouldContain(mode.ToString());
    }
}
