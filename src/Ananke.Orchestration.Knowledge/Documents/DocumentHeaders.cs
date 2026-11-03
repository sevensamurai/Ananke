using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Ananke.Orchestration.Knowledge.Documents;

/// <summary>
/// How <see cref="DocumentProcessor"/> reads a document's header. Every field <see cref="Read"/> gives (and every tag
/// given to <c>ProcessAsync</c> for a field the document doesn't give) is read for the date and superseding; only the
/// fields named in <see cref="Fields"/> are kept: put at the top of every chunk and into its metadata, so a passage says
/// where it is from and how current it is.
/// </summary>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed record DocumentHeaderOptions
{
    /// <summary>The metadata key a document's date is written under, as an ISO 8601 date and time.</summary>
    public const string EffectiveAtKey = "effective_at";

    /// <summary>The metadata key for what supersedes a document, when <see cref="SupersededBy"/> says something does.</summary>
    public const string SupersededByKey = "superseded_by";

    /// <summary>The metadata key for the sources a document replaces, as a JSON array, when <see cref="Replaces"/> names any.</summary>
    public const string ReplacesSourcesKey = "replaces_sources";

    /// <summary>
    /// The metadata key naming, as a JSON array, the header fields kept on a chunk (<see cref="Fields"/>), so a catalog
    /// can tell them from other metadata.
    /// </summary>
    public const string HeaderFieldsKey = "header_fields";

    /// <summary>The link kind a document's <see cref="Updates"/> are written as.</summary>
    public const string UpdatesKind = "updates";

    /// <summary>Reads the header's fields from an extracted document. <see cref="DocumentHeaders.FromMarkdown"/> reads the usual Markdown shape.</summary>
    public required Func<ExtractedDocument, IReadOnlyDictionary<string, string>> Read { get; init; }

    /// <summary>
    /// The fields kept, in this order: put at the top of each chunk's text and into its metadata, and handed to the
    /// records step. None by default — a header is often where people are named (authors, reviewers, attendees).
    /// </summary>
    public IReadOnlyList<string> Fields { get; init; } = [];

    /// <summary>
    /// The field that holds the document's date: the first date in it (<see cref="ParseDate"/>) becomes the chunks'
    /// <see cref="EffectiveAtKey"/>, and the catalog's <see cref="Catalog.CatalogEntry.EffectiveAt"/>.
    /// </summary>
    public string? DateField { get; init; }

    /// <summary>How the date field is read. Default is <see cref="DocumentHeaders.DateIn"/>.</summary>
    public Func<string, DateTimeOffset?>? ParseDate { get; init; }

    /// <summary>
    /// What supersedes the document, by its own header, or nothing: for example what follows <c>Superseded by</c> in its
    /// status. When it names something, the chunks carry it as <see cref="SupersededByKey"/> (several joined with
    /// <c>", "</c>), a catalog entry built from them is <see cref="Catalog.CatalogEntry.SupersededBy"/>, and the
    /// document's dated records are read as replaced.
    /// </summary>
    public Func<IReadOnlyDictionary<string, string>, IReadOnlyList<string>?>? SupersededBy { get; init; }

    /// <summary>
    /// The sources this document replaces, by its header, as source ids: the product maps its references (<c>2616</c>,
    /// <c>keps/sig-x/1234-…</c>) to them. A <c>CatalogAwareKnowledgeStore</c> marks each one superseded by this document,
    /// whether it is indexed before or after. For documents that never change once published, this is the only way
    /// superseding is said.
    /// </summary>
    public Func<IReadOnlyDictionary<string, string>, IReadOnlyList<string>?>? Replaces { get; init; }

    /// <summary>
    /// The sources this document updates without replacing, as source ids. They are written as links of kind
    /// <see cref="UpdatesKind"/> and never mark anything superseded.
    /// </summary>
    public Func<IReadOnlyDictionary<string, string>, IReadOnlyList<string>?>? Updates { get; init; }
}

