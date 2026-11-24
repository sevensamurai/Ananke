using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Documents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Orchestration.Knowledge.Records;
using Ananke.Orchestration.Tests;
using Shouldly;

namespace Ananke.Qdrant.Tests.Containers;

/// <summary><see cref="DatedRecordListingCases"/> on <see cref="QdrantKnowledgeStore"/>.</summary>
[TestFixture]
public class QdrantDatedRecordListingTests
{
    private QdrantKnowledgeStore? _store;

    [OneTimeSetUp]
    public async Task FillAsync()
    {
        if (QdrantServer.Client is not { } client)
            return;
        _store = new QdrantKnowledgeStore(client, new InMemoryEmbedder(), QdrantServer.NewCollection("records"), vectorSize: 64);
        await DatedRecordListingCases.FillAsync(_store);
    }

    private QdrantKnowledgeStore Store()
    {
        QdrantServer.Require();
        return _store!;
    }

    [Test]
    public Task ListRecords_MoreThanTheLimit_ShowsTheNewestAndSaysTheLimitWasReached() =>
        DatedRecordListingCases.ListRecords_MoreThanTheLimit_ShowsTheNewestAndSaysTheLimitWasReached(Store());

    [Test]
    public Task ListRecords_Since_ListsOnlyTheLaterOnes() =>
        DatedRecordListingCases.ListRecords_Since_ListsOnlyTheLaterOnes(Store());

    [Test]
    public Task ListAsync_ByDate_OrdersAndBoundsInTheStore() =>
        DatedRecordListingCases.ListAsync_ByDate_OrdersAndBoundsInTheStore(Store());

    [Test]
    public Task ListAsync_DateWithoutAnOffset_IsReadAsUtc() =>
        DatedRecordListingCases.ListAsync_DateWithoutAnOffset_IsReadAsUtc(new QdrantKnowledgeStore(
            QdrantServer.Require(), new InMemoryEmbedder(), QdrantServer.NewCollection("no-offset"), vectorSize: 64));

    [Test]
    public Task ListAsync_ValuesThatArentIsoDates_AreNoDate() =>
        DatedRecordListingCases.ListAsync_ValuesThatArentIsoDates_AreNoDate(new QdrantKnowledgeStore(
            QdrantServer.Require(), new InMemoryEmbedder(), QdrantServer.NewCollection("not-dates"), vectorSize: 64));

    [Test]
    public async Task ListRecords_AReplacedRecord_ShowsTheDocumentIds()
    {
        var store = new QdrantKnowledgeStore(
            QdrantServer.Require(), new InMemoryEmbedder(), QdrantServer.NewCollection("replaced"), vectorSize: 64);
        await store.UpsertAsync(
        [
            Record("otel.md", "2026-11-01", "Telemetry goes to Grafana Cloud", replaces: null),
            Record("signoz.md", "2026-11-05", "Telemetry goes to SigNoz", replaces: "Grafana Cloud"),
        ]);

        var records = await new DatedRecordReader<DatedRecordListingCases.Note>(store).ListAsync();
        records.Select(r => r.Id).ShouldBe(["signoz.md:record:0", "otel.md:record:0"]);
        records[1].ReplacedBy.ShouldBe("signoz.md:record:0");

        var result = await DatedRecordTools.Create(new DatedRecordReader<DatedRecordListingCases.Note>(store), ["kind"])
            .Tools["list_records"].ExecuteAsync(new Dictionary<string, object?>());
        result.Value.ShouldContain("Telemetry goes to Grafana Cloud (otel.md) — replaced by signoz.md:record:0");
    }

    /// <summary>Record 0 of <paramref name="source"/>, as a <see cref="DatedRecordExtractor{TRecord}"/> writes it.</summary>
    private static KnowledgeDocument Record(string source, string date, string text, string? replaces)
    {
        var metadata = new Dictionary<string, string>
        {
            ["source"] = source,
            ["kind"] = "note",
            [DatedRecordExtractor<DatedRecordListingCases.Note>.RecordKey] =
                System.Text.Json.JsonSerializer.Serialize(new DatedRecordListingCases.Note(text)),
            [DocumentHeaderOptions.EffectiveAtKey] = $"{date}T00:00:00.0000000+00:00",
        };
        if (replaces is not null)
            metadata[DatedRecordExtractor<DatedRecordListingCases.Note>.ReplacesKey] = replaces;
        return new KnowledgeDocument { Id = $"{source}:record:0", Text = text, Metadata = metadata };
    }

    [Test]
    public async Task ListAsync_ByDateWithMorePointsOnOneDateThanAPage_ListsEachOnce()
    {
        var store = new QdrantKnowledgeStore(
            QdrantServer.Require(), new InMemoryEmbedder(), QdrantServer.NewCollection("ties"), vectorSize: 64);
        await store.UpsertAsync([.. Enumerable.Range(0, 600).Select(i => new KnowledgeDocument
        {
            Id = $"n{i}",
            Text = $"Note {i}",
            Metadata = new Dictionary<string, string> { ["effective_at"] = i < 300 ? "2026-11-01T00:00:00+00:00" : "2026-10-01T00:00:00+00:00" },
        })]);

        var listed = await store.ListAsync(new KnowledgeFilter(), limit: 1000, order: new KnowledgeDateOrder("effective_at"));

        listed.Select(c => c.Id).Distinct().Count().ShouldBe(600);
        listed.Take(300).ShouldAllBe(c => c.Metadata["effective_at"].StartsWith("2026-11-01"));
    }
}
