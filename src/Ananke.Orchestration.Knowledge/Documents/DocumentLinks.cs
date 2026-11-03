using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Ananke.Orchestration.Knowledge.Documents;

/// <summary>
/// Links found in text by patterns a product gives, each with the kind of thing it finds, and no model call: a match is
/// the metadata entry <c>entity:{kind}:{match}</c> = <c>{kind}:{match}</c>, the shape <c>DocumentStructureBuilder</c>
/// turns into a <c>mentions</c> edge (to the node <c>entity:{kind}:{match}</c>) and a store can filter on. The kind says
/// what a link is: the same text pattern finds card keys in one set of documents and <c>UTF-8</c> in another.
/// </summary>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public static class DocumentLinks
{
    /// <summary>The prefix of a link's metadata key.</summary>
    public const string Prefix = "entity:";

    /// <summary>
    /// The links <paramref name="patterns"/> (kind → pattern) find in <paramref name="text"/>, each once. A pattern with a
    /// group named <c>key</c> links what the group matched, rather than the whole match. A pattern's own time limit is the
    /// product's to set; one that runs past it finds nothing in that text.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Find(IReadOnlyDictionary<string, Regex> patterns, string text)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(text);

        var links = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (kind, pattern) in patterns)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(kind);
            try
            {
                foreach (Match match in pattern.Matches(text))
                {
                    var value = match.Groups["key"] is { Success: true } key ? key.Value : match.Value;
                    if (value.Length > 0)
                        links.TryAdd($"{Prefix}{kind}:{value}", $"{kind}:{value}");
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // What this pattern found before its time limit stands; the others still run.
            }
        }

        return links;
    }
}
