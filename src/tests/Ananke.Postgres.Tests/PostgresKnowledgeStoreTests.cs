using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge;
using Shouldly;

namespace Ananke.Postgres.Tests;

/// <summary>
/// <see cref="PostgresKnowledgeStore"/> against Postgres with pgvector. The embedder returns fixed vectors, so the row
/// nearest the query in embedding (<c>overview</c>) differs from the one that shares its words (<c>identity</c>).
/// </summary>
[TestFixture]
public class PostgresKnowledgeStoreTests
{
    private const string Query = "Which library is Core.Identity built on?";

    private static readonly KnowledgeDocument Overview = new()
    {
        Id = "overview",
        Text = "Token server overview: the service signs tokens for the apps",
    };

    private static readonly KnowledgeDocument Identity = new()
    {
        Id = "identity",
        Text = "Core.Identity is built on OpenIddict",
        Metadata = new Dictionary<string, string> { ["kind"] = "design", ["source"] = "0001.md" },
    };

    private static readonly KnowledgeDocument Unrelated = new()
    {
        Id = "unrelated",
        Text = "Release notes for the web app",
    };

    private static readonly Dictionary<string, float[]> Vectors = new()
    {
        [Query] = [0.9f, 0.1f, 0f],
        [Overview.Text] = [1f, 0f, 0f],
        [Identity.Text] = [0f, 1f, 0f],
        [Unrelated.Text] = [0f, 0f, 1f],
    };

    private PostgresKnowledgeStore _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _store = new PostgresKnowledgeStore(
            PostgresDatabase.Require(),
            new FixedEmbedder(Vectors, new EmbeddingModelIdentity("test", "fixed", 3)),
            new PostgresKnowledgeOptions { Table = PostgresDatabase.NewTable("chunks") });
        await _store.UpsertAsync([Overview, Identity, Unrelated]);
    }

    [Test]
    public async Task SearchAsync_DenseMode_RanksByEmbedding()
    {
        var results = await _store.SearchAsync(Query, new SearchOptions { TopK = 3 });

        results.Select(r => r.Id).ShouldBe(["overview", "identity", "unrelated"]);
        results[0].Score.ShouldBe(0.9939f, tolerance: 1e-3f);
    }

    [Test]
    public async Task SearchAsync_DenseMode_KeepsTextAndMetadata()
    {
        var results = await _store.SearchAsync(Query, new SearchOptions { TopK = 3 });

        var identity = results.Single(r => r.Id == "identity");
        identity.Text.ShouldBe(Identity.Text);
        identity.Metadata["source"].ShouldBe("0001.md");
    }

    [Test]
    public async Task SearchAsync_SparseMode_RanksBySharedWords()
    {
        var results = await _store.SearchAsync(Query, new SearchOptions { Mode = SearchMode.Sparse, TopK = 3 });

        results.Select(r => r.Id).ShouldBe(["identity"]);
    }

#pragma warning disable ANNKEEXP004
    [Test]
    public async Task ListAsync_AFilter_ListsTheRowsWhoseMetadataHasIt()
    {
        var listed = await _store.ListAsync(new KnowledgeFilter { ["kind"] = "design" });

        listed.Select(r => r.Id).ShouldBe(["identity"]);
        listed[0].Metadata["source"].ShouldBe("0001.md");
        listed[0].Score.ShouldBe(0f);
    }

    [Test]
    public async Task ListAsync_NoFilter_ListsEveryRowByIdUpToTheLimit()
    {
        (await _store.ListAsync(new KnowledgeFilter())).Select(r => r.Id).ShouldBe(["identity", "overview", "unrelated"]);
        (await _store.ListAsync(new KnowledgeFilter(), limit: 2)).Select(r => r.Id).ShouldBe(["identity", "overview"]);
    }