/// <summary>
/// Header readers for <see cref="DocumentHeaderOptions.Read"/>, one for each common shape, and <see cref="Combine"/> to
/// try several. Each returns fields whose names are compared without case. Their patterns have a time limit: text that
/// runs past it gives no fields rather than hold up ingestion.
/// </summary>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public static partial class DocumentHeaders
{
    private const int TimeoutMilliseconds = 250;
    private const int MaxFieldBlockLines = 60;

    /// <summary>
    /// Reads a Markdown document's title (its first heading, as <c>Title</c>) and the bold-labelled fields of its first
    /// section, or of the second when the first has none (a title alone, then a section of links):
    /// <c>**Status:** Accepted · **Date:** 2026-11-01</c> and <c>**Recording**: …</c> alike. A paragraph's lines are read
    /// as one, so a value may wrap. The first time a field appears is the one kept.
    /// </summary>
    public static IReadOnlyDictionary<string, string> FromMarkdown(ExtractedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var fields = Fields();
        if (document.Sections.Count == 0)
            return fields;

        var first = document.Sections[0];
        if (Title(first) is { } title)
            fields["Title"] = title;

        try
        {
            foreach (var section in document.Sections.Take(2))
            {
                foreach (var paragraph in Paragraph().Split(section.Text))
                {
                    var line = string.Join(' ', paragraph.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                    foreach (Match field in Field().Matches(line))
                    {
                        // A list's items are read as one line, so a value can end with the next item's bullet.
                        var value = field.Groups["value"].Value.Trim().TrimEnd('·', '*', '-', '+').Trim();
                        if (value.Length > 0)
                            fields.TryAdd(field.Groups["key"].Value.Trim(), value);
                    }
                }

                if (fields.Keys.Any(k => k != "Title"))
                    break;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // The title, if any, stands; fields past a pattern's time limit are left out.
        }

        return fields;
    }

    /// <summary>
    /// Reads a YAML front matter's fields, as <c>MarkdownExtractor</c> puts them in <see cref="ExtractedDocument.Metadata"/>
    /// (a list's items joined with <c>", "</c>); the document's title too, from its heading or its front matter.
    /// </summary>
    public static IReadOnlyDictionary<string, string> FromFrontMatter(ExtractedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var fields = Fields();
        foreach (var (key, value) in document.Metadata ?? new Dictionary<string, string>())
            fields.TryAdd(key, value);
        return fields;
    }

    /// <summary>
    /// Reads an RFC-822 block of <c>Key: value</c> lines at the very top of the first section, as PEPs and mail-style
    /// headers write it: an indented line continues the field before it, and the block ends at the first blank line or
    /// the first line that isn't a field.
    /// </summary>
    public static IReadOnlyDictionary<string, string> FromFieldBlock(ExtractedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var fields = Fields();
        if (document.Sections.Count == 0)
            return fields;

        string? last = null;
        try
        {
            foreach (var line in document.Sections[0].Text.Replace("\r", "").Split('\n').Take(MaxFieldBlockLines))
            {
                if (line.Trim().Length == 0)
                    break;
                if (char.IsWhiteSpace(line[0]) && last is not null)
                {
                    fields[last] = $"{fields[last]} {line.Trim()}".Trim();
                    continue;
                }
                if (BlockField().Match(line) is not { Success: true } field)
                    break;
                last = field.Groups["key"].Value;
                fields[last] = field.Groups["value"].Value.Trim();
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // What was read before the time limit stands.
        }

        return fields;
    }

    /// <summary>
    /// Reads a decision record's shape: the first heading as <c>Title</c>, the first line of a section headed
    /// <c>Status</c> as <c>Status</c>, and a plain <c>Date: …</c> line in the first two sections as <c>Date</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, string> FromStatusSection(ExtractedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var fields = Fields();
        if (document.Sections.Count == 0)
            return fields;

        if (Title(document.Sections[0]) is { } title)
            fields["Title"] = title;

        if (document.Sections.FirstOrDefault(s => string.Equals(s.SectionTitle?.Trim(), "Status", StringComparison.OrdinalIgnoreCase)) is { } status
            && status.Text.Split('\n').Skip(1).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) is { } statusLine)
        {
            fields["Status"] = statusLine;
        }

        try
        {
            foreach (var section in document.Sections.Take(2))
            {
                if (DateLine().Match(section.Text) is { Success: true } date)
                {
                    fields.TryAdd("Date", date.Groups["value"].Value.Trim());
                    break;
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // No date line read.
        }

        return fields;
    }

    /// <summary>A reader that tries each of <paramref name="readers"/> in turn, taking each field from the first that has it.</summary>
    public static Func<ExtractedDocument, IReadOnlyDictionary<string, string>> Combine(
        params Func<ExtractedDocument, IReadOnlyDictionary<string, string>>[] readers)
    {
        ArgumentNullException.ThrowIfNull(readers);
        return document =>
        {
            var fields = Fields();
            foreach (var reader in readers)
            {
                foreach (var (key, value) in reader(document))
                    fields.TryAdd(key, value);
            }
            return fields;
        };
    }

    /// <summary>
    /// The first date in <paramref name="value"/>, as midnight UTC, or <see langword="null"/>: <c>2026-11-02</c>,
    /// <c>2019-07-1</c>, <c>05-Jul-2001</c>, <c>5 July 2001</c>, <c>July 5, 2001</c>, <c>Sept. 3, 2020</c>, or
    /// <c>June 2022</c> as its first day. Month names are English.
    /// </summary>
    public static DateTimeOffset? DateIn(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        try
        {
            if (IsoDate().Match(value) is { Success: true } iso)
                return Date(Number(iso, "year"), Number(iso, "month"), Number(iso, "day"));

            Match? best = null;
            foreach (var pattern in (Regex[])[DayMonthYear(), MonthDayYear(), MonthYear()])
            {
                if (pattern.Match(value) is { Success: true } m && Month(m.Groups["month"].Value) is not null
                    && (best is null || m.Index < best.Index))
                {
                    best = m;
                }
            }

            return best is null
                ? null
                : Date(Number(best, "year"), Month(best.Groups["month"].Value)!.Value, best.Groups["day"].Success ? Number(best, "day") : 1);
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> Fields() => new(StringComparer.OrdinalIgnoreCase);

    private static string? Title(ExtractedSection first)
    {
        try
        {
            var title = first.SectionTitle ?? (Heading().Match(first.Text) is { Success: true } h ? h.Groups[1].Value.Trim() : null);
            return string.IsNullOrWhiteSpace(title) ? null : title;
        }
        catch (RegexMatchTimeoutException)
        {
            return first.SectionTitle;
        }
    }

    private static int Number(Match match, string group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    private static DateTimeOffset? Date(int year, int month, int day) =>
        year is >= 1 and <= 9999 && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month)
            ? new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero)
            : null;

    private static readonly string[] MonthNames =
        ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    private static int? Month(string name) =>
        name.Length >= 3 && Array.IndexOf(MonthNames, name[..3].ToLowerInvariant()) is var i and >= 0 ? i + 1 : null;

    [GeneratedRegex(@"^\s*#\s+(.+)$", RegexOptions.Multiline, TimeoutMilliseconds)]
    private static partial Regex Heading();

    [GeneratedRegex(@"\n\s*\n", RegexOptions.None, TimeoutMilliseconds)]
    private static partial Regex Paragraph();

    [GeneratedRegex(@"\*\*(?<key>[^*\n:]{1,40})(?::\*\*|\*\*:)\s*(?<value>.*?)(?=\s*\*\*[^*\n:]{1,40}(?::\*\*|\*\*:)|$)", RegexOptions.None, TimeoutMilliseconds)]
    private static partial Regex Field();

    [GeneratedRegex(@"^(?<key>[A-Za-z][A-Za-z0-9-]{0,40}):[ \t]*(?<value>.*)$", RegexOptions.None, TimeoutMilliseconds)]
    private static partial Regex BlockField();

    [GeneratedRegex(@"^Date:\s*(?<value>.+)$", RegexOptions.Multiline, TimeoutMilliseconds)]
    private static partial Regex DateLine();

    [GeneratedRegex(@"\b(?<year>\d{4})-(?<month>\d{1,2})-(?<day>\d{1,2})\b", RegexOptions.None, TimeoutMilliseconds)]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"\b(?<day>\d{1,2})[- ](?<month>[A-Za-z]{3,9})\.?[- ](?<year>\d{4})\b", RegexOptions.None, TimeoutMilliseconds)]
    private static partial Regex DayMonthYear();

    [GeneratedRegex(@"\b(?<month>[A-Za-z]{3,9})\.?\s+(?<day>\d{1,2}),?\s+(?<year>\d{4})\b", RegexOptions.None, TimeoutMilliseconds)]
    private static partial Regex MonthDayYear();

    [GeneratedRegex(@"\b(?<month>[A-Za-z]{3,9})\.?\s+(?<year>\d{4})\b", RegexOptions.None, TimeoutMilliseconds)]
    private static partial Regex MonthYear();
}
