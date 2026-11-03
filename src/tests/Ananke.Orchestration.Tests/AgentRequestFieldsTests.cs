using System.Text.Json.Nodes;
using Ananke.A2A.Client;
using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Distributed;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Anthropic;
using Ananke.Orchestration.Google;
using Ananke.Orchestration.Tests.Fakes;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Fields of an endpoint's own that one request carries: set from a job, kept apart by the cache, and
/// refused by an adapter that has no way to send them.
/// </summary>
#pragma warning disable ANNKEEXP003 // a request's own fields are experimental
[TestFixture]
public sealed class AgentRequestFieldsTests
{
    private static Dictionary<string, JsonNode> Effort(string effort) => new()
    {
        ["reasoning"] = new JsonObject { ["effort"] = effort }
    };

    [Test]
    public async Task AgentJob_WithRequestFields_EachRequestItMakesCarriesThem()
    {
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "echo", "{}"),
            ScriptedAgentModel.Say("""{"Text":"done"}"""));

        var job = AgentJobFactory.Create<string, Answer>("answer", model)
            .WithPrompt(s => s)
            .WithTools(new ToolKit("k").AddTool("echo", "Returns a fixed payload", () => ToolResult.Ok("echoed")))
            .WithRequestFields(Effort("high"))
            .MapResult((_, answer) => answer.Text)
            .Build();

        (await job.ExecuteAsync("question").WaitAsync(TimeSpan.FromSeconds(10))).ShouldBe("done");

        // The tool round, the answer after it, and the structured call that reads it.
        model.Requests.Count.ShouldBe(3);
        foreach (var request in model.Requests)
            request.RequestFields.ShouldNotBeNull()["reasoning"]!["effort"]!.GetValue<string>().ShouldBe("high");
    }

    [Test]
    public async Task Caching_TwoRequestsDifferingOnlyInAField_AreCachedApart()
    {
        await using var store = new InMemoryDistributedLock();
        var inner = new ScriptedAgentModel(ScriptedAgentModel.Say("hello"));
        var caching = new CachingAgentModel(inner, store, TimeSpan.FromMinutes(5));

        await caching.GenerateAsync(Hi() with { RequestFields = Effort("high") });
        await caching.GenerateAsync(Hi() with { RequestFields = Effort("low") });
        await caching.GenerateAsync(Hi() with { RequestFields = Effort("low") });

        inner.Requests.Count.ShouldBe(2);
    }

    /// <summary>A closed local port, so a request that is not refused fails without leaving the machine.</summary>
    private static readonly Uri Nowhere = new("http://127.0.0.1:9/");

    private static IEnumerable<TestCaseData> AdaptersThatSendNoFields()
    {
        yield return new TestCaseData(GeminiAgentModel.Create("test-key", Models.Google.Gemini35FlashLite, Nowhere)).SetName("Gemini");
        yield return new TestCaseData(AnthropicAgentModel.Create("test-key", Models.Anthropic.Starred, Nowhere)).SetName("Anthropic");
        yield return new TestCaseData(new A2AAgentModel(new A2AAgentModelOptions
        {
            AgentUrl = new Uri("http://fake-a2a-agent/"),
            HttpClient = new HttpClient(new NeverCalled()) { BaseAddress = new Uri("http://fake-a2a-agent/") }
        })).SetName("A2A");
    }

    [TestCaseSource(nameof(AdaptersThatSendNoFields))]
    public async Task Generate_ARequestCarryingFields_IsRefused_NamingThem_BeforeAnyCall(IAgentModel adapter)
    {
        var thrown = await Should.ThrowAsync<NotSupportedException>(
            () => adapter.GenerateAsync(Hi() with { RequestFields = Effort("high") }).WaitAsync(TimeSpan.FromSeconds(10)));

        thrown.Message.ShouldContain("'reasoning'");
    }

    private static AgentRequest Hi() => new() { Messages = [AgentMessage.User("hi")] };

    private sealed record Answer
    {
        public string Text { get; init; } = "";
    }

    /// <summary>Fails the test if a request reaches the wire.</summary>
    private sealed class NeverCalled : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A refused request reached the wire.");
    }
}
#pragma warning restore ANNKEEXP003
