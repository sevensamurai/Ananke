using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.OpenAI;
using OpenAI;
using OpenAI.Chat;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// The one rejection this adapter answers by changing the request and trying again.
/// </summary>
/// <remarks>
/// <para>
/// <b>Found live, and it is not a corner case:</b> every current OpenAI model rejects a chat
/// completion carrying function tools —
/// <c>"Function tools with reasoning_effort are not supported for … set reasoning_effort to
/// 'none'"</c> — while this adapter never sets that parameter. It is the service's own default that
/// conflicts, so with no fix, no current model can call a tool through the shipped adapter.
/// </para>
/// <para>
/// <b>Sending it up front would be worse than sending nothing.</b> Confirmed against the live API:
/// non-reasoning models answer <c>"Unrecognized request argument supplied: reasoning_effort"</c>,
/// and older reasoning models answer <c>"does not support 'none' with this model"</c>. So the fix is
/// a reply to an instruction, not a rule — and these tests assert what actually goes on the wire,
/// because that is the only place the difference is visible.
/// </para>
/// </remarks>
[TestFixture]
public sealed class OpenAIReasoningEffortTests
{
    private const string Endpoint = "https://contoso.example.invalid/openai/v1/";

    private const string ToolsRejected =
        "Function tools with reasoning_effort are not supported for gpt-5.6-luna in "
        + "/v1/chat/completions. To use function tools, use /v1/responses or set reasoning_effort "
        + "to 'none'.";

    [Test]
    public async Task Generate_WhenTheServiceAsksForNoReasoningEffort_RetriesOnceWithIt()
    {
        var transport = new ScriptedHandler(ToolsRejected);
        var model = ModelWith(transport);

        var response = await model.GenerateAsync(WithATool());

        response.Text.ShouldBe("ok");
        transport.Bodies.Count.ShouldBe(2, "one rejected call, one that answered the instruction");

        // The first request said nothing about reasoning; only the retry does.
        transport.Bodies[0].ShouldNotContain("reasoning_effort");
        transport.Bodies[1].ShouldContain("\"reasoning_effort\":\"none\"");
    }

    [Test]
    public async Task Generate_WhenNothingIsRejected_NeverMentionsReasoningEffort()
    {
        // The default has to stay "say nothing": the same adapter serves models that reject the
        // parameter outright, and Ollama, vLLM and Azure deployments that have never heard of it.
        var transport = new ScriptedHandler(rejectionMessage: null);
        var model = ModelWith(transport);

        await model.GenerateAsync(WithATool());

        transport.Bodies.ShouldHaveSingleItem();
        transport.Bodies[0].ShouldNotContain("reasoning_effort");
    }

    [Test]
    public async Task Generate_ADifferent400_IsNotAnsweredWithAReasoningEffort()
    {
        // Matched on the instruction, not on the status. A rejection that does not name this fix is
        // somebody else's problem and must surface as it is.
        var transport = new ScriptedHandler("Invalid schema for function 'save': missing 'type'.");
        var model = ModelWith(transport);

        await Should.ThrowAsync<ClientResultException>(model.GenerateAsync(WithATool()));

        transport.Bodies.ShouldHaveSingleItem();
    }

    [Test]
    public async Task GenerateStream_WhenTheServiceAsksForNoReasoningEffort_RetriesOnceWithIt()
    {
        // The same rejection arrives on the first chunk, and a fix that only covered the unary path
        // would be a fix half the callers receive — this adapter has had unary and streaming
        // disagree about a shipped behaviour once already.
        var transport = new ScriptedHandler(ToolsRejected, streaming: true);
        var model = ModelWith(transport);

        var text = new StringBuilder();
        await foreach (var chunk in model.GenerateStreamAsync(WithATool()))
            text.Append(chunk.TextDelta);

        text.ToString().ShouldBe("ok");
        transport.Bodies.Count.ShouldBe(2);
        transport.Bodies[1].ShouldContain("\"reasoning_effort\":\"none\"");
    }

    private static AgentRequest WithATool() => new()
    {
        Messages = [new AgentMessage { Role = AgentRole.User, Content = "hi" }],
        Tools =
        [
            new AgentTool("save", "Writes a file.", """{"type":"object","properties":{}}""")
        ]
    };

    private static OpenAIChatAgentModel ModelWith(HttpMessageHandler handler)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(Endpoint),
            Transport = new HttpClientPipelineTransport(new HttpClient(handler)),
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
        };

        return new OpenAIChatAgentModel(
            new ChatClient("gpt-5.6-luna", new ApiKeyCredential("test-key"), options));
    }

    /// <summary>Rejects the first call with <paramref name="rejectionMessage"/>, then answers.</summary>
    private sealed class ScriptedHandler(string? rejectionMessage, bool streaming = false)
        : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

            if (Bodies.Count == 1 && rejectionMessage is not null)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        "{\"error\":{\"message\":"
                        + System.Text.Json.JsonSerializer.Serialize(rejectionMessage)
                        + ",\"type\":\"invalid_request_error\",\"param\":\"reasoning_effort\"}}",
                        Encoding.UTF8, "application/json")
                };
            }

            return streaming
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(StreamedOk, Encoding.UTF8, "text/event-stream")
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(CompletedOk, Encoding.UTF8, "application/json")
                };
        }

        private const string CompletedOk = """
            {
              "id": "chatcmpl-1",
              "object": "chat.completion",
              "created": 1,
              "model": "gpt-5.6-luna",
              "choices": [
                { "index": 0, "message": { "role": "assistant", "content": "ok" }, "finish_reason": "stop" }
              ],
              "usage": { "prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2 }
            }
            """;

        private const string StreamedOk =
            "data: {\"id\":\"chatcmpl-1\",\"object\":\"chat.completion.chunk\",\"created\":1,"
            + "\"model\":\"gpt-5.6-luna\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\","
            + "\"content\":\"ok\"},\"finish_reason\":null}]}\n\n"
            + "data: {\"id\":\"chatcmpl-1\",\"object\":\"chat.completion.chunk\",\"created\":1,"
            + "\"model\":\"gpt-5.6-luna\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n"
            + "data: [DONE]\n\n";
    }
}
