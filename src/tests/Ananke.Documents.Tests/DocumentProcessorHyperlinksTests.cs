using System.Text;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Documents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

#pragma warning disable ANNKEEXP004

namespace Ananke.Documents.Tests;

/// <summary><see cref="DocumentProcessor.Hyperlinks"/>: the documents a document links to, stored on its chunks.</summary>
[TestFixture]
public class DocumentProcessorHyperlinksTests
{
    private const string Plan = """
        # Hosted OAuth

        Built on [the token finding](../findings/tokens.md) and [the audit](../findings/audit.md#result).
        See [Gitea](https://gitea.io/docs/oauth.md) and [below](#later) too.

        ## Later

        Nothing linked here.
        """;

    private static async Task<IReadOnlyList<KnowledgeChunk>> IndexAsync(string markdown, string sourceId, HyperlinkOptions? hyperlinks)
    {
        var store = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        var processor = new DocumentProcessor(new HttpClient(), [new MarkdownExtractor()], new SlidingWindowChunker(), store)
        {
            Hyperlinks = hyperlinks,
        };
        await processor.ProcessAsync(new MemoryStream(Encoding.UTF8.GetBytes(markdown)), ".md", sourceId).ConfigureAwait(false);
        return await store.ListAsync(new KnowledgeFilter { ["source"] = sourceId }).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> DocumentLinks(KnowledgeChunk chunk) =>
        [.. chunk.Metadata.Keys.Where(k => k.StartsWith(HyperlinkOptions.KeyPrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

    [Test]
    public async Task ProcessAsync_Hyperlinks_StoresRelativeLinksOnTheChunksOfTheirSection()
    {
        var chunks = await IndexAsync(Plan, "plans/oauth.md", new HyperlinkOptions());

        var linking = chunks.Single(c => c.Text.Contains("Built on", StringComparison.Ordinal));
        DocumentLinks(linking).ShouldBe(["entity:document:findings/audit.md", "entity:document:findings/tokens.md"]);
        linking.Metadata["entity:document:findings/tokens.md"].ShouldBe("document:findings/tokens.md");
        DocumentLinks(chunks.Single(c => c.Text.Contains("Nothing linked", StringComparison.Ordinal))).ShouldBeEmpty();
    }

    [Test]
    public async Task ProcessAsync_WithoutHyperlinks_StoresNoDocumentLinks()
    {
        var chunks = await IndexAsync(Plan, "plans/oauth.md", hyperlinks: null);

        chunks.ShouldNotBeEmpty();
        chunks.ShouldAllBe(c => DocumentLinks(c).Count == 0);
    }

    [Test]
    public async Task ProcessAsync_Hyperlinks_ChunksAsWithout()
    {
        var with = await IndexAsync(Plan, "plans/oauth.md", new HyperlinkOptions());
        var without = await IndexAsync(Plan, "plans/oauth.md", hyperlinks: null);

        with.Select(c => (c.Id, c.Text)).ShouldBe(without.Select(c => (c.Id, c.Text)));
    }

    [Test]
    public async Task ProcessAsync_AResolver_StoresWhatItMapsLinksTo()
    {
        var chunks = await IndexAsync(Plan, "plans/oauth.md", new HyperlinkOptions
        {
            Resolve = (_, uri) => uri.StartsWith("https://gitea.io/", StringComparison.Ordinal) ? "gitea:oauth" : null,
        });

        DocumentLinks(chunks.Single(c => c.Text.Contains("Built on", StringComparison.Ordinal)))
            .ShouldBe(["entity:document:gitea:oauth"]);
    }

    [TestCase("plans/a.md", "b.md", "plans/b.md")]
    [TestCase("plans/a.md", "./b.md", "plans/b.md")]
    [TestCase("plans/a.md", "../findings/b.md?x=1#part", "findings/b.md")]
    [TestCase("plans/a.md", "my%20notes.md", "plans/my notes.md")]
    [TestCase("a.md", "sub/b.md", "sub/b.md")]
    [TestCase("/docs/plans/a.md", "../b.md", "/docs/b.md")]
    [TestCase("https://docs.example.com/plans/a.md", "../b.md#part", "https://docs.example.com/b.md")]
    [TestCase("https://docs.example.com/plans/a.md", "/c.md", "https://docs.example.com/c.md")]
    [TestCase("https://docs.example.com/plans/a.md", "https://docs.example.com/plans/b.md#part", "https://docs.example.com/plans/b.md")]
    [TestCase("https://docs.example.com/plans/a.md", "//docs.example.com/c.md", "https://docs.example.com/c.md")]
    public void ResolveRelative_ARelativeLink_ResolvesAgainstTheSource(string source, string uri, string expected) =>
        HyperlinkOptions.ResolveRelative(source, uri).ShouldBe(expected);

    [TestCase("plans/a.md", "https://example.com/b.md")]
    [TestCase("plans/a.md", "mailto:someone@example.com")]
    [TestCase("plans/a.md", "//example.com/b.md")]
    [TestCase("plans/a.md", "#section")]
    [TestCase("plans/a.md", "a.md#section")]
    [TestCase("plans/a.md", "/b.md")]
    [TestCase("plans/a.md", "../../b.md")]
    [TestCase("plans/a.md", "")]
    [TestCase("https://docs.example.com/plans/a.md", "https://other.example.com/b.md")]
    [TestCase("https://docs.example.com/plans/a.md", "mailto:someone@example.com")]
    [TestCase("https://docs.example.com/plans/a.md", "https://docs.example.com/plans/a.md#top")]
    public void ResolveRelative_NotARelativeLinkToAnotherDocument_IsLeftOut(string source, string uri) =>
        HyperlinkOptions.ResolveRelative(source, uri).ShouldBeNull();
}
