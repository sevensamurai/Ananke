using System.Net;
using System.Text;
using System.Text.Json;
using Ananke.Orchestration.Tools;
using Ananke.Federation.Execution;

namespace Ananke.Federation.LocalEmulators;

/// <summary>
/// Real emulator for the <c>web_search</c> capability (Claude).
/// Queries DuckDuckGo Lite (no API key required) and returns a plain-text
/// summary of the top results.
/// </summary>
internal sealed class WebSearchExecutor : IPlatformNativeExecutor
{
    private readonly HttpClient _http;

    public WebSearchExecutor(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "Ananke-LocalEmulator/1.0 (web_search)");
    }

    public string Capability => "web_search";
    public bool IsStub => false;

    public async Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        if (!args.TryGetValue("query", out var queryVal) || queryVal is null)
            return ToolResult.Fatal("Missing required argument: query");

        var query = queryVal.ToString()!;
        var maxResults = args.TryGetValue("max_results", out var mr) && mr is not null
            ? Convert.ToInt32(mr) : 5;

        var encodedQuery = WebUtility.UrlEncode(query);
        var url = $"https://lite.duckduckgo.com/lite/?q={encodedQuery}";

        try
        {
            var html = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
            var results = ParseDuckDuckGoLite(html, maxResults);

            var sb = new StringBuilder();
            sb.AppendLine($"Web search results for: {query}");
            sb.AppendLine();
            foreach (var (title, snippet, link) in results)
            {
                sb.AppendLine($"• {title}");
                if (!string.IsNullOrWhiteSpace(snippet)) sb.AppendLine($"  {snippet}");
                if (!string.IsNullOrWhiteSpace(link)) sb.AppendLine($"  {link}");
                sb.AppendLine();
            }
            return ToolResult.Ok(sb.ToString().TrimEnd());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return ToolResult.Error($"web_search failed for '{query}': {ex.Message}");
        }
    }

    /// <summary>
    /// Minimal text extraction from DuckDuckGo Lite HTML. Returns (title, snippet, url) tuples.
    /// DuckDuckGo Lite is plain HTML; no JavaScript required.
    /// </summary>
    private static List<(string Title, string Snippet, string Link)> ParseDuckDuckGoLite(
        string html, int maxResults)
    {
        var results = new List<(string, string, string)>();

        // DDG Lite groups results as: <a class="result-link"> … </a> rows
        // followed by a snippet row.  A lightweight regex-free parse is sufficient.
        var lines = html.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string? pendingTitle = null;
        string? pendingLink = null;

        foreach (var line in lines)
        {
            if (results.Count >= maxResults) break;

            // Result link: <a class="result-link" href="...">Title text</a>
            if (line.Contains("result-link") && line.Contains("<a "))
            {
                var href = Extract(line, "href=\"", "\"");
                var title = StripTags(Extract(line, ">", "</a>"));
                pendingLink = href;
                pendingTitle = title;
                continue;
            }

            // Snippet: <td class="result-snippet">…</td>
            if (pendingTitle is not null && line.Contains("result-snippet"))
            {
                var snippet = StripTags(Extract(line, ">", "</td>"));
                results.Add((pendingTitle, snippet, pendingLink ?? string.Empty));
                pendingTitle = null;
                pendingLink = null;
            }
        }

        return results;
    }

    private static string Extract(string source, string start, string end)
    {
        var si = source.IndexOf(start, StringComparison.Ordinal);
        if (si < 0) return string.Empty;
        si += start.Length;
        var ei = source.IndexOf(end, si, StringComparison.Ordinal);
        return ei < 0 ? source[si..] : source[si..ei];
    }

    private static string StripTags(string html)
    {
        var sb = new StringBuilder(html.Length);
        var inTag = false;
        foreach (var c in html)
        {
            if (c == '<') { inTag = true; continue; }
            if (c == '>') { inTag = false; continue; }
            if (!inTag) sb.Append(c);
        }
        return sb.ToString().Trim();
    }
}
