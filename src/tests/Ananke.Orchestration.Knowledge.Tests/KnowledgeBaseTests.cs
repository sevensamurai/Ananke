using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

namespace Ananke.Orchestration.Knowledge.Tests;

[TestFixture]
public class KnowledgeBaseTests
{
    private static InMemoryKnowledgeStore Store() => new(new InMemoryEmbedder());
    private static InMemoryKnowledgeCatalog Catalog() => new(new InMemoryEmbedder());

    [Test]
    public void Constructor_NullSections_Throws() =>
        Should.Throw<ArgumentNullException>(() => new KnowledgeBase(null!, Catalog()));

    [Test]
    public void Constructor_NullCatalog_Throws() =>
        Should.Throw<ArgumentNullException>(() => new KnowledgeBase([], null!));

    [Test]
    public void Constructor_DuplicateSectionNames_Throws() =>
        Should.Throw<ArgumentException>(() => new KnowledgeBase(
            [new KnowledgeSection("pets", Store()), new KnowledgeSection("pets", Store())], Catalog()));

    [Test]
    public void Constructor_DuplicateSectionNames_DifferByCaseOnly_StillThrows()
    {
        // Section lookup is case-insensitive, so "Pets" and "pets" collide.
        Should.Throw<ArgumentException>(() => new KnowledgeBase(
            [new KnowledgeSection("pets", Store()), new KnowledgeSection("Pets", Store())], Catalog()));
    }

    [Test]
    public void Count_ReflectsNumberOfSections()
    {
        var kb = new KnowledgeBase(
            [new KnowledgeSection("pets", Store()), new KnowledgeSection("policies", Store())], Catalog());

        kb.Count.ShouldBe(2);
    }

    [Test]
    public void Indexer_ExistingSection_ReturnsIt()
    {
        var petStore = Store();
        var kb = new KnowledgeBase([new KnowledgeSection("pets", petStore)], Catalog());

        kb["pets"].Store.ShouldBeSameAs(petStore);
    }

    [Test]
    public void Indexer_UnknownSection_ThrowsNamingAvailableSections()
    {
        var kb = new KnowledgeBase(
            [new KnowledgeSection("pets", Store()), new KnowledgeSection("policies", Store())], Catalog());

        var ex = Should.Throw<KeyNotFoundException>(() => kb["unknown"]);

        ex.Message.ShouldContain("pets");
        ex.Message.ShouldContain("policies");
    }

    [Test]
    public void Indexer_IsCaseInsensitive()
    {
        var kb = new KnowledgeBase([new KnowledgeSection("Pets", Store())], Catalog());

        Should.NotThrow(() => kb["pets"]);
        Should.NotThrow(() => kb["PETS"]);
    }

    [Test]
    public void TryGetSection_ExistingSection_ReturnsTrueAndSection()
    {
        var kb = new KnowledgeBase([new KnowledgeSection("pets", Store())], Catalog());

        var found = kb.TryGetSection("pets", out var section);

        found.ShouldBeTrue();
        section.ShouldNotBeNull();
        section.Name.ShouldBe("pets");
    }

    [Test]
    public void TryGetSection_MissingSection_ReturnsFalseAndNull()
    {
        var kb = new KnowledgeBase([], Catalog());

        var found = kb.TryGetSection("nope", out var section);

        found.ShouldBeFalse();
        section.ShouldBeNull();
    }

    [Test]
    public void GetEnumerator_YieldsAllSections()
    {
        var kb = new KnowledgeBase(
            [new KnowledgeSection("pets", Store()), new KnowledgeSection("policies", Store())], Catalog());

        var names = kb.Select(s => s.Name).OrderBy(n => n).ToList();

        names.ShouldBe(["pets", "policies"]);
    }

    [Test]
    public async Task SearchAsync_MergesResultsAcrossSectionsOrderedByScore()
    {
        var petStore = Store();
        var policyStore = Store();
        // Both texts share the query's words (so both clear the default score threshold), but the
        // pet doc matches it exactly while the policy doc only partially overlaps — same pattern
        // used in InMemoryKnowledgeStoreTests.SearchAsync_ResultsAreOrderedByDescendingScore.
        await petStore.UpsertAsync([new KnowledgeDocument { Id = "pet-1", Text = "golden retriever family dog" }]);
        await policyStore.UpsertAsync([new KnowledgeDocument { Id = "policy-1", Text = "golden retriever leash accessory" }]);
        var kb = new KnowledgeBase(
            [new KnowledgeSection("pets", petStore), new KnowledgeSection("policies", policyStore)], Catalog());

        var results = await kb.SearchAsync("golden retriever family dog");

        results.Count.ShouldBe(2);
        // Both sections contributed a result, each correctly tagged.
        results.ShouldContain(r => r.Section == "pets" && r.Chunk.Id == "pet-1");
        results.ShouldContain(r => r.Section == "policies" && r.Chunk.Id == "policy-1");
        // Ordered by descending score — the exact-match pet result must rank above the partial one.
        results[0].Section.ShouldBe("pets");
    }

    [Test]
    public async Task SearchAsync_NoSections_ReturnsEmpty()
    {
        var kb = new KnowledgeBase([], Catalog());

        var results = await kb.SearchAsync("anything");

        results.ShouldBeEmpty();
    }

    [Test]
    public void Catalog_ReturnsTheSharedCatalogInstance()
    {
        var catalog = Catalog();
        var kb = new KnowledgeBase([], catalog);

        kb.Catalog.ShouldBeSameAs(catalog);
    }
}
