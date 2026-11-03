using System.ClientModel;
using System.ClientModel.Primitives;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Anthropic;
using Ananke.Orchestration.Google;
using Ananke.Orchestration.OpenAI;
using Ananke.Orchestration.Tests.Fakes;
using Ananke.TestHelpers.ProviderStubs;
using Anthropic;
using Google.GenAI;
using OpenAI;
using OpenAI.Chat;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Why a reply stopped, as each adapter maps it from its provider, and what a job does with a
/// reply that was cut off.
/// </summary>
[TestFixture]
public class AgentStopReasonTests
{
    public enum Provider { OpenAI, Anthropic, Gemini }

    private static readonly Provider[] Providers = [Provider.OpenAI, Provider.Anthropic, Provider.Gemini];

    private static readonly AgentTool Tool = new("look_up", "Looks something up.", """{"type":"object","properties":{}}""");

    [TestCaseSource(nameof(Providers))]
    public async Task GenerateAsync_AFinishedReply_StopsWithEndTurn(Provider provider)
    {
        var response = await Model(provider, new()).GenerateAsync(Ask());

        response.StopReason.ShouldBe(AgentStopReason.EndTurn);
    }

    [TestCaseSource(nameof(Providers))]
    public async Task GenerateAsync_AReplyAtTheTokenLimit_StopsWithMaxTokens(Provider provider)
    {
        var response = await Model(provider, new() { Truncates = true }).GenerateAsync(Ask());

        response.StopReason.ShouldBe(AgentStopReason.MaxTokens);
    }

    [TestCaseSource(nameof(Providers))]
    public async Task GenerateAsync_AToolCall_StopsWithToolCalls(Provider provider)
    {
        var response = await Model(provider, new()).GenerateAsync(Ask() with { Tools = [Tool] });

        response.RequiresAction.ShouldBeTrue();
        response.StopReason.ShouldBe(AgentStopReason.ToolCalls);
    }

    [TestCaseSource(nameof(Providers))]
    public async Task GenerateStreamAsync_AFinishedReply_StopsWithEndTurn(Provider provider)
    {
        var response = await Completed(Model(provider, new()), Ask());

        response.StopReason.ShouldBe(AgentStopReason.EndTurn);
    }

    [TestCaseSource(nameof(Providers))]
    public async Task GenerateStreamAsync_AReplyAtTheTokenLimit_StopsWithMaxTokens(Provider provider)
    {
        var response = await Completed(Model(provider, new() { Truncates = true }), Ask());

        response.StopReason.ShouldBe(AgentStopReason.MaxTokens);
    }

    [TestCase(Provider.OpenAI, "\"max_completion_tokens\":123")]
    [TestCase(Provider.Anthropic, "\"max_tokens\":123")]
    [TestCase(Provider.Gemini, "\"maxOutputTokens\":123")]
    public async Task GenerateAsync_MaxOutputTokens_ReachesTheProvider(Provider provider, string field)
    {
        var stub = new Stub();

        await Model(provider, stub).GenerateAsync(Ask() with { MaxOutputTokens = 123 });

        stub.Handler!.LastRequestBody.ShouldNotBeNull().ShouldContain(field);
    }

    [Test]
    public async Task GenerateAsync_AnthropicWithNoLimitAsked_SendsTheRaisedDefault()
    {
        var stub = new Stub();

        await Model(Provider.Anthropic, stub).GenerateAsync(Ask());

        stub.Handler!.LastRequestBody.ShouldNotBeNull().ShouldContain("\"max_tokens\":16384");
    }

    [Test]
    public async Task AgentJob_TruncatedStructuredReply_SaysTruncated()
    {
        // Cut off mid-object: without the stop reason this reads as a formatting slip and a repair
        // round is spent on a reply that can only be cut off again.
        var model = new ScriptedAgentModel(new AgentResponse
        {
            Text = """{"answer":"the first half of an ans""",
            StopReason = AgentStopReason.MaxTokens
        });

        var job = AgentJobFactory.Create<string, Answer>("answer", model)
            .WithPrompt(s => s)
            .MapResult((s, r) => r.Text)
            .Build();

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => job.ExecuteAsync("question").WaitAsync(TimeSpan.FromSeconds(10)));

        thrown.Message.ShouldContain("cut off at the output-token limit");
        model.Requests.Count.ShouldBe(1, "no repair round is spent on a reply that was cut off");
    }

    private sealed record Answer
    {
        public string Text { get; init; } = "";
    }

    private sealed class Stub
    {
        public bool Truncates { get; init; }

        public ScriptedProviderHandler? Handler { get; set; }
    }

    private static AgentRequest Ask() => new() { Messages = [AgentMessage.User("hello")] };

    private static async Task<AgentResponse> Completed(IStreamingAgentModel model, AgentRequest request)
    {
        AgentResponse? completed = null;

        await foreach (var chunk in model.GenerateStreamAsync(request))
            completed = chunk.CompletedResponse ?? completed;

        return completed.ShouldNotBeNull();
    }

    private static IStreamingAgentModel Model(Provider provider, Stub stub)
    {
        switch (provider)
        {
            case Provider.OpenAI:
                {
                    var handler = new OpenAIStubHandler { Truncates = stub.Truncates };
                    stub.Handler = handler;
                    var options = new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(new HttpClient(handler)) };
                    return new OpenAIChatAgentModel(new ChatClient("gpt-4.1-mini", new ApiKeyCredential("stub-key"), options));
                }
            case Provider.Anthropic:
                {
                    var handler = new AnthropicStubHandler { Truncates = stub.Truncates };
                    stub.Handler = handler;
                    return new AnthropicAgentModel(new AnthropicClient(new global::Anthropic.Core.ClientOptions
                    {
                        ApiKey = "stub-key",
                        HttpClient = new HttpClient(handler)
                    }), Ananke.Abstractions.Agents.Models.Anthropic.Sonnet5);
                }
            default:
                {
                    var handler = new GeminiStubHandler { Truncates = stub.Truncates };
                    stub.Handler = handler;
                    return new GeminiAgentModel(new Client(apiKey: "stub-key", clientOptions: new global::Google.GenAI.Types.ClientOptions
                    {
                        HttpClientFactory = () => new HttpClient(handler)
                    }), "gemini-2.5-flash");
                }
        }
    }
}
