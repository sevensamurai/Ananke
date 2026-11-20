using System.Globalization;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Documents;
using Ananke.Orchestration.Knowledge.Records;
using Ananke.Orchestration.Tools;
using Shouldly;

#pragma warning disable ANNKEEXP004

namespace Ananke.Orchestration.Tests;

/// <summary>
/// The listing cases every <see cref="IListableKnowledgeStore"/> is held to, shared by each store's tests: 1,500
/// records whose ids sort oldest first, the opposite of the order <c>list_records</c> gives, so a store that reads the
/// first 1,000 by id and orders them afterwards keeps the oldest. Two more records have no date, and one a date that
/// isn't one.
/// </summary>
public static class DatedRecordListingCases
{
    /// <summary>How many dated records <see cref="FillAsync"/> stores.</summary>
    public const int Count = 1500;

    /// <summary>The record each chunk holds.</summary>
    public sealed record Note(string Text);

    private static readonly DateTimeOffset FirstDay = new(2022, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The date of record <paramref name="i"/>: one day after the one before it.</summary>
    public static DateTimeOffset DateOf(int i) => FirstDay.AddDays(i);

    /// <summary>The text of record <paramref name="i"/>.</summary>
    public static string TextOf(int i) => $"Note {i:D4}";

    /// <summary>Stores the records in <paramref name="store"/>, as a <see cref="DatedRecordExtractor{TRecord}"/> writes them.</summary>
    public static Task FillAsync(IKnowledgeStore store) =>
        store.UpsertAsync(
        [
            .. Enumerable.Range(0, Count).Select(i => Record($"note-{i:D4}", TextOf(i), DateOf(i).ToString("O", CultureInfo.InvariantCulture))),
            Record("note-undated-a", "Undated note a", null),
            Record("note-undated-b", "Undated note b", null),
            Record("note-undated-c", "Undated note c", "soon"),
        ]);

    private static KnowledgeDocument Record(string id, string text, string? date)
    {
        var metadata = new Dictionary<string, string>
        {
            ["source"] = $"{id}.md",
            ["kind"] = "note",
            [DatedRecordExtractor<Note>.RecordKey] = System.Text.Json.JsonSerializer.Serialize(new Note(text)),
        };
        if (date is not null)
            metadata[DocumentHeaderOptions.EffectiveAtKey] = date;
        return new KnowledgeDocument { Id = id, Text = text, Metadata = metadata };
    }

    private static Task<ToolResult> ListRecordsAsync(IKnowledgeStore store, Dictionary<string, object?> args, int maxRecords) =>
        DatedRecordTools.Create(new DatedRecordReader<Note>(store), ["kind"], maxRecords: maxRecords)
            .Tools["list_records"].ExecuteAsync(args);

    /// <summary><c>list_records</c> shows the newest records first, and says that more match than it shows.</summary>
    public static async Task ListRecords_MoreThanTheLimit_ShowsTheNewestAndSaysTheLimitWasReached(IKnowledgeStore store)
    {
        var result = await ListRecordsAsync(store, new() { ["kind"] = "note" }, maxRecords: 10);

        result.IsError.ShouldBeFalse(result.Value);
        var lines = result.Value.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Where(l => l.StartsWith("- ", StringComparison.Ordinal))
            .ShouldBe([.. Enumerable.Range(Count - 10, 10).Reverse().Select(i => $"- {DateOf(i):yyyy-MM-dd} · {TextOf(i)} (note-{i:D4}.md)")]);
        result.Value.ShouldContain("More than 10 records match");
        result.Value.ShouldNotContain("undated");
    }

    /// <summary><c>list_records</c> with <c>since</c> lists every record from that day on, and only those.</summary>
    public static async Task ListRecords_Since_ListsOnlyTheLaterOnes(IKnowledgeStore store)
    {
        var result = await ListRecordsAsync(store, new() { ["since"] = $"{DateOf(Count - 25):yyyy-MM-dd}" }, maxRecords: 200);

        result.IsError.ShouldBeFalse(result.Value);
        result.Value.ShouldStartWith("25 record(s), newest first:");
        result.Value.ShouldContain($"- {DateOf(Count - 1):yyyy-MM-dd} · {TextOf(Count - 1)}");
        result.Value.ShouldContain($"- {DateOf(Count - 25):yyyy-MM-dd} · {TextOf(Count - 25)}");
        result.Value.ShouldNotContain(TextOf(Count - 26));
        result.Value.ShouldNotContain("More than");
    }

    /// <summary>
    /// Listing by date: newest first and cut at the limit in the store, the chunks without a date after the dated ones,
    /// and a lower bound that leaves out the earlier and the undated ones.
    /// </summary>
    public static async Task ListAsync_ByDate_OrdersAndBoundsInTheStore(IListableKnowledgeStore store)
    {
        var order = new KnowledgeDateOrder(DocumentHeaderOptions.EffectiveAtKey);
        var filter = new KnowledgeFilter { ["kind"] = "note" };

        var newest = await store.ListAsync(filter, limit: 3, order: order);
        newest.Select(c => c.Metadata["source"]).ShouldBe(["note-1499.md", "note-1498.md", "note-1497.md"]);

        var all = await store.ListAsync(filter, limit: Count + 10, order: order);
        all.Count.ShouldBe(Count + 3);
        all.Take(Count).Select(c => c.Metadata["source"]).ShouldBe([.. Enumerable.Range(0, Count).Reverse().Select(i => $"note-{i:D4}.md")]);
        all.Skip(Count).Select(c => c.Metadata["source"]).ShouldBe(["note-undated-a.md", "note-undated-b.md", "note-undated-c.md"], ignoreOrder: true);

        var since = await store.ListAsync(filter, limit: Count, order: order with { Since = DateOf(Count - 3) });
        since.Select(c => c.Metadata["source"]).ShouldBe(["note-1499.md", "note-1498.md", "note-1497.md"]);
    }
}
