using Ananke.Orchestration.Tools;
using Ananke.Federation.Execution;

namespace Ananke.Federation.LocalEmulators;

/// <summary>
/// Stub emulator for <c>computer_use</c> (Claude, Vertex AI / Gemini Enterprise).
/// Records the action sequence and returns a canned screenshot token.
/// No real browser or OS automation is performed.
/// </summary>
internal sealed class ComputerUseExecutor : IPlatformNativeExecutor
{
    private readonly List<string> _actionLog = [];

    public string Capability => "computer_use";
    public bool IsStub => true;

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var action = args.TryGetValue("action", out var a) ? a?.ToString() ?? "unknown" : "unknown";
        _actionLog.Add(action);

        var result = new
        {
            action,
            screenshot = "[STUB] base64-encoded-screenshot-placeholder",
            actionIndex = _actionLog.Count,
            note = "ComputerUseExecutor is a stub. No real screen interaction was performed."
        };
        return Task.FromResult(ToolResult.Json(result));
    }

    /// <summary>All actions recorded in this session.</summary>
    public IReadOnlyList<string> ActionLog => _actionLog;
}

/// <summary>
/// Stub emulator for <c>browser_automation</c> (Foundry / Azure AI).
/// Records the action sequence. No real browser is launched.
/// </summary>
internal sealed class BrowserAutomationExecutor : IPlatformNativeExecutor
{
    private readonly List<string> _actionLog = [];

    public string Capability => "browser_automation";
    public bool IsStub => true;

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var action = args.TryGetValue("action", out var a) ? a?.ToString() ?? "navigate" : "navigate";
        var url = args.TryGetValue("url", out var u) ? u?.ToString() ?? string.Empty : string.Empty;
        _actionLog.Add($"{action} {url}".Trim());

        var result = new
        {
            action,
            url,
            pageSource = "[STUB] <html><body>Browser automation stub</body></html>",
            actionIndex = _actionLog.Count,
            note = "BrowserAutomationExecutor is a stub. No real browser was launched."
        };
        return Task.FromResult(ToolResult.Json(result));
    }

    /// <summary>All actions recorded in this session.</summary>
    public IReadOnlyList<string> ActionLog => _actionLog;
}

/// <summary>
/// Stub emulator for <c>image_generation</c> (Foundry / Azure AI, Vertex AI / Gemini Enterprise).
/// Returns a fixture image token. No real image is generated.
/// </summary>
internal sealed class ImageGenerationExecutor : IPlatformNativeExecutor
{
    public string Capability => "image_generation";
    public bool IsStub => true;

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var prompt = args.TryGetValue("prompt", out var p) ? p?.ToString() ?? "(none)" : "(none)";
        var result = new
        {
            prompt,
            imageUrl = "https://placehold.co/512x512?text=Stub+Image",
            mimeType = "image/png",
            note = "ImageGenerationExecutor is a stub. No real image was generated."
        };
        return Task.FromResult(ToolResult.Json(result));
    }
}

/// <summary>
/// Stub emulator for <c>google_search</c>, <c>google_search_retrieval</c>,
/// and <c>url_context</c> (Vertex AI / Gemini Enterprise).
/// Returns deterministic fixture results.
/// </summary>
internal sealed class GoogleSearchStubExecutor : IPlatformNativeExecutor
{
    private readonly string _capability;

    public GoogleSearchStubExecutor(string capability)
        => _capability = capability;

    public string Capability => _capability;
    public bool IsStub => true;

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var query = args.TryGetValue("query", out var q) ? q?.ToString() ?? "(none)" : "(none)";
        var results = new[]
        {
            new { title = "[Fixture] Google result 1 for: " + query, url = "https://example.com/g1", snippet = "Stub result — no real Google Search call was made." },
            new { title = "[Fixture] Google result 2 for: " + query, url = "https://example.com/g2", snippet = "Stub result — no real Google Search call was made." }
        };
        return Task.FromResult(ToolResult.Json(results));
    }
}

/// <summary>
/// Stub emulator for <c>capture_structured_outputs</c> (Foundry / Azure AI).
/// Passes through the provided JSON value as a successful structured output.
/// </summary>
internal sealed class CaptureStructuredOutputsExecutor : IPlatformNativeExecutor
{
    public string Capability => "capture_structured_outputs";
    public bool IsStub => true;

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var value = args.TryGetValue("value", out var v) ? v : args;
        return Task.FromResult(ToolResult.Json(value));
    }
}
