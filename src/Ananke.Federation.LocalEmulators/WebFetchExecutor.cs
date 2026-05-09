using Ananke.Orchestration.Tools;
using Ananke.Federation.Execution;

namespace Ananke.Federation.LocalEmulators;

/// <summary>
/// Real emulator for the <c>web_fetch</c> capability (Claude).
/// Issues an HTTP GET to the requested URL and returns the response body.
/// </summary>
internal sealed class WebFetchExecutor : IPlatformNativeExecutor
{
    private readonly HttpClient _http;

    public WebFetchExecutor(HttpClient? http = null)
        => _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

    public string Capability => "web_fetch";
    public bool IsStub => false;

    public async Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        if (!args.TryGetValue("url", out var urlVal) || urlVal is null)
            return ToolResult.Fatal("Missing required argument: url");

        var url = urlVal.ToString()!;
        try
        {
            var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ToolResult.Ok(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return ToolResult.Error($"web_fetch failed for '{url}': {ex.Message}");
        }
    }
}
