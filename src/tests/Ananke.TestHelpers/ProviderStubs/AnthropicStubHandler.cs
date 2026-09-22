namespace Ananke.TestHelpers.ProviderStubs;

/// <summary>
/// Canned Messages responses, in the shape the Anthropic API documents.
/// </summary>
public sealed class AnthropicStubHandler : ScriptedProviderHandler
{
    protected override (string Payload, string MediaType) Respond(HttpRequestMessage request, string body)
    {
        var streaming = Mentions(body, "\"stream\":true");

        // A request that declares tools is answered with a tool_use block, as the service would.
        if (!streaming && FirstToolName(body) is { } tool)
            return (ToolUse(tool), "application/json");

        // Anthropic has no response_format; the adapter fuses the schema into the system prompt.
        // A real Claude reading that instruction answers with JSON, so the stub does too.
        var content = Mentions(body, "respond with valid JSON")
            ? StructuredJson
            : ReplyText;

        return streaming
            ? (StreamOf(content), "text/event-stream")
            : (Message(content), "application/json");
    }

    private static string ToolUse(string tool) => $$"""
        {
          "id": "msg_conformance",
          "type": "message",
          "role": "assistant",
          "model": "claude-sonnet-5",
          "content": [ { "type": "tool_use", "id": "toolu_conformance_0", "name": {{Quote(tool)}}, "input": {} } ],
          "stop_reason": "tool_use",
          "stop_sequence": null,
          "usage": { "input_tokens": {{InputTokens}}, "output_tokens": {{OutputTokens}} }
        }
        """;

    private static string Message(string content) => $$"""
        {
          "id": "msg_conformance",
          "type": "message",
          "role": "assistant",
          "model": "claude-sonnet-5",
          "content": [ { "type": "text", "text": {{Quote(content)}} } ],
          "stop_reason": "end_turn",
          "stop_sequence": null,
          "usage": { "input_tokens": {{InputTokens}}, "output_tokens": {{OutputTokens}} }
        }
        """;

    private static string StreamOf(string content)
    {
        // input_tokens arrive on message_start, output_tokens on message_delta — the split the
        // adapter reads, and the one the API documents.
        var events = new List<string>
        {
            Event("message_start",
                """{"type":"message_start","message":{"id":"msg_conformance","type":"message","role":"assistant","model":"claude-sonnet-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":"""
                + InputTokens + ""","output_tokens":0}}}"""),
            Event("content_block_start",
                """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""")
        };

        var words = content.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            var text = i == 0 ? words[i] : " " + words[i];
            events.Add(Event("content_block_delta",
                """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"""
                + Quote(text) + "}}"));
        }

        events.Add(Event("content_block_stop", """{"type":"content_block_stop","index":0}"""));
        events.Add(Event("message_delta",
            """{"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":"""
            + OutputTokens + "}}"));
        events.Add(Event("message_stop", """{"type":"message_stop"}"""));

        return Sse([.. events]);
    }

    private static string Event(string name, string data) => $"event: {name}\ndata: {data.Trim()}";

}
