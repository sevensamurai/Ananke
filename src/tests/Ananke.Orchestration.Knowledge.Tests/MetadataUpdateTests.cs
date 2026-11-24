using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Orchestration.Knowledge.Linking;

#pragma warning disable ANNKEEXP004 // updates through the experimental IListableKnowledgeStore

namespace Ananke.Orchestration.Knowledge.Tests;

/// <summary>
/// <see cref="MetadataUpdateCases"/> on <see cref="InMemoryKnowledgeStore"/> and through the stores that wrap one,
/// <see cref="LinkedKnowledgeStore"/> and <see cref="CatalogAwareKnowledgeStore"/>.
/// </summary>
[TestFixture]
public class MetadataUpdateTests
{
    public enum Wrapping { None, Linked, CatalogAware }

    private static IListableKnowledgeStore NewStore(Wrapping wrapping)
    {
        var embedder = new InMemoryEmbedder();
        var inner = new InMemoryKnowledgeStore(embedder);
        return wrapping switch
        {
            Wrapping.Linked => new LinkedKnowledgeStore(inner, new InMemoryDocumentLinkGraph()),
            Wrapping.CatalogAware => new CatalogAwareKnowledgeStore(inner, new InMemoryKnowledgeCatalog(embedder)),
            _ => inner,
        };
    }

    [TestCase(Wrapping.None)]
    [TestCase(Wrapping.Linked)]
    [TestCase(Wrapping.CatalogAware)]
    public Task UpdateMetadata_AsListed_ReplacesTheMetadataAndKeepsTheText(Wrapping wrapping) =>
        MetadataUpdateCases.UpdateMetadata_AsListed_ReplacesTheMetadataAndKeepsTheText(NewStore(wrapping));

    [TestCase(Wrapping.None)]
    [TestCase(Wrapping.Linked)]
    [TestCase(Wrapping.CatalogAware)]
    public Task UpdateMetadata_WrittenSinceListed_KeepsTheWrite(Wrapping wrapping) =>
        MetadataUpdateCases.UpdateMetadata_WrittenSinceListed_KeepsTheWrite(NewStore(wrapping));

    [TestCase(Wrapping.None)]
    [TestCase(Wrapping.Linked)]
    [TestCase(Wrapping.CatalogAware)]
    public Task UpdateMetadata_DeletedSinceListed_StaysDeleted(Wrapping wrapping) =>
        MetadataUpdateCases.UpdateMetadata_DeletedSinceListed_StaysDeleted(NewStore(wrapping));
}
