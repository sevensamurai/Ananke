using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Documents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Orchestration.Knowledge.Records;
using Shouldly;

#pragma warning disable ANNKEEXP004

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Superseding, explicit only: <see cref="DatedRecordReader{TRecord}"/> returns records newest first, and labels an
/// older one replaced when a newer one returned with it names it in its <c>replaces</c>, or when its document's header
/// says the document is superseded. Records are written by <see cref="DatedRecordExtractor{TRecord}"/> with a scripted
/// model.
/// </summary>
[TestFixture]
public class DatedRecordReaderTests
{
    public sealed record Decision(string Statement, string? Date, string? Replaces, string Status = "decided");

    private const string Query = "telemetry backend Grafana Cloud SigNoz Better Stack";

    private InMemoryKnowledgeStore _records = null!;
    private DatedRecordReader<Decision> _reader = null!;

    [SetUp]
    public void SetUp()
    {
        _records = new InMemoryKnowledgeStore(new InMemoryEmbedder());
        _reader = new DatedRecordReader<Decision>(_records);
    }

    private async Task StoreAsync(string source, string date, params Decision[] decisions)
    {
        var reply = System.Text.Json.JsonSerializer.Serialize(new { records = decisions });
        var extractor = new DatedRecordExtractor<Decision>(new Scripted(reply), _records, new DatedRecordOptions<Decision>
        {
            Instructions = "Extract the decisions.",
            Text = d => d.Statement,
            Date = d => d.Date,
            Replaces = d => d.Replaces,
            Fields = d => new Dictionary<string, string> { ["status"] = d.Status },
        });
        var header = new Dictionary<string, string> { [DocumentHeaderOptions.EffectiveAtKey] = $"{date}T00:00:00.0000000+00:00" };
        await extractor.ExtractAsync(source, new ExtractedDocument([new ExtractedSection("text")]), header).ConfigureAwait(false);
    }

    private Task<IReadOnlyList<DatedRecord<Decision>>> SearchAsync() =>
        _reader.SearchAsync(Query, new SearchOptions { Mode = SearchMode.Sparse, TopK = 10 });

    [Test]
    public async Task ListAsync_AFilter_ListsEveryMatchingRecordNewestFirst()
    {
        for (var day = 1; day <= 12; day++)
            await StoreAsync($"plan-{day:D2}.md", $"2026-10-{day:D2}", new Decision($"Plan {day} is done", null, null, day % 3 == 0 ? "parked" : "decided"));

        var parked = await _reader.ListAsync(new KnowledgeFilter { ["status"] = "parked" });

        parked.Select(r => r.Source).ShouldBe(["plan-12.md", "plan-09.md", "plan-06.md", "plan-03.md"]);
        (await _reader.ListAsync()).Count.ShouldBe(12);
    }

    [Test]
    public async Task ListAsync_Since_LeavesOutOlderRecords()
    {
        await StoreAsync("old.md", "2026-10-31", new Decision("Telemetry goes to Better Stack", null, null));
        await StoreAsync("new.md", "2026-11-05", new Decision("Telemetry goes to SigNoz", null, null));

        var recent = await _reader.ListAsync(since: new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));

