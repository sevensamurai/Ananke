using Ananke.Orchestration.Knowledge.Catalog;
using Shouldly;

namespace Ananke.Qdrant.Tests;

/// <summary>
/// <see cref="QdrantKnowledgeCatalog"/>'s payload mapping, as a round trip: <see cref="QdrantKnowledgeCatalog.BuildPayload"/>
/// then <see cref="QdrantKnowledgeCatalog.MapPayload"/>. No Qdrant connection is needed.
/// </summary>
[TestFixture]
public class QdrantKnowledgeCatalogPayloadTests
{
    private static CatalogEntry Entry(DateTimeOffset? effectiveAt) => new()
    {
        Source = "0005.md",
        Summary = "The knowledge base",
        Keywords = ["ananke", "telos"],
        Category = "design",
        IndexedAt = new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero),
        EffectiveAt = effectiveAt,
        ChunkCount = 4,
        SupersededBy = "0006.md",
        Replaces = ["0003.md", "0004.md"],
        Fields = new Dictionary<string, string> { ["Status"] = "Accepted", ["Type"] = "Plan" },
    };

    [Test]
    public void RoundTrip_Fields_AreKept()
    {
        var back = QdrantKnowledgeCatalog.MapPayload(QdrantKnowledgeCatalog.BuildPayload(Entry(null), "text"));

        back.Fields.ShouldBe(new Dictionary<string, string> { ["Status"] = "Accepted", ["Type"] = "Plan" }, ignoreOrder: true);
    }

    [Test]
    public void BuildPayload_Fields_AreAlsoIndexedAsLowerCaseKeyValuePairs()
    {
        var payload = QdrantKnowledgeCatalog.BuildPayload(Entry(null), "text");

        payload[QdrantKnowledgeCatalog.FieldValuesPayloadKey].ListValue.Values.Select(v => v.StringValue)
            .ShouldBe(["status=accepted", "type=plan"], ignoreOrder: true);
    }

    [Test]
    public void RoundTrip_EffectiveAt_IsKept()
    {
        var entry = Entry(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero));

        var back = QdrantKnowledgeCatalog.MapPayload(QdrantKnowledgeCatalog.BuildPayload(entry, "The knowledge base\nKeywords: ananke, telos"));

        back.EffectiveAt.ShouldBe(entry.EffectiveAt);
        back.IndexedAt.ShouldBe(entry.IndexedAt);
        back.SupersededBy.ShouldBe("0006.md");
        back.Keywords.ShouldBe(["ananke", "telos"]);
        back.Replaces.ShouldBe(["0003.md", "0004.md"]);
    }

    [Test]
    public void RoundTrip_NoEffectiveAt_StaysNull() =>
        QdrantKnowledgeCatalog.MapPayload(QdrantKnowledgeCatalog.BuildPayload(Entry(null), "text")).EffectiveAt.ShouldBeNull();
}
