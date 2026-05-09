using Ananke.Orchestration.Tools;
using Ananke.Federation.Execution;

namespace Ananke.Federation.LocalEmulators;

/// <summary>
/// Stub emulator for <c>deep_research</c> (Foundry, Vertex AI / Gemini Enterprise).
/// Composes <see cref="WebSearchExecutor"/> and <see cref="WebFetchExecutor"/>
/// in a fixed N-step loop to simulate the multi-step research pattern.
/// Set <see cref="IsStub"/> to <c>true</c> because it does not fully replicate
/// the platform's managed research pipeline (no citation aggregation, no dedup,
/// no summarisation model call).
/// </summary>
internal sealed class DeepResearchExecutor : IPlatformNativeExecutor
{
    private readonly WebSearchExecutor _search;
    private readonly WebFetchExecutor _fetch;
    private readonly int _steps;

    public DeepResearchExecutor(
        WebSearchExecutor? search = null,
        WebFetchExecutor? fetch = null,
        int steps = 3)
    {
        _search = search ?? new WebSearchExecutor();
        _fetch = fetch ?? new WebFetchExecutor();
        _steps = Math.Max(1, steps);
    }

    public string Capability => "deep_research";
    public bool IsStub => true;

    public async Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        if (!args.TryGetValue("topic", out var topicVal) || topicVal is null)
            return ToolResult.Fatal("Missing required argument: topic");

        var topic = topicVal.ToString()!;
        var sections = new System.Text.StringBuilder();
        sections.AppendLine($"# Deep Research (local emulator): {topic}");
        sections.AppendLine($"> Note: This is a {_steps}-step local emulation. Not a full platform deep-research run.");
        sections.AppendLine();

        // Step 1: Initial search
        var searchArgs = new Dictionary<string, object?> { ["query"] = topic, ["max_results"] = 3 };
        var searchResult = await _search.ExecuteAsync(searchArgs, ct).ConfigureAwait(false);

        sections.AppendLine("## Search results");
        sections.AppendLine(searchResult.Value);
        sections.AppendLine();

        // Steps 2..N: Fetch top URLs found in search (simple URL extraction)
        var urls = ExtractUrls(searchResult.Value);
        var fetched = 0;

        foreach (var url in urls)
        {
            if (fetched >= _steps - 1) break;
            ct.ThrowIfCancellationRequested();

            var fetchArgs = new Dictionary<string, object?> { ["url"] = url };
            var fetchResult = await _fetch.ExecuteAsync(fetchArgs, ct).ConfigureAwait(false);

            sections.AppendLine($"## Source: {url}");
            var snippet = fetchResult.IsError
                ? fetchResult.Value
                : Truncate(fetchResult.Value, 1000);
            sections.AppendLine(snippet);
            sections.AppendLine();
            fetched++;
        }

        return ToolResult.Ok(sections.ToString().TrimEnd());
    }

    private static IEnumerable<string> ExtractUrls(string text)
    {
        foreach (var word in text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                word.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                yield return word.Trim('.', ',', ')');
        }
    }

    private static string Truncate(string s, int maxLength) =>
        s.Length <= maxLength ? s : s[..maxLength] + "\n[truncated]";
}