        recent.Select(r => r.Source).ShouldBe(["new.md"]);
    }

    [Test]
    public async Task ListAsync_ANewerRecordThatNamesAnOlderOne_LabelsTheOlderReplaced()
    {
        await StoreAsync("otel.md", "2026-11-01", new Decision("Telemetry goes to Grafana Cloud behind the Collector", null, null));
        await StoreAsync("signoz.md", "2026-11-05", new Decision("Telemetry goes to self-hosted SigNoz", null, "Grafana Cloud"));

        var listed = await _reader.ListAsync();

        listed.Single(r => r.Source == "otel.md").ReplacedBy.ShouldBe("signoz.md:record:0");
    }

    [Test]
    public async Task ListAsync_RecordsInAStoreThatCantList_Throws()
    {
        var reader = new DatedRecordReader<Decision>(new SearchOnly());

        await Should.ThrowAsync<NotSupportedException>(() => reader.ListAsync());
    }

    [Test]
    public async Task SearchAsync_ReturnsTheRecordsNewestFirst()
    {
        await StoreAsync("old.md", "2026-10-31", new Decision("Telemetry goes to Better Stack", null, null));
        await StoreAsync("new.md", "2026-11-05", new Decision("Telemetry goes to SigNoz", null, null));
        await StoreAsync("mid.md", "2026-11-01", new Decision("Telemetry goes to Grafana Cloud", null, null));

        var results = await SearchAsync();

        results.Select(r => r.Source).ShouldBe(["new.md", "mid.md", "old.md"]);
        results[0].EffectiveAt.ShouldBe(new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero));
        results[0].Record.ShouldBe(new Decision("Telemetry goes to SigNoz", null, null));
    }

    [Test]
    public async Task SearchAsync_ANewerRecordThatNamesAnOlderOne_LabelsTheOlderReplaced()
    {
        await StoreAsync("otel.md", "2026-11-01", new Decision("Telemetry goes to Grafana Cloud behind the Collector", null, null));
        await StoreAsync("signoz.md", "2026-11-05", new Decision("Telemetry goes to self-hosted SigNoz", null, "Grafana Cloud"));

        var results = await SearchAsync();

        var grafana = results.Single(r => r.Source == "otel.md");
        grafana.ReplacedBy.ShouldBe("signoz.md:record:0");
        results.Single(r => r.Source == "signoz.md").ReplacedBy.ShouldBeNull();
    }

    [Test]
    public async Task SearchAsync_AReplacesThatDoesntNameTheRecord_LabelsNothing()
    {
        await StoreAsync("otel.md", "2026-11-01", new Decision("Telemetry goes to Grafana Cloud", null, null));
        await StoreAsync("signoz.md", "2026-11-05", new Decision("Telemetry goes to SigNoz", null, "Better Stack"));

        var results = await SearchAsync();

        results.ShouldAllBe(r => r.ReplacedBy == null);
    }

    [Test]
    public async Task SearchAsync_AnOlderRecordNamingANewerOne_DoesntReplaceIt()
    {
        await StoreAsync("signoz.md", "2026-11-05", new Decision("Telemetry goes to SigNoz", null, null));
        await StoreAsync("otel.md", "2026-11-01", new Decision("Telemetry goes to Grafana Cloud", null, "SigNoz"));

        var results = await SearchAsync();

        results.ShouldAllBe(r => r.ReplacedBy == null);
    }

    [Test]
    public async Task SearchAsync_ARecordOfASupersededDocument_IsLabelledReplacedByWhatSupersedesIt()
    {
        var reply = System.Text.Json.JsonSerializer.Serialize(new { records = new[] { new Decision("Telemetry goes to Grafana Cloud", null, null) } });
        var extractor = new DatedRecordExtractor<Decision>(new Scripted(reply), _records, new DatedRecordOptions<Decision>
        {
            Instructions = "Extract the decisions.",
            Text = d => d.Statement,
        });
        var header = new Dictionary<string, string>
        {
            [DocumentHeaderOptions.EffectiveAtKey] = "2026-11-01T00:00:00.0000000+00:00",
            [DocumentHeaderOptions.SupersededByKey] = "2026-11-05-telemetry-on-signoz.md",
        };
        await extractor.ExtractAsync("otel.md", new ExtractedDocument([new ExtractedSection("text")]), header);

        (await SearchAsync()).Single().ReplacedBy.ShouldBe("2026-11-05-telemetry-on-signoz.md");
    }

    [Test]
    public async Task SearchAsync_WithTheCatalog_LabelsTheRecordsOfADocumentANewerOneReplaced()
    {
        await StoreAsync("rfc2616.txt", "1999-06-01", new Decision("HTTP/1.1 keeps connections open by default", null, null));
        var catalog = new InMemoryKnowledgeCatalog(new InMemoryEmbedder());
        await catalog.IndexAsync(new CatalogEntry
        {
            Source = "rfc2616.txt",
            Summary = "",
            Keywords = [],
            IndexedAt = DateTimeOffset.UnixEpoch,
            ChunkCount = 1,
            SupersededBy = "rfc9110.txt",
        });

        var results = await new DatedRecordReader<Decision>(_records, catalog)
            .SearchAsync("HTTP connections open", new SearchOptions { Mode = SearchMode.Sparse, TopK = 10 });

        results.Single().ReplacedBy.ShouldBe("rfc9110.txt");
    }

    [Test]
    public async Task UpsertAsync_ChunksOfASupersededDocument_SetTheCatalogEntrysSupersededBy()
    {
        var catalog = new InMemoryKnowledgeCatalog(new InMemoryEmbedder());
        var store = new CatalogAwareKnowledgeStore(new InMemoryKnowledgeStore(new InMemoryEmbedder()), catalog);

        await store.UpsertAsync(
        [
            new KnowledgeDocument
            {
                Id = "otel.md:chunk:0",
                Text = "Telemetry goes to Grafana Cloud",
                Metadata = new Dictionary<string, string> { ["source"] = "otel.md", [DocumentHeaderOptions.SupersededByKey] = "signoz.md" },
            },
        ]);

        (await catalog.GetAsync("otel.md"))!.SupersededBy.ShouldBe("signoz.md");
        (await catalog.DiscoverAsync("telemetry")).ShouldBeEmpty();
    }

    private sealed class Scripted(string reply) : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = reply });
    }

    /// <summary>A store that searches but can't list.</summary>
    private sealed class SearchOnly : IKnowledgeStore
    {
        public Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(string query, SearchOptions? options = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<KnowledgeChunk>>([]);

        public Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default) => Task.CompletedTask;
    }
}
