using System.Net;
using System.Net.Sockets;
using Ananke.Orchestration.Tools;
using Ananke.Federation.Execution;

namespace Ananke.Federation.LocalEmulators;

/// <summary>
/// Real emulator for the <c>web_fetch</c> capability (Claude).
/// Issues an HTTP GET to the requested URL and returns the response body.
/// </summary>
/// <remarks>
/// Requests targeting loopback, link-local, or private IP ranges are blocked to
/// prevent server-side request forgery (SSRF). Response bodies are capped at
/// <see cref="MaxResponseBytes"/> to prevent memory exhaustion. Redirects are
/// limited to <see cref="MaxRedirects"/> hops.
/// </remarks>
internal sealed class WebFetchExecutor : IPlatformNativeExecutor
{
    /// <summary>Maximum response body size in bytes (4 MiB).</summary>
    public const int MaxResponseBytes = 4 * 1024 * 1024;

    /// <summary>Maximum number of HTTP redirects followed per request.</summary>
    public const int MaxRedirects = 5;

    private readonly HttpClient _http;

    public WebFetchExecutor(HttpClient? http = null)
    {
        if (http is null)
        {
            var handler = new HttpClientHandler { MaxAutomaticRedirections = MaxRedirects };
            http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        }
        _http = http;
    }

    public string Capability => "web_fetch";
    public bool IsStub => false;

    public async Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        if (!args.TryGetValue("url", out var urlVal) || urlVal is null)
            return ToolResult.Fatal("Missing required argument: url");

        var url = urlVal.ToString()!;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return ToolResult.Fatal($"web_fetch: invalid or non-HTTP(S) URL '{url}'");

        if (IsSsrfBlocked(uri))
            return ToolResult.Fatal($"web_fetch: requests to '{uri.Host}' are blocked (loopback/private/link-local range)");

        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength > MaxResponseBytes)
                return ToolResult.Error($"web_fetch: response for '{url}' exceeds the {MaxResponseBytes / 1024 / 1024} MiB cap ({contentLength} bytes)");

            using var limitedStream = new System.IO.MemoryStream(MaxResponseBytes);
            using var responseStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

            var buffer = new byte[81920];
            int totalRead = 0, read;
            while ((read = await responseStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                totalRead += read;
                if (totalRead > MaxResponseBytes)
                    return ToolResult.Error($"web_fetch: response body for '{url}' exceeded the {MaxResponseBytes / 1024 / 1024} MiB cap");
                await limitedStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }

            var body = System.Text.Encoding.UTF8.GetString(limitedStream.ToArray());
            return ToolResult.Ok(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return ToolResult.Error($"web_fetch failed for '{url}': {ex.Message}");
        }
    }

    private static bool IsSsrfBlocked(Uri uri)
    {
        var host = uri.Host;

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            return true;

        if (IPAddress.TryParse(host, out var ip))
            return IsPrivateOrReservedIp(ip);

        return false;
    }

    private static bool IsPrivateOrReservedIp(IPAddress ip)
    {
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 127                                        // 127.0.0.0/8 loopback
                || b[0] == 10                                         // 10.0.0.0/8
                || (b[0] == 172 && b[1] is >= 16 and <= 31)          // 172.16.0.0/12
                || (b[0] == 192 && b[1] == 168)                      // 192.168.0.0/16
                || (b[0] == 169 && b[1] == 254);                     // 169.254.0.0/16 link-local
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || IPAddress.IsLoopback(ip);   // fe80::/10, ::1

        return false;
    }
}
