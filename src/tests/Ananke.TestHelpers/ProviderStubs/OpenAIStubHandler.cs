namespace Ananke.TestHelpers.ProviderStubs;

/// <summary>
/// Canned Chat Completions responses, in the shape the OpenAI API documents.
/// </summary>
public sealed class OpenAIStubHandler : ScriptedProviderHandler
{
    /// <summary>
    /// The <c>finish_reason</c> a tool call arrives with. OpenAI sends <c>tool_calls</c>; some
    /// compatible servers, Ollama among them, send <c>stop</c>.
    /// </summary>
    public string ToolCallFinish { get; init; } = "tool_calls";

    protected override (string Payload, string MediaType) Respond(HttpRequestMessage request, string body)
    {
        var streaming = Mentions(body, "\"stream\":true");

        // A request that declares tools is answered with a tool call, as the service would.
        if (!streaming && FirstToolName(body) is { } tool)
            return (ToolCall(tool, ToolCallFinish), "application/json");

        var content = Mentions(body, "\"response_format\"")
            ? StructuredJson
            : ReplyText;

        // "length" is the documented finish_reason for a reply cut off at max_completion_tokens.
        var finish = Truncates ? "length" : "stop";

        return streaming
            ? (StreamOf(content, finish), "text/event-stream")
            : (Completion(content, finish), "application/json");
    }

    private static string ToolCall(string tool, string finish) => $$"""
        {
          "id": "chatcmpl-conformance",
          "object": "chat.completion",
          "created": 1755600000,
          "model": "gpt-4.1-mini",
          "choices": [
            {
              "index": 0,
              "message": {
                "role": "assistant",
                "content": null,
                "tool_calls": [
                  {
                    "id": "call_conformance_0",
                    "type": "function",
                    "function": { "name": {{Quote(tool)}}, "arguments": "{}" }
                  }
                ]
              },
              "finish_reason": {{Quote(finish)}}
            }
          ],
          "usage": {
            "prompt_tokens": {{InputTokens}},
            "completion_tokens": {{OutputTokens}},
            "total_tokens": {{InputTokens + OutputTokens}}
          }
        }
        """;

    private static string Completion(string content, string finish) => $$"""
        {
          "id": "chatcmpl-conformance",
          "object": "chat.completion",
          "created": 1755600000,
          "model": "gpt-4.1-mini",
          "choices": [
            {
              "index": 0,
              "message": { "role": "assistant", "content": {{Quote(content)}} },
              "finish_reason": {{Quote(finish)}}
            }
          ],
          "usage": {
            "prompt_tokens": {{InputTokens}},
            "completion_tokens": {{OutputTokens}},
            "total_tokens": {{InputTokens + OutputTokens}}
          }
        }
        """;

    private static string StreamOf(string content, string finish)
    {
        // One delta per word, then a final chunk carrying finish_reason and usage — which is where
        // the Chat Completions stream reports it.
        var deltas = content.Split(' ')
            .Select((word, i) => "data: " + Chunk($$"""
                { "index": 0, "delta": { "role": "assistant", "content": {{Quote(i == 0 ? word : " " + word)}} }, "finish_reason": null }
                """));

        var final = "data: " + Chunk(
            $$"""{ "index": 0, "delta": {}, "finish_reason": {{Quote(finish)}} }""",
            $$"""
              , "usage": { "prompt_tokens": {{InputTokens}}, "completion_tokens": {{OutputTokens}}, "total_tokens": {{InputTokens + OutputTokens}} }
              """);

        return Sse([.. deltas, final, "data: [DONE]"]);
    }

    private static string Chunk(string choice, string extra = "") => $$"""
        {"id":"chatcmpl-conformance","object":"chat.completion.chunk","created":1755600000,"model":"gpt-4.1-mini","choices":[{{choice}}]{{extra}}}
        """;

}
