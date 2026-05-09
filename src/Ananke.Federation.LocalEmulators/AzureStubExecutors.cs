using System.Text.Json;
using Ananke.Orchestration.Tools;
using Ananke.Federation.Execution;

namespace Ananke.Federation.LocalEmulators;

/// <summary>
/// Stub emulator for <c>bing_search</c>, <c>bing_grounding</c>, and
/// <c>bing_custom_search</c> (Foundry / Azure AI).
/// Returns deterministic fixture results. Documented for test use only.
/// </summary>
internal sealed class BingSearchExecutor : IPlatformNativeExecutor
{
    private readonly string _capability;

    public BingSearchExecutor(string capability = "bing_search")
        => _capability = capability;

    public string Capability => _capability;
    public bool IsStub => true;

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var query = args.TryGetValue("query", out var q) ? q?.ToString() ?? "(none)" : "(none)";
        var results = new[]
        {
            new { title = "[Fixture] Result 1 for: " + query, url = "https://example.com/1", snippet = "Stub result — replace with real Bing credentials for production." },
            new { title = "[Fixture] Result 2 for: " + query, url = "https://example.com/2", snippet = "Stub result — replace with real Bing credentials for production." }
        };
        return Task.FromResult(ToolResult.Json(results));
    }
}

/// <summary>
/// Stub emulator for <c>azure_ai_search</c> (Foundry / Azure AI).
/// Returns an in-memory document store response with the same query shape.
/// </summary>
internal sealed class AzureAiSearchExecutor : IPlatformNativeExecutor
{
    private static readonly IReadOnlyList<object> FixtureDocuments =
    [
        new { id = "doc-001", title = "[Fixture] Azure AI Search document 1", content = "Sample content from stub Azure AI Search index.", score = 0.95 },
        new { id = "doc-002", title = "[Fixture] Azure AI Search document 2", content = "More sample content from stub index.",            score = 0.87 }
    ];

    public string Capability => "azure_ai_search";
    public bool IsStub => true;

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
        => Task.FromResult(ToolResult.Json(FixtureDocuments));
}

/// <summary>
/// Stub emulator for <c>sharepoint</c> and <c>sharepoint_grounding</c> (Foundry / Azure AI).
/// Returns a fixture document set.
/// </summary>
internal sealed class SharePointExecutor : IPlatformNativeExecutor
{
    private readonly string _capability;

    public SharePointExecutor(string capability = "sharepoint")
        => _capability = capability;

    public string Capability => _capability;
    public bool IsStub => true;

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var result = new
        {
            files = new[]
            {
                new { name = "[Fixture] Document.docx", url = "https://contoso.sharepoint.com/Shared Documents/Document.docx", lastModified = "2026-01-01" },
                new { name = "[Fixture] Report.xlsx",   url = "https://contoso.sharepoint.com/Shared Documents/Report.xlsx",   lastModified = "2026-02-01" }
            }
        };
        return Task.FromResult(ToolResult.Json(result));
    }
}

/// <summary>
/// Stub emulator for <c>microsoft_fabric</c> (Foundry / Azure AI).
/// Returns a fixture dataset response.
/// </summary>
internal sealed class MicrosoftFabricExecutor : IPlatformNativeExecutor
{
    public string Capability => "microsoft_fabric";
    public bool IsStub => true;

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var result = new
        {
            dataset = "[Fixture] Microsoft Fabric dataset",
            rows = new[] { new { col1 = "a", col2 = 1 }, new { col1 = "b", col2 = 2 } }
        };
        return Task.FromResult(ToolResult.Json(result));
    }
}
