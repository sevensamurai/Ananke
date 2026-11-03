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
/// What a call's usage says beside its token counts: the tokens spent reasoning, the input tokens read
/// from the provider's cache, and the cost an endpoint such as OpenRouter reports in <c>usage.cost</c>.
/// Read off a canned response, on both the plain and the streamed path.
/// </summary>
[TestFixture]
public sealed class OpenAIUsageTests
{
    private const string Reported =
        """{ "prompt_tokens": 10, "completion_tokens": 7, "total_tokens": 17, "cost": 0.00079575, "completion_tokens_details": { "reasoning_tokens": 5 }, "prompt_tokens_details": { "cached_tokens": 6 } }""";

    private const string CountsOnly = """{ "prompt_tokens": 10, "completion_tokens": 7, "total_tokens": 17 }""";

    [Test]
    public async Task GenerateAsync_AUsageWithReasoningCachedTokensAndACost_CarriesThem()
    {
        var response = await ModelWith(new Answering(Reported)).GenerateAsync(Hi()).WaitAsync(TimeSpan.FromSeconds(10));

        var usage = response.Usage.ShouldNotBeNull();
        usage.OutputTokens.ShouldBe(7);
        usage.ReasoningTokens.ShouldBe(5);
        usage.CachedInputTokens.ShouldBe(6);
        usage.Cost.ShouldBe(0.00079575m);
    }

    [Test]
    public async Task GenerateStreamAsync_AUsageWithReasoningCachedTokensAndACost_CarriesThem()
    {
        var usage = (await Completed(ModelWith(new Answering(Reported, streaming: true)))).Usage.ShouldNotBeNull();

        usage.OutputTokens.ShouldBe(7);
        usage.ReasoningTokens.ShouldBe(5);
        usage.CachedInputTokens.ShouldBe(6);
        usage.Cost.ShouldBe(0.00079575m);
    }

    [Test]
    public async Task GenerateAsync_AUsageWithNone_LeavesThemNull()
    {
        var response = await ModelWith(new Answering(CountsOnly)).GenerateAsync(Hi()).WaitAsync(TimeSpan.FromSeconds(10));

        var usage = response.Usage.ShouldNotBeNull();
        usage.ReasoningTokens.ShouldBeNull();
        usage.CachedInputTokens.ShouldBeNull();
        usage.Cost.ShouldBeNull();
    }

    [Test]
    public async Task GenerateStreamAsync_AUsageWithNone_LeavesThemNull()
    {
        var usage = (await Completed(ModelWith(new Answering(CountsOnly, streaming: true)))).Usage.ShouldNotBeNull();

        usage.ReasoningTokens.ShouldBeNull();
        usage.CachedInputTokens.ShouldBeNull();
        usage.Cost.ShouldBeNull();
    }

    private static async Task<AgentResponse> Completed(OpenAIChatAgentModel model)
    {
        AgentResponse? completed = null;

        await foreach (var chunk in model.GenerateStreamAsync(Hi()).WithCancellation(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token))
            completed = chunk.CompletedResponse ?? completed;

        return completed.ShouldNotBeNull();
    }

    private static AgentRequest Hi() => new() { Messages = [AgentMessage.User("hi")] };

    private static OpenAIChatAgentModel ModelWith(HttpMessageHandler handler) =>
        new(new ChatClient("z-ai/glm-5.3", new ApiKeyCredential("test-key"), new OpenAIClientOptions
        {
            Endpoint = new Uri("https://openrouter.example.invalid/api/v1/"),
            Transport = new HttpClientPipelineTransport(new HttpClient(handler)),
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
        }));

    /// <summary>Answers "ok" with <paramref name="usage"/>, whole or as a stream whose last chunk carries it.</summary>
    private sealed class Answering(string usage, bool streaming = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = streaming
                    ? new StringContent(
                        "data: {\"id\":\"gen-1\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"z-ai/glm-5.3\","
                        + "\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}\n\n"
                        + "data: {\"id\":\"gen-1\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"z-ai/glm-5.3\","
                        + "\"choices\":[],\"usage\":" + usage + "}\n\n"
                        + "data: [DONE]\n\n",
                        Encoding.UTF8, "text/event-stream")
                    : new StringContent(
                        "{\"id\":\"gen-1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"z-ai/glm-5.3\","
                        + "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"ok\"},\"finish_reason\":\"stop\"}],"
                        + "\"usage\":" + usage + "}",
                        Encoding.UTF8, "application/json")
            });
    }
}
