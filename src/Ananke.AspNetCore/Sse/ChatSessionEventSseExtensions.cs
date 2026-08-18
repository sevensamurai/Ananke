using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
using Microsoft.AspNetCore.Http;

namespace Ananke.AspNetCore.Sse;

/// <summary>
/// Extension methods for streaming <see cref="ChatSessionEvent"/> sequences as SSE events.
/// </summary>
public static class ChatSessionEventSseExtensions
{
    /// <summary>
    /// Consumes a <see cref="ChatSessionEvent"/> stream and writes corresponding SSE events
    /// to the given <see cref="HttpResponse"/>.
    /// <para>Event mapping:</para>
    /// <list type="bullet">
    ///   <item><see cref="TextDeltaEvent"/> → <c>event: delta</c></item>
    ///   <item><see cref="AudioDeltaEvent"/> → <c>event: audio_delta</c></item>
    ///   <item><see cref="ToolCallEvent"/> → <c>event: tool_call</c></item>
    ///   <item><see cref="ToolResultEvent"/> → <c>event: tool_result</c></item>
    ///   <item><see cref="InterruptedEvent"/> → <c>event: interrupted</c></item>
    ///   <item><see cref="ResumedEvent"/> → <c>event: resumed</c></item>
    ///   <item><see cref="CompletedEvent"/> → silently consumed (session-level "done" is the caller's responsibility)</item>
    ///   <item><see cref="ErrorEvent"/> → <c>event: error</c></item>
    /// </list>
    /// </summary>
    /// <param name="events">The async stream of chat session events.</param>
    /// <param name="response">The HTTP response to write SSE events to.</param>
    /// <param name="ct">
    /// Stops consuming <paramref name="events"/> when the client disconnects. Pass
    /// <see cref="HttpContext.RequestAborted"/>.
    /// </param>
    public static Task WriteSseAsync(
        this IAsyncEnumerable<ChatSessionEvent> events,
        HttpResponse response,
        CancellationToken ct = default) =>
        events.WriteSseAsync(
            (eventName, data) => response.WriteSseAsync(eventName, data, ct), ct: ct);

    /// <summary>
    /// Consumes a <see cref="ChatSessionEvent"/> stream and writes corresponding SSE events
    /// via the provided delegate. Useful when the SSE writer is decoupled from a specific
    /// <see cref="HttpResponse"/> (e.g. re-bound per request in session-based scenarios).
    /// </summary>
    /// <param name="events">The async stream of chat session events.</param>
    /// <param name="writeSse">Delegate that writes a named SSE event with data.</param>
    /// <param name="onError">Optional callback invoked with the error message before writing the SSE error event.</param>
    /// <param name="ct">
    /// Stops consuming <paramref name="events"/> when the client disconnects. Pass
    /// <see cref="HttpContext.RequestAborted"/>.
    /// <para>
    /// This cancels the <i>consumption</i> of the event stream, which is what stops the model
    /// generating into a dead connection. It is deliberately not threaded into
    /// <paramref name="writeSse"/>: that delegate is caller-supplied and already closes over
    /// whatever response it writes to, so widening its signature would break every binding site
    /// to cancel a write that fails on its own against a disconnected client anyway.
    /// </para>
    /// </param>
    public static async Task WriteSseAsync(
        this IAsyncEnumerable<ChatSessionEvent> events,
        Func<string, object, Task> writeSse,
        Action<string>? onError = null,
        CancellationToken ct = default)
    {
        await foreach (var evt in events.WithCancellation(ct))
        {
            switch (evt)
            {
                case TextDeltaEvent d:
                    await writeSse("delta", new { text = d.Text });
                    break;
                case AudioDeltaEvent a:
                    await writeSse("audio_delta", new { data = Convert.ToBase64String(a.Data), mimeType = a.MimeType });
                    break;
                case ToolCallEvent t:
                    await writeSse("tool_call", new { name = t.Name, args = t.Args });
                    break;
                case ToolResultEvent t:
                    await writeSse("tool_result", new { name = t.Name, result = t.Result });
                    break;
                case InterruptedEvent i:
                    await writeSse("interrupted", new { partialText = i.PartialText });
                    break;
                case ResumedEvent:
                    await writeSse("resumed", new { });
                    break;
                case CompletedEvent:
                    break;
                case ErrorEvent e:
                    onError?.Invoke(e.Message);
                    await writeSse("error", new { message = e.Message });
                    break;
            }
        }
    }
}
