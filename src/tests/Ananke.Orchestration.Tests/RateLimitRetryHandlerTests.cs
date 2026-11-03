using System.Net;
using System.Text;
using Ananke.A2A.Client;
using Ananke.Abstractions.Agents;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class RateLimitRetryHandlerTests
{
    [Test]
    public async Task SendAsync_A429ThenOk_RetriesAndReturnsOk()
    {
        var inner = new ScriptedHandler(TooManyRequests(), Ok("done"));
        using var client = Client(inner);

        using var response = await client.PostAsync("http://agent/", new StringContent("{}"))
            .WaitAsync(TimeSpan.FromSeconds(10));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        inner.Bodies.ShouldBe(["{}", "{}"], "the retry sends the same body");
    }

    [Test]
    public async Task SendAsync_APersistent429_StopsAfterMaxAttempts()
    {
        var inner = new ScriptedHandler(TooManyRequests(), TooManyRequests(), TooManyRequests(), Ok("late"));
        using var client = Client(inner);

        using var response = await client.PostAsync("http://agent/", new StringContent("{}"))
            .WaitAsync(TimeSpan.FromSeconds(10));

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        inner.Bodies.Count.ShouldBe(3);
    }

    [Test]
    public async Task SendAsync_RetryAfter_WaitsTheStatedDelay()
    {
        var time = new FakeTimeProvider();
        var inner = new ScriptedHandler(TooManyRequests(retryAfter: TimeSpan.FromSeconds(30)), Ok("done"));
        using var client = Client(inner, time);

        var sending = client.PostAsync("http://agent/", new StringContent("{}"));
        await WaitUntil(() => inner.Bodies.Count == 1);

        time.Advance(TimeSpan.FromSeconds(29));
        inner.Bodies.Count.ShouldBe(1, "29 of the 30 seconds asked for have passed");

        time.Advance(TimeSpan.FromSeconds(1));
        using var response = await sending.WaitAsync(TimeSpan.FromSeconds(10));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task SendAsync_RetryAfterBeyondTheLongestWait_ReturnsThe429()
    {
        var inner = new ScriptedHandler(TooManyRequests(retryAfter: TimeSpan.FromHours(1)), Ok("done"));
        using var client = Client(inner);

        using var response = await client.PostAsync("http://agent/", new StringContent("{}"))
            .WaitAsync(TimeSpan.FromSeconds(10));

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        inner.Bodies.Count.ShouldBe(1);
    }

    [Test]
    public async Task SendAsync_A429ForADepletedAccount_ReturnsItWithItsBody()
    {
        var inner = new ScriptedHandler(
            TooManyRequests(body: """{"error":{"code":"insufficient_quota"}}"""), Ok("done"));
        using var client = Client(inner);

        using var response = await client.PostAsync("http://agent/", new StringContent("{}"))
            .WaitAsync(TimeSpan.FromSeconds(10));

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await response.Content.ReadAsStringAsync()).ShouldContain("insufficient_quota");
        inner.Bodies.Count.ShouldBe(1);
    }

    [Test]
    public async Task SendAsync_AnotherStatus_IsNotRetried()
    {
        var inner = new ScriptedHandler(
            new HttpResponseMessage(HttpStatusCode.BadRequest), Ok("done"));
        using var client = Client(inner);

        using var response = await client.PostAsync("http://agent/", new StringContent("{}"))
            .WaitAsync(TimeSpan.FromSeconds(10));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        inner.Bodies.Count.ShouldBe(1);
    }

    [Test]
    public async Task GenerateAsync_AnAgentThatAnswers429Once_Succeeds()
    {
        var inner = new ScriptedHandler(TooManyRequests(), Ok(AgentMessageJson("Hello.")));
        var model = new A2AAgentModel(new A2AAgentModelOptions
        {
            AgentUrl = new Uri("http://agent/"),
            HttpClient = Client(inner)
        });

        var response = await model.GenerateAsync(new AgentRequest { Messages = [AgentMessage.User("Hi")] })
            .WaitAsync(TimeSpan.FromSeconds(10));

        response.Text.ShouldBe("Hello.");
    }

    private static HttpClient Client(HttpMessageHandler inner, TimeProvider? time = null) =>
        new(new RateLimitRetryHandler(time) { BaseDelay = TimeSpan.Zero, InnerHandler = inner });

    private static HttpResponseMessage TooManyRequests(TimeSpan? retryAfter = null, string body = "slow down")
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (retryAfter is { } delay)
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(delay);
        return response;
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static string AgentMessageJson(string text) =>
        $$"""
        {
          "jsonrpc": "2.0",
          "id": 1,
          "result": {
            "kind": "message",
            "role": "agent",
            "messageId": "{{Guid.NewGuid()}}",
            "parts": [{ "kind": "text", "text": "{{text}}" }]
          }
        }
        """;

    private static async Task WaitUntil(Func<bool> condition)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (waited.Elapsed > TimeSpan.FromSeconds(10))
                Assert.Fail("Timed out waiting for the first attempt.");
            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    private sealed class ScriptedHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private int _next;

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            return responses[Math.Min(_next++, responses.Length - 1)];
        }
    }
}
