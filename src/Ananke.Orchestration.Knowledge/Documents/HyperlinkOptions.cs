using System.Diagnostics.CodeAnalysis;

namespace Ananke.Orchestration.Knowledge.Documents;

/// <summary>
/// How <see cref="DocumentProcessor.Hyperlinks"/> stores the hyperlinks of a document's sections as links to other
/// documents: each hyperlink <see cref="Resolve"/> maps to a source id is written into the metadata of the chunks of the
/// section that holds it, as <c>entity:document:{source id}</c> = <c>document:{source id}</c>, the shape of every link
/// (<see cref="DocumentLinks"/>). They go with the document's chunks when it is indexed again.
/// <see cref="LinkedDocumentReader"/> reads them back.
/// </summary>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed record HyperlinkOptions
{
    /// <summary>The kind of link a resolved hyperlink is stored as.</summary>
    public const string DocumentKind = "document";

    /// <summary>The prefix of a document link's metadata key: <c>entity:document:</c>.</summary>
    public const string KeyPrefix = DocumentLinks.Prefix + DocumentKind + ":";

    /// <summary>
    /// Maps a hyperlink to the source id of the document it points at, given the source id of the document that holds
    /// it and the link's target as written (<see cref="ExtractedLink.Uri"/>); <see langword="null"/> leaves the link out.
    /// Default is <see cref="ResolveRelative"/>.
    /// </summary>
    public Func<string, string, string?> Resolve { get; init; } = ResolveRelative;

    /// <summary>The metadata key of a link to <paramref name="source"/>: <c>entity:document:{source}</c>.</summary>
    public static string KeyFor(string source) => KeyPrefix + source;

    /// <summary>
    /// A relative link resolved against <paramref name="sourceId"/>, without its query or fragment: <c>../b.md</c> from
    /// <c>plans/a.md</c> is <c>b.md</c>. A source id with a scheme (<c>https://…</c>) resolves as a URI, and an absolute
    /// link on its own scheme and host counts as relative to it. A path source id uses <c>/</c> between folders; a link
    /// that starts with <c>/</c> or climbs above its first folder is left out. So are links to another host or with
    /// another scheme (external URLs, <c>mailto:</c>), links within the document (<c>#section</c>) and links to the
    /// document itself.
    /// </summary>
    public static string? ResolveRelative(string sourceId, string uri)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        ArgumentNullException.ThrowIfNull(uri);

        var target = uri.Trim();
        if (target.IndexOfAny(['#', '?']) is var cut and >= 0)
            target = target[..cut];
        if (target.Length == 0)
            return null;

        var absolute = HasScheme(target) || target.StartsWith("//", StringComparison.Ordinal);
        string resolved;
        if (HasScheme(sourceId))
        {
            if (!Uri.TryCreate(sourceId, UriKind.Absolute, out var baseUri) || !Uri.TryCreate(baseUri, target, out var full))
                return null;
            if (absolute && Uri.Compare(full, baseUri, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) != 0)
                return null;
            resolved = full.ToString();
        }
        else
        {
            if (absolute || target.StartsWith('/'))
                return null;

            var rooted = sourceId.StartsWith('/');
            var folder = sourceId[..(sourceId.LastIndexOf('/') + 1)];
            var segments = new List<string>();
            foreach (var segment in (folder + Uri.UnescapeDataString(target)).Split('/'))
            {
                if (segment is "" or ".")
                    continue;
                if (segment == "..")
                {
                    if (segments.Count == 0)
                        return null;
                    segments.RemoveAt(segments.Count - 1);
                    continue;
                }
                segments.Add(segment);
            }

            if (segments.Count == 0)
                return null;
            resolved = (rooted ? "/" : "") + string.Join('/', segments);
        }

        return string.Equals(resolved, sourceId, StringComparison.Ordinal) ? null : resolved;
    }

    /// <summary>Whether <paramref name="text"/> starts with a URI scheme, such as <c>https:</c> or <c>mailto:</c>.</summary>
    private static bool HasScheme(string text)
    {
        var colon = text.IndexOf(':');
        if (colon < 1 || !char.IsAsciiLetter(text[0]))
            return false;
        for (var i = 1; i < colon; i++)
        {
            if (!char.IsAsciiLetterOrDigit(text[i]) && text[i] is not ('+' or '-' or '.'))
                return false;
        }
        return true;
    }
}
