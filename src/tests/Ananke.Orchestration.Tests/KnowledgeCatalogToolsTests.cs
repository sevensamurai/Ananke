using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary><see cref="KnowledgeCatalogTools"/>' <c>list_documents</c>: every document with a header field's value, newest first.</summary>
[TestFixture]
public class KnowledgeCatalogToolsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static CatalogEntry Entry(string source, string type, string status, DateTimeOffset? effectiveAt, string? supersededBy = null) => new()
    {
        Source = source,
        Summary = "",
        Keywords = [],
        IndexedAt = Now,
        EffectiveAt = effectiveAt,
        ChunkCount = 1,
        SupersededBy = supersededBy,
        Fields = new Dictionary<string, string> { ["Type"] = type, ["Status"] = status },
    };

    [Test]
    public async Task ListDocuments_AFieldAndValue_ListsEveryDocumentWithItNewestFirst()
    {
        var catalog = new InMemoryKnowledgeCatalog(new InMemoryEmbedder());
        await catalog.IndexAsync(Entry("plan-old.md", "plan", "Closed", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), supersededBy: "plan-new.md"));
        await catalog.IndexAsync(Entry("plan-new.md", "plan", "Open", new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero)));
        await catalog.IndexAsync(Entry("adr-1.md", "adr", "Accepted", new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero)));

        var result = await KnowledgeCatalogTools.Create(catalog).Tools["list_documents"]
            .ExecuteAsync(new Dictionary<string, object?> { ["field"] = "type", ["value"] = "plan" });

        result.Value.ShouldContain("2 document(s)");
        result.Value.IndexOf("plan-new.md", StringComparison.Ordinal).ShouldBeLessThan(result.Value.IndexOf("plan-old.md", StringComparison.Ordinal));
        result.Value.ShouldContain("2026-11-02");
        result.Value.ShouldContain("Status: Open");
        result.Value.ShouldContain("Superseded by: plan-new.md");
        result.Value.ShouldNotContain("adr-1.md");
    }

    [Test]
    public async Task ListDocuments_NoMatch_SaysSo()
    {
        var result = await KnowledgeCatalogTools.Create(new InMemoryKnowledgeCatalog(new InMemoryEmbedder())).Tools["list_documents"]
            .ExecuteAsync(new Dictionary<string, object?> { ["field"] = "type", ["value"] = "plan" });

        result.Value.ShouldContain("No document");
    }
}