#pragma warning restore ANNKEEXP004

    [Test]
    public async Task SearchAsync_MaxPerSource_KeepsAtMostThatManyFromOneDocument()
    {
        await _store.UpsertAsync([Identity with { Id = "identity-2", Text = "Core.Identity is built on OpenIddict, again" }]);

        var results = await _store.SearchAsync(Query, new SearchOptions { Mode = SearchMode.Sparse, TopK = 3, MaxPerSource = 1 });

        results.Count(r => r.Metadata.GetValueOrDefault("source") == "0001.md").ShouldBe(1);
    }

    [Test]
    public async Task SearchAsync_SparseMode_AQueryWithNoWords_FindsNothing()
    {
        var results = await _store.SearchAsync("?!", new SearchOptions { Mode = SearchMode.Sparse });

        results.ShouldBeEmpty();
    }

    [Test]
    public async Task SearchAsync_HybridMode_FusesBothRankings()
    {
        var results = await _store.SearchAsync(Query, new SearchOptions { Mode = SearchMode.Hybrid, TopK = 2 });

        results.Select(r => r.Id).ShouldBe(["identity", "overview"]);
    }

    [Test]
    public async Task SearchAsync_Filter_MatchesRowsWithEveryKeyAndValue()
    {
        var options = new SearchOptions { TopK = 3, Filter = new KnowledgeFilter { ["kind"] = "design", ["source"] = "0001.md" } };

        var results = await _store.SearchAsync(Query, options);

        results.Select(r => r.Id).ShouldBe(["identity"]);
    }

    [Test]
    public async Task SearchAsync_ScoreThreshold_LeavesOutFartherRows()
    {
        var results = await _store.SearchAsync(Query, new SearchOptions { TopK = 3, ScoreThreshold = 0.5f });

        results.Select(r => r.Id).ShouldBe(["overview"]);
    }

    [Test]
    public async Task UpsertAsync_SameId_Replaces()
    {
        Vectors["Core.Identity is built on ASP.NET Core Identity"] = [0f, 1f, 0f];
        await _store.UpsertAsync([Identity with { Text = "Core.Identity is built on ASP.NET Core Identity" }]);

        var results = await _store.SearchAsync("OpenIddict", new SearchOptions { Mode = SearchMode.Sparse });

        results.ShouldBeEmpty();
    }

    [Test]
    public async Task DeleteAsync_ByFilter_RemovesTheRows()
    {
        await _store.DeleteAsync(new KnowledgeFilter { ["source"] = "0001.md" });

        var results = await _store.SearchAsync(Query, new SearchOptions { TopK = 3 });

        results.Select(r => r.Id).ShouldBe(["overview", "unrelated"]);
    }

    [Test]
    public async Task UpsertAsync_AnotherEmbedderOnTheSameTable_IsRefused()
    {
        var table = PostgresDatabase.NewTable("stamped");
        var first = new PostgresKnowledgeStore(PostgresDatabase.Require(),
            new FixedEmbedder(Vectors, new EmbeddingModelIdentity("test", "fixed", 3)), new PostgresKnowledgeOptions { Table = table });
        await first.UpsertAsync([Overview]);
        var second = new PostgresKnowledgeStore(PostgresDatabase.Require(),
            new FixedEmbedder(Vectors, new EmbeddingModelIdentity("test", "other", 3)), new PostgresKnowledgeOptions { Table = table });

        var error = await Should.ThrowAsync<InvalidOperationException>(() => second.UpsertAsync([Unrelated]));

        error.Message.ShouldContain("test/fixed (3)");
        error.Message.ShouldContain("test/other (3)");
    }

    [Test]
    public void Constructor_ATableNameThatIsntAPlainIdentifier_IsRefused() =>
        Should.Throw<ArgumentException>(() => new PostgresKnowledgeStore(PostgresDatabase.Require(),
            new FixedEmbedder(Vectors), new PostgresKnowledgeOptions { Table = "chunks; DROP TABLE x" }));
}
