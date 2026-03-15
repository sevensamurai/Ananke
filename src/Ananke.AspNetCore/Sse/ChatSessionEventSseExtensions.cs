using Ananke.Orchestration.Agents;
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
    public static Task WriteSseAsync(
        this IAsyncEnumerable<ChatSessionEvent> events,
        HttpResponse response) =>
        events.WriteSseAsync(response.WriteSseAsync);

    /// <summary>
    /// Consumes a <see cref="ChatSessionEvent"/> stream and writes corresponding SSE events
    /// via the provided delegate. Useful when the SSE writer is decoupled from a specific
    /// <see cref="HttpResponse"/> (e.g. re-bound per request in session-based scenarios).
    /// </summary>
    /// <param name="events">The async stream of chat session events.</param>
    /// <param name="writeSse">Delegate that writes a named SSE event with data.</param>
    /// <param name="onError">Optional callback invoked with the error message before writing the SSE error event.</param>
    public static async Task WriteSseAsync(
        this IAsyncEnumerable<ChatSessionEvent> events,
        Func<string, object, Task> writeSse,
        Action<string>? onError = null)
    {
        await foreach (var evt in events)
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
