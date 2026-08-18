using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Ananke.AspNetCore.Sse;

/// <summary>
/// Extension methods for writing Server-Sent Events (SSE) to an <see cref="HttpResponse"/>.
/// <para>
/// SSE is a text-based protocol where each field follows the format <c>field: value\n</c>
/// and each event is terminated by a blank line (<c>\n\n</c>).
/// </para>
/// <seealso href="https://html.spec.whatwg.org/multipage/server-sent-events.html#server-sent-events">
/// WHATWG — Server-Sent Events
/// </seealso>
/// </summary>
public static class SseResponseExtensions
{
    /// <summary>End of a single field (<c>field: value</c>).</summary>
    private const string TerminateField = "\n";

    /// <summary>End of a complete event (blank line after the last field).</summary>
    private const string TerminateEvent = "\n\n";

    /// <summary>
    /// Serialization options that guarantee SSE-safe (single-line) JSON output.
    /// <list type="bullet">
    ///   <item><see cref="JsonSerializerOptions.WriteIndented"/> is <c>false</c> — no structural newlines.</item>
    ///   <item>The default <see cref="System.Text.Encodings.Web.JavaScriptEncoder"/> escapes
    ///         control characters (<c>\n</c> → <c>\\n</c>, <c>\r</c> → <c>\\r</c>) —
    ///         no literal newlines inside string values.</item>
    /// </list>
    /// Together these ensure the serialized JSON never contains characters that would
    /// break SSE framing.
    /// </summary>
    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        WriteIndented = false
    };

    /// <summary>
    /// Configures the response headers for an SSE stream.
    /// Sets <c>Content-Type: text/event-stream</c> and <c>Cache-Control: no-cache</c>.
    /// </summary>
    public static void EnableSse(this HttpResponse response)
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
    }

    /// <summary>
    /// Writes a named SSE event with JSON-serialized data and flushes the response.
    /// </summary>
    /// <param name="response">The HTTP response to write to.</param>
    /// <param name="eventName">The SSE event name (appears in <c>event:</c> field).</param>
    /// <param name="data">The data object, serialized as JSON in the <c>data:</c> field.</param>
    /// <param name="ct">
    /// Typically <see cref="HttpContext.RequestAborted"/> — cancels the write/flush if the
    /// client disconnects mid-stream.
    /// </param>
    public static async Task WriteSseAsync(this HttpResponse response, string eventName, object data, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(data, SseJsonOptions);
        await response.WriteAsync($"event: {eventName}{TerminateField}data: {json}{TerminateEvent}", ct);
        await response.Body.FlushAsync(ct);
    }
}
