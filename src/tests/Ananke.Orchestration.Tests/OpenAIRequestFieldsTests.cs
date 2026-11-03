using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.OpenAI;
using OpenAI;
using OpenAI.Chat;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Fields of an OpenAI-compatible endpoint's own that every request carries, such as OpenRouter's
/// <c>provider</c> and <c>session_id</c>. Read off the body as it goes on the wire.
/// </summary>
#pragma warning disable ANNKEEXP003 // WithRequestFields is experimental
[TestFixture]
public sealed class OpenAIRequestFieldsTests
{
    private static readonly Dictionary<string, JsonNode> OpenRouterFields = new()
    {
        ["session_id"] = JsonValue.Create("kira-20261014-200000"),
        ["provider"] = new JsonObject { ["order"] = new JsonArray("z-ai"), ["allow_fallbacks"] = false }
    };

    [Test]
    public async Task Generate_WithRequestFields_SendsEach_BesideTheAdaptersOwn()
    {
        var transport = new Capturing();

        await ModelWith(transport).WithRequestFields(OpenRouterFields).GenerateAsync(Hi());

        var body = JsonNode.Parse(transport.Bodies.ShouldHaveSingleItem())!;
        body["session_id"]!.GetValue<string>().ShouldBe("kira-20261014-200000");
        body["provider"]!["order"]![0]!.GetValue<string>().ShouldBe("z-ai");
        body["provider"]!["allow_fallbacks"]!.GetValue<bool>().ShouldBeFalse();
        body["model"]!.GetValue<string>().ShouldBe("z-ai/glm-5.3");
        body["messages"]!.AsArray().Count.ShouldBe(1);
    }

    [Test]
    public async Task GenerateStream_WithRequestFields_SendsEach()
    {
        var transport = new Capturing(streaming: true);

        await foreach (var _ in ModelWith(transport).WithRequestFields(OpenRouterFields).GenerateStreamAsync(Hi()))
        {
        }

        var body = JsonNode.Parse(transport.Bodies.ShouldHaveSingleItem())!;
        body["session_id"]!.GetValue<string>().ShouldBe("kira-20261014-200000");
        body["provider"]!["order"]![0]!.GetValue<string>().ShouldBe("z-ai");
        body["stream"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Test]
    public async Task WithRequestFields_LeavesTheModelItIsCalledOn_SendingNone()
    {
        var transport = new Capturing();
        var model = ModelWith(transport);
        _ = model.WithRequestFields(OpenRouterFields);

        await model.GenerateAsync(Hi());

        var body = transport.Bodies.ShouldHaveSingleItem();
        body.ShouldNotContain("session_id");
        body.ShouldNotContain("provider");
    }

    [Test]
    public async Task Generate_ARequestsOwnFields_AreSent_OverTheModelsOfTheSameName()
    {
        var transport = new Capturing();
        var request = Hi() with
        {
            RequestFields = new Dictionary<string, JsonNode>
            {
                ["session_id"] = JsonValue.Create("kira-20261015-120000"),
                ["reasoning"] = new JsonObject { ["effort"] = "high" }
            }
        };

        await ModelWith(transport).WithRequestFields(OpenRouterFields).GenerateAsync(request);

        var body = JsonNode.Parse(transport.Bodies.ShouldHaveSingleItem())!;
        body["session_id"]!.GetValue<string>().ShouldBe("kira-20261015-120000");
        body["reasoning"]!["effort"]!.GetValue<string>().ShouldBe("high");
        body["provider"]!["order"]![0]!.GetValue<string>().ShouldBe("z-ai");
    }

    [Test]
    public async Task Generate_ARequestFieldThatIsNotOneField_IsRefused()
    {
        var request = Hi() with { RequestFields = new Dictionary<string, JsonNode> { ["reasoning.effort"] = JsonValue.Create("high") } };

        (await Should.ThrowAsync<ArgumentException>(() => ModelWith(new Capturing()).GenerateAsync(request)))
            .Message.ShouldContain("'reasoning.effort'");
    }

    [TestCase("provider.order")]
    [TestCase("$.provider")]
    [TestCase("")]
    public void WithRequestFields_ANameThatIsNotOneField_IsRefused(string name)
    {
        var model = ModelWith(new Capturing());

        Should.Throw<ArgumentException>(() => model.WithRequestFields(new Dictionary<string, JsonNode> { [name] = JsonValue.Create(1) }))
            .Message.ShouldContain($"'{name}'");
    }

    private static AgentRequest Hi() => new() { Messages = [AgentMessage.User("hi")] };

    private static OpenAIChatAgentModel ModelWith(HttpMessageHandler handler) =>
        new(new ChatClient("z-ai/glm-5.3", new ApiKeyCredential("test-key"), new OpenAIClientOptions
        {
            Endpoint = new Uri("https://openrouter.example.invalid/api/v1/"),
            Transport = new HttpClientPipelineTransport(new HttpClient(handler)),
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
        }));

    /// <summary>Keeps each request's body and answers "ok".</summary>
    private sealed class Capturing(bool streaming = false) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = streaming
                    ? new StringContent(StreamedOk, Encoding.UTF8, "text/event-stream")
                    : new StringContent(CompletedOk, Encoding.UTF8, "application/json")
            };
        }

        private const string CompletedOk = """
            {
              "id": "gen-1",
              "object": "chat.completion",
              "created": 1,
              "model": "z-ai/glm-5.3",
              "choices": [
                { "index": 0, "message": { "role": "assistant", "content": "ok" }, "finish_reason": "stop" }
              ],
              "usage": { "prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2 }
            }
            """;

        private const string StreamedOk =
            "data: {\"id\":\"gen-1\",\"object\":\"chat.completion.chunk\",\"created\":1,"
            + "\"model\":\"z-ai/glm-5.3\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\","
            + "\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}\n\n"
            + "data: [DONE]\n\n";
    }
}
#pragma warning restore ANNKEEXP003
