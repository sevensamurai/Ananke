using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Documents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

#pragma warning disable ANNKEEXP004

namespace Ananke.Orchestration.Knowledge.Tests.Documents;

/// <summary><see cref="LinkedDocumentReader"/>: the documents a document links to, from the links stored on its chunks.</summary>
[TestFixture]
public class LinkedDocumentReaderTests
{
    private static readonly DateTimeOffset Indexed = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private static KnowledgeDocument Chunk(string source, int i, string? date = null, params string[] links)
    {
        var metadata = new Dictionary<string, string> { ["source"] = source };
        if (date is not null)
            metadata[DocumentHeaderOptions.EffectiveAtKey] = $"{date}T00:00:00.0000000+00:00";
        foreach (var link in links)
            metadata[HyperlinkOptions.KeyFor(link)] = $"{HyperlinkOptions.DocumentKind}:{link}";
        return new KnowledgeDocument { Id = $"{source}:chunk:{i}", Text = $"{source} {i}", Metadata = metadata };
    }

    private static async Task<InMemoryKnowledgeStore> StoreAsync()
    {
        var store = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        await store.UpsertAsync(
        [
            Chunk("plans/oauth.md", 0, "2026-11-01", "findings/tokens.md", "findings/audit.md"),
            Chunk("plans/oauth.md", 1, "2026-11-01", "findings/tokens.md", "findings/missing.md"),
            Chunk("plans/oauth.md", 2, "2026-11-01"),
            Chunk("findings/tokens.md", 0, "2026-10-02"),
            Chunk("findings/audit.md", 0, "2026-10-20"),
            Chunk("plans/other.md", 0, "2026-11-02"),
        ]).ConfigureAwait(false);
        return store;
    }

    [Test]
    public async Task ListAsync_ADocumentWithLinks_ListsEachDocumentOnceNewestFirst()
    {
        var linked = await new LinkedDocumentReader(await StoreAsync()).ListAsync("plans/oauth.md");

        linked.Select(d => d.Source).ShouldBe(["findings/audit.md", "findings/tokens.md", "findings/missing.md"]);
        linked[0].EffectiveAt.ShouldBe(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero));
        linked[0].Indexed.ShouldBeTrue();
        linked[2].Indexed.ShouldBeFalse();
        linked[2].EffectiveAt.ShouldBeNull();
    }

    [Test]
    public async Task ListAsync_ACatalog_TakesItsDateAndWhatSupersedes()
    {
        var catalog = new InMemoryKnowledgeCatalog(new InMemoryEmbedder());
        await catalog.IndexAsync(new CatalogEntry
        {
            Source = "findings/tokens.md",
            Summary = "",
            Keywords = [],
            IndexedAt = Indexed,
            ChunkCount = 1,
            EffectiveAt = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero),
            SupersededBy = "findings/tokens-2.md",
        });

        var linked = await new LinkedDocumentReader(await StoreAsync(), catalog).ListAsync("plans/oauth.md");

        linked[0].Source.ShouldBe("findings/tokens.md");
        linked[0].SupersededBy.ShouldBe("findings/tokens-2.md");
        linked[0].IndexedAt.ShouldBe(Indexed);
        linked[1].Source.ShouldBe("findings/audit.md");
        linked[1].IndexedAt.ShouldBeNull();
    }

    [Test]
    public async Task ListAsync_NoLinks_IsEmpty()
    {
        var reader = new LinkedDocumentReader(await StoreAsync());

        (await reader.ListAsync("plans/other.md")).ShouldBeEmpty();
        (await reader.ListAsync("not-indexed.md")).ShouldBeEmpty();
    }
}
