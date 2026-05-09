using System.Text;
using Ananke.Orchestration.Tools;
using Ananke.Federation.Execution;

namespace Ananke.Federation.LocalEmulators;

/// <summary>
/// Real emulator for the <c>file_search</c> capability (Foundry / Azure AI).
/// Performs keyword-based search over files in a configurable root directory.
/// Returns matching file paths with the matching lines as snippets.
/// </summary>
internal sealed class FileSearchExecutor : IPlatformNativeExecutor
{
    private readonly string _searchRoot;
    private readonly int _maxResults;
    private readonly IReadOnlyList<string> _extensions;

    private static readonly IReadOnlyList<string> DefaultExtensions =
        [".txt", ".md", ".cs", ".json", ".yaml", ".yml", ".xml", ".py", ".js", ".ts", ".html"];

    public FileSearchExecutor(
        string? searchRoot = null,
        int maxResults = 10,
        IReadOnlyList<string>? extensions = null)
    {
        _searchRoot = searchRoot ?? Directory.GetCurrentDirectory();
        _maxResults = maxResults;
        _extensions = extensions ?? DefaultExtensions;
    }

    public string Capability => "file_search";
    public bool IsStub => false;

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        if (!args.TryGetValue("query", out var queryVal) || queryVal is null)
            return Task.FromResult(ToolResult.Fatal("Missing required argument: query"));

        var query = queryVal.ToString()!;
        var maxResults = args.TryGetValue("max_results", out var mr) && mr is not null
            ? Convert.ToInt32(mr) : _maxResults;

        if (!Directory.Exists(_searchRoot))
            return Task.FromResult(ToolResult.Error($"Search root does not exist: {_searchRoot}"));

        var sb = new StringBuilder();
        sb.AppendLine($"File search results for: {query}");
        sb.AppendLine();

        var hits = 0;
        foreach (var file in EnumerateFiles(_searchRoot, _extensions))
        {
            ct.ThrowIfCancellationRequested();
            if (hits >= maxResults) break;

            try
            {
                var matchLines = FindMatches(file, query);
                if (matchLines.Count == 0) continue;

                var relativePath = Path.GetRelativePath(_searchRoot, file);
                sb.AppendLine($"📄 {relativePath}");
                foreach (var (lineNo, lineText) in matchLines.Take(3))
                    sb.AppendLine($"  L{lineNo}: {lineText.Trim()}");
                sb.AppendLine();
                hits++;
            }
            catch (IOException)
            {
                /* skip locked or unreadable files */
            }
        }

        if (hits == 0)
            return Task.FromResult(ToolResult.Ok($"No files found matching '{query}' in {_searchRoot}"));

        return Task.FromResult(ToolResult.Ok(sb.ToString().TrimEnd()));
    }

    private static IEnumerable<string> EnumerateFiles(string root, IReadOnlyList<string> extensions) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));

    private static List<(int Line, string Text)> FindMatches(string filePath, string query)
    {
        var matches = new List<(int, string)>();
        var lines = File.ReadLines(filePath);
        var i = 0;
        foreach (var line in lines)
        {
            i++;
            if (line.Contains(query, StringComparison.OrdinalIgnoreCase))
                matches.Add((i, line));
        }
        return matches;
    }
}
