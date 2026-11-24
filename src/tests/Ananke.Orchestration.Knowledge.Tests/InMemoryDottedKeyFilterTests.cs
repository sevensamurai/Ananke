using Ananke.Orchestration.Knowledge.Embeddings;

namespace Ananke.Orchestration.Knowledge.Tests;

/// <summary><see cref="DottedKeyFilterCases"/> on <see cref="InMemoryKnowledgeStore"/>.</summary>
[TestFixture]
public class InMemoryDottedKeyFilterTests
{
    [Test]
    public Task List_ADottedKey_MatchesTheDocumentHoldingIt() =>
        DottedKeyFilterCases.List_ADottedKey_MatchesTheDocumentHoldingIt(new InMemoryKnowledgeStore(new InMemoryEmbedder()));

    [Test]
    public Task Delete_ADottedKey_RemovesTheDocumentHoldingIt() =>
        DottedKeyFilterCases.Delete_ADottedKey_RemovesTheDocumentHoldingIt(new InMemoryKnowledgeStore(new InMemoryEmbedder()));
}
