using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Documents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Orchestration.Knowledge.Records;
using Ananke.Orchestration.Tools;
using Shouldly;

#pragma warning disable ANNKEEXP004

namespace Ananke.Orchestration.Tests;

/// <summary><see cref="DatedRecordTools"/>: <c>list_records</c> lists every matching record, newest first.</summary>
[TestFixture]
public class DatedRecordToolsTests
{
    public sealed record Decision(string Statement, string Status, string? Replaces = null);

    private InMemoryKnowledgeStore _records = null!;

    [SetUp]
    public void SetUp() => _records = new InMemoryKnowledgeStore(new InMemoryEmbedder());

    private async Task StoreAsync(string source, string date, params Decision[] decisions)
    {
        var reply = System.Text.Json.JsonSerializer.Serialize(new { records = decisions });
        var extractor = new DatedRecordExtractor<Decision>(new Scripted(reply), _records, new DatedRecordOptions<Decision>
        {
            Instructions = "Extract the decisions.",
            Text = d => d.Statement,
            Replaces = d => d.Replaces,
            Fields = d => new Dictionary<string, string> { ["status"] = d.Status },
        });
        var header = new Dictionary<string, string> { [DocumentHeaderOptions.EffectiveAtKey] = $"{date}T00:00:00.0000000+00:00" };
        await extractor.ExtractAsync(source, new ExtractedDocument([new ExtractedSection("text")]), header).ConfigureAwait(false);
    }

    private Task<ToolResult> ListAsync(Dictionary<string, object?> args, int maxRecords = 200) =>
        DatedRecordTools.Create(new DatedRecordReader<Decision>(_records), ["status"], maxRecords: maxRecords)
            .Tools["list_records"].ExecuteAsync(args);

    [Test]
    public async Task ListRecords_AStatus_ListsEveryRecordWithItNewestFirst()
    {
        await StoreAsync("a.md", "2026-10-01", new Decision("Use Postgres", "decided"));
        await StoreAsync("b.md", "2026-11-01", new Decision("Use SigNoz", "decided"), new Decision("Try Memgraph", "parked"));

        var result = await ListAsync(new() { ["status"] = "decided" });

        result.IsError.ShouldBeFalse();
        result.Value.ShouldContain("2 record(s)");
        result.Value.IndexOf("2026-11-01 · Use SigNoz (b.md)", StringComparison.Ordinal)
            .ShouldBeLessThan(result.Value.IndexOf("2026-10-01 · Use Postgres (a.md)", StringComparison.Ordinal));
        result.Value.ShouldNotContain("Memgraph");
    }

    [Test]
    public async Task ListRecords_Since_LeavesOutOlderOnes()
    {
        await StoreAsync("a.md", "2026-10-01", new Decision("Use Postgres", "decided"));
        await StoreAsync("b.md", "2026-11-01", new Decision("Use SigNoz", "decided"));

        var result = await ListAsync(new() { ["since"] = "2026-10-15" });

        result.Value.ShouldContain("Use SigNoz");
        result.Value.ShouldNotContain("Use Postgres");
    }

    [Test]
    public async Task ListRecords_AReplacedRecord_SaysWhatReplacedIt()
    {
        await StoreAsync("otel.md", "2026-11-01", new Decision("Telemetry goes to Grafana Cloud", "decided"));
        await StoreAsync("signoz.md", "2026-11-05", new Decision("Telemetry goes to SigNoz", "decided", "Grafana Cloud"));

        var result = await ListAsync([]);

        result.Value.ShouldContain("Telemetry goes to Grafana Cloud (otel.md) — replaced by signoz.md:record:0");
    }

    [Test]
    public async Task ListRecords_MoreThanItShows_CountsTheRest()
    {
        for (var day = 1; day <= 5; day++)
            await StoreAsync($"d{day}.md", $"2026-10-0{day}", new Decision($"Decision {day}", "decided"));

        var result = await ListAsync([], maxRecords: 2);

        result.Value.ShouldContain("Decision 5");
        result.Value.ShouldNotContain("Decision 1");
        result.Value.ShouldContain("3 more");
    }

    [Test]
    public async Task ListRecords_ASinceThatIsntADate_IsAnError()
    {
        var result = await ListAsync(new() { ["since"] = "last week" });

        result.IsError.ShouldBeTrue();
    }

    /// <summary>Answers every call with the reply it was given.</summary>
    private sealed class Scripted(string reply) : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = reply });
    }
}
