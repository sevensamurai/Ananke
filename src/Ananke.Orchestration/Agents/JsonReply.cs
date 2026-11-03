namespace Ananke.Orchestration.Agents;

/// <summary>
/// Recovers the JSON object or array from a reply a provider was only <em>asked</em> for a
/// schema, rather than one that enforces it — the two shapes such a reply tends to take: a
/// Markdown code fence, or a sentence of preamble before the JSON starts.
/// </summary>
internal static class JsonReply
{
    /// <summary>
    /// Strips a Markdown code fence, then, if the result still does not start with an object
    /// or array, takes the first <c>{</c> to the last <c>}</c>. Text that already starts with
    /// <c>{</c> or <c>[</c> passes through unchanged.
    /// </summary>
    public static string Extract(string raw)
    {
        var trimmed = StripMarkdownFences(raw);

        if (trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '['))
            return trimmed;

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        return start >= 0 && end > start ? trimmed[start..(end + 1)] : trimmed;
    }

    private static string StripMarkdownFences(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
                trimmed = trimmed[(firstNewline + 1)..];

            if (trimmed.EndsWith("```", StringComparison.Ordinal))
                trimmed = trimmed[..^3].TrimEnd();
        }
        return trimmed;
    }
}
