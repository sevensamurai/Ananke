namespace Ananke.TestHelpers.ProviderStubs;

/// <summary>
/// Canned <c>generateContent</c> responses, in the shape the Gemini API documents.
/// </summary>
public sealed class GeminiStubHandler : ScriptedProviderHandler
{
    /// <summary>
    /// A thinking model bills reasoning as output and reports it separately. The stub reports it
    /// because Gemini does — under-counting it by ignoring this field was a live budget defect until
    /// 2026-08-20, and the fixture should not quietly restate the shape the bug assumed.
    /// </summary>
    private const int ThoughtsTokens = 7;

    protected override (string Payload, string MediaType) Respond(HttpRequestMessage request, string body)
    {
        // Gemini distinguishes the two paths by method name in the URL, not by a body flag.
        var streaming = request.RequestUri!.AbsolutePath
            .Contains("streamGenerateContent", StringComparison.Ordinal);

        // A request that declares tools is answered with a functionCall part, as the service would.
        if (!streaming && FirstToolName(body) is { } tool)
            return (FunctionCall(tool), "application/json");

        var content = Mentions(body, "\"responseSchema\"") || Mentions(body, "\"responseMimeType\"")
            ? StructuredJson
            : ReplyText;

        var finish = Truncates ? "MAX_TOKENS" : "STOP";

        return streaming
            ? (StreamOf(content, finish), "text/event-stream")
            : (GenerateContent(content, finish), "application/json");
    }

    private static string FunctionCall(string tool) =>
        "{\"candidates\":[{\"content\":{\"parts\":[{\"functionCall\":{\"name\":"
        + Quote(tool) + ",\"args\":{}}}],\"role\":\"model\"},\"index\":0,\"finishReason\":\"STOP\"}],"
        + Usage(OutputTokens) + "}";

    private static string GenerateContent(string content, string finish) =>
        $"{{{Candidate(content, finish)},{Usage(OutputTokens)}}}";

    private static string StreamOf(string content, string finish)
    {
        var words = content.Split(' ');

        var events = words.Select((word, i) =>
            "data: {" + Candidate(i == 0 ? word : " " + word) + "}");

        // Usage lands on the final chunk, as it does on the wire.
        var final = "data: {" + Candidate(string.Empty, finish) + "," + Usage(OutputTokens) + "}";

        return Sse([.. events, final]);
    }

    private static string Candidate(string text, string? finish = null)
    {
        var parts = text.Length == 0 ? "[]" : $"[{{\"text\":{Quote(text)}}}]";
        var reason = finish is null ? string.Empty : $",\"finishReason\":{Quote(finish)}";
        return $"\"candidates\":[{{\"content\":{{\"parts\":{parts},\"role\":\"model\"}},\"index\":0{reason}}}]";
    }

    private static string Usage(int candidateTokens) =>
        "\"usageMetadata\":{"
        + $"\"promptTokenCount\":{InputTokens},"
        + $"\"candidatesTokenCount\":{candidateTokens},"
        + $"\"thoughtsTokenCount\":{ThoughtsTokens},"
        + $"\"totalTokenCount\":{InputTokens + candidateTokens + ThoughtsTokens}"
        + "}";

}
