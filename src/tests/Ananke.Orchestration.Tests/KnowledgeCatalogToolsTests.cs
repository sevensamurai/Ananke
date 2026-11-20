using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Documents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// <see cref="KnowledgeCatalogTools"/>' <c>list_documents</c>: every document with a header field's value, newest first;
/// and <c>list_linked_documents</c>: the documents one links to.
/// </summary>
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
    public async Task ListDocuments_MoreThanItShows_ShowsTheNewestAndSaysMoreMatch()
    {
        var catalog = new InMemoryKnowledgeCatalog(new InMemoryEmbedder());
        for (var day = 0; day < 205; day++)
            await catalog.IndexAsync(Entry($"plan-{day:D3}.md", "plan", "Open", Now.AddDays(-day)));

        var result = await KnowledgeCatalogTools.Create(catalog).Tools["list_documents"]
            .ExecuteAsync(new Dictionary<string, object?> { ["field"] = "type", ["value"] = "plan" });

        result.Value.ShouldStartWith("More than 200 documents match; the newest 200:");
        result.Value.ShouldContain("plan-000.md");
        result.Value.ShouldContain("plan-199.md");
        result.Value.ShouldNotContain("plan-200.md");
    }

    [Test]
    public async Task ListDocuments_NoMatch_SaysSo()
    {
        var result = await KnowledgeCatalogTools.Create(new InMemoryKnowledgeCatalog(new InMemoryEmbedder())).Tools["list_documents"]
            .ExecuteAsync(new Dictionary<string, object?> { ["field"] = "type", ["value"] = "plan" });

        result.Value.ShouldContain("No document");
    }

#pragma warning disable ANNKEEXP004
    private static KnowledgeDocument Linking(string source, params string[] targets) => new()
    {
        Id = $"{source}:chunk:0",
        Text = source,
        Metadata = new Dictionary<string, string>(targets.Select(t => KeyValuePair.Create(HyperlinkOptions.KeyFor(t), $"document:{t}")))
        {
            ["source"] = source,
        },
    };

    [Test]
    public async Task ListLinkedDocuments_ADocumentWithLinks_ListsThemNewestFirstWithDateAndSupersession()
    {
        var store = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        await store.UpsertAsync([Linking("plan-new.md", "finding-a.md", "finding-b.md", "elsewhere.md"), Linking("finding-a.md"), Linking("finding-b.md")]);
        var catalog = new InMemoryKnowledgeCatalog(new InMemoryEmbedder());
        await catalog.IndexAsync(Entry("finding-a.md", "finding", "Done", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), supersededBy: "finding-b.md"));
        await catalog.IndexAsync(Entry("finding-b.md", "finding", "Done", new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero)));

        var result = await KnowledgeCatalogTools.CreateLinks(new LinkedDocumentReader(store, catalog)).Tools["list_linked_documents"]
            .ExecuteAsync(new Dictionary<string, object?> { ["source"] = "plan-new.md" });

        result.Value.ShouldContain("plan-new.md links to 3 document(s)");
        result.Value.IndexOf("finding-b.md — 2026-10-20", StringComparison.Ordinal)
            .ShouldBeLessThan(result.Value.IndexOf("finding-a.md — 2026-10-01 — Superseded by: finding-b.md", StringComparison.Ordinal));
        result.Value.ShouldContain("elsewhere.md — not indexed");
    }

    [Test]
    public async Task ListLinkedDocuments_NoLinks_SaysSo()
    {
        var store = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        await store.UpsertAsync([Linking("plan-new.md")]);

        var result = await KnowledgeCatalogTools.CreateLinks(new LinkedDocumentReader(store)).Tools["list_linked_documents"]
            .ExecuteAsync(new Dictionary<string, object?> { ["source"] = "plan-new.md" });

        result.Value.ShouldStartWith("No links");
    }
#pragma warning restore ANNKEEXP004
}
