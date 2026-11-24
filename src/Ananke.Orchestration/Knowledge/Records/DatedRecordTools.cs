using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Ananke.Orchestration.Tools;

namespace Ananke.Orchestration.Knowledge.Records;

/// <summary>
/// A <see cref="ToolKit"/> over the records a <see cref="DatedRecordExtractor{TRecord}"/> or a
/// <see cref="DatedRecordWriter{TRecord}"/> stored: <c>list_records</c> lists every record matching the properties
/// given, newest first, so a roundup is answered as a list rather than by the top few of a search. A written record
/// whose passage is gone (<see cref="DatedRecord{TRecord}.PassageGone"/>) is listed with that said.
/// </summary>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public static class DatedRecordTools
{
    /// <summary>Creates the toolkit.</summary>
    /// <param name="reader">The records to list; their store must list (<see cref="IListableKnowledgeStore"/>).</param>
    /// <param name="filterFields">
    /// The record properties a caller may filter by, as <see cref="DatedRecordOptions{TRecord}.Fields"/> keeps them
    /// (<c>status</c>, <c>subject</c>); each becomes an optional parameter.
    /// </param>
    /// <param name="name">Name for the returned <see cref="ToolKit"/>. Default is <c>"dated_records"</c>.</param>
    /// <param name="describe">How a record is written in the list. Default is its text.</param>
    /// <param name="maxRecords">
    /// How many records a list shows at most, the newest; when more match, the list says so. Default is 200.
    /// </param>
    public static ToolKit Create<TRecord>(
        DatedRecordReader<TRecord> reader,
        IReadOnlyList<string> filterFields,
        string name = "dated_records",
        Func<DatedRecord<TRecord>, string>? describe = null,
        int maxRecords = 200)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(filterFields);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRecords);
        describe ??= r => r.Text;

        return new ToolKit(name).AddTool(
            "list_records",
            "List every record matching the values given, newest first, with its date, its document and what replaced " +
            "it. Leave a parameter out to match any value. Use this for a roundup rather than a search, which returns " +
            "only the closest few.",
            tool =>
            {
                foreach (var field in filterFields)
                    tool.Param(field, $"Only records whose {field} is this value", required: false);
                tool.Param("since", "Only records dated on or after this day, yyyy-MM-dd", required: false)
                    .OnExecute(async (args, ct) =>
                    {
                        DateTimeOffset? since = null;
                        if (args.GetOrDefault("since") is { Length: > 0 } sinceText)
                        {
                            if (!DateTimeOffset.TryParseExact(sinceText, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                    DateTimeStyles.AssumeUniversal, out var parsed))
                                return ToolResult.Error($"'since' must be a day as yyyy-MM-dd, not '{sinceText}'.");
                            since = parsed;
                        }

                        var filter = new KnowledgeFilter();
                        foreach (var field in filterFields)
                        {
                            if (args.GetOrDefault(field) is { Length: > 0 } value)
                                filter[field] = value;
                        }

                        // One more than is shown, to tell whether more match.
                        var records = await reader.ListAsync(
                            filter, since, maxRecords == int.MaxValue ? maxRecords : maxRecords + 1, ct).ConfigureAwait(false);
                        return Format(records, describe, maxRecords);
                    });
            });
    }

    private static string Format<TRecord>(
        IReadOnlyList<DatedRecord<TRecord>> records, Func<DatedRecord<TRecord>, string> describe, int maxRecords)
    {
        if (records.Count == 0)
            return "No record matches.";

        var text = new StringBuilder(records.Count > maxRecords
            ? $"More than {maxRecords} records match; the newest {maxRecords}:\n"
            : $"{records.Count} record(s), newest first:\n");
        foreach (var record in records.Take(maxRecords))
        {
            var date = record.EffectiveAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "undated";
            text.Append($"- {date} · {describe(record)} ({record.Source})");
            if (record.ReplacedBy is not null)
                text.Append($" — replaced by {record.ReplacedBy}");
            if (record.PassageGone)
                text.Append(" — its passage is gone");
            text.Append('\n');
        }

        if (records.Count > maxRecords)
            text.Append("Narrow the list with a value or 'since' to see the rest.\n");
        return text.ToString();
    }
}
