using System.Net;
using Ananke.Federation.Anthropic;
using Shouldly;

namespace Ananke.Federation.Anthropic.Tests;

[TestFixture]
public sealed class ClaudeManagedAgentsClientTests
{
    // ── PingAsync ─────────────────────────────────────────────────────────────

    [Test]
    public async Task PingAsync_returns_true_on_200()
    {
        using var client = BuildClient(HttpStatusCode.OK, """{"data":[]}""");
        (await client.PingAsync()).ShouldBeTrue();
    }

    [Test]
    public async Task PingAsync_returns_false_on_401()
    {
        using var client = BuildClient(HttpStatusCode.Unauthorized, """{"error":{"message":"Unauthorized"}}""");
        (await client.PingAsync()).ShouldBeFalse();
    }

    [Test]
    public async Task PingAsync_returns_false_on_network_exception()
    {
        var handler = new ThrowingHttpMessageHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri(ClaudeManagedAgentsClient.BaseUrl) };
        using var client = new ClaudeManagedAgentsClient("sk-ant-test", http);
        (await client.PingAsync()).ShouldBeFalse();
    }

    // ── CreateEnvironmentAsync ────────────────────────────────────────────────

    [Test]
    public async Task CreateEnvironmentAsync_returns_id_from_response()
    {
        using var client = BuildClient(HttpStatusCode.OK, """{"id":"env-test-123","type":"environment"}""");
        var id = await client.CreateEnvironmentAsync("my-workflow");
        id.ShouldBe("env-test-123");
    }

    [Test]
    public async Task CreateEnvironmentAsync_throws_on_api_error()
    {
        using var client = BuildClient(HttpStatusCode.BadRequest,
            """{"error":{"type":"invalid_request_error","message":"bad name"}}""");
        await Should.ThrowAsync<HttpRequestException>(() => client.CreateEnvironmentAsync("wf"));
    }

    [Test]
    public async Task CreateEnvironmentAsync_throws_when_response_missing_id()
    {
        using var client = BuildClient(HttpStatusCode.OK, """{"type":"environment"}""");
        await Should.ThrowAsync<InvalidOperationException>(() => client.CreateEnvironmentAsync("wf"));
    }

    // ── CreateAgentAsync ──────────────────────────────────────────────────────

    [Test]
    public async Task CreateAgentAsync_returns_id_from_response()
    {
        using var client = BuildClient(HttpStatusCode.OK, """{"id":"agent-xyz","type":"agent"}""");
        var id = await client.CreateAgentAsync(
            "my-agent", "claude-sonnet-4-5", "You are helpful.",
            new System.Text.Json.Nodes.JsonArray());
        id.ShouldBe("agent-xyz");
    }

    [Test]
    public async Task CreateAgentAsync_includes_environment_id_when_provided()
    {
        string? capturedBody = null;
        var handler = new CapturingHttpMessageHandler(
            HttpStatusCode.OK, """{"id":"agent-1"}""",
            body => capturedBody = body);
        using var http = new HttpClient(handler) { BaseAddress = new Uri(ClaudeManagedAgentsClient.BaseUrl) };
        using var client = new ClaudeManagedAgentsClient("sk-ant-test", http);

        await client.CreateAgentAsync("a", "m", "s",
            new System.Text.Json.Nodes.JsonArray(), environmentId: "env-999");

        capturedBody!.ShouldContain("env-999");
    }

    // ── DeleteAgentAsync / DeleteEnvironmentAsync ─────────────────────────────

    [Test]
    public async Task DeleteAgentAsync_succeeds_on_200()
    {
        using var client = BuildClient(HttpStatusCode.OK, "{}");
        await Should.NotThrowAsync(() => client.DeleteAgentAsync("agent-1"));
    }

    [Test]
    public async Task DeleteAgentAsync_succeeds_on_404_not_found()
    {
        using var client = BuildClient(HttpStatusCode.NotFound,
            """{"error":{"message":"not found"}}""");
        await Should.NotThrowAsync(() => client.DeleteAgentAsync("agent-gone"));
    }

    [Test]
    public async Task DeleteEnvironmentAsync_succeeds_on_200()
    {
        using var client = BuildClient(HttpStatusCode.OK, "{}");
        await Should.NotThrowAsync(() => client.DeleteEnvironmentAsync("env-1"));
    }

    [Test]
    public async Task DeleteAgentAsync_throws_on_server_error()
    {
        using var client = BuildClient(HttpStatusCode.InternalServerError,
            """{"error":{"message":"server error"}}""");
        await Should.ThrowAsync<HttpRequestException>(() => client.DeleteAgentAsync("agent-1"));
    }

    // ── Request headers ───────────────────────────────────────────────────────

    [Test]
    public async Task Requests_carry_required_anthropic_headers()
    {
        // Use a capturing handler but pre-seed the client's DefaultRequestHeaders
        // the same way BuildDefaultClient does, so we confirm the header names/values.
        var capturedHeaders = new Dictionary<string, string?>();
        var handler = new HeaderCapturingHandler(
            HttpStatusCode.OK, """{"id":"env-h"}""",
            capturedHeaders);
        using var http = new HttpClient(handler) { BaseAddress = new Uri(ClaudeManagedAgentsClient.BaseUrl) };
        http.DefaultRequestHeaders.Add("x-api-key", "sk-ant-fake");
        http.DefaultRequestHeaders.Add("anthropic-version", ClaudeManagedAgentsClient.AnthropicVersionHeader);
        http.DefaultRequestHeaders.Add("anthropic-beta", ClaudeManagedAgentsClient.AgentsBetaHeader);

        using var client = new ClaudeManagedAgentsClient("sk-ant-fake", http);
        await client.CreateEnvironmentAsync("hdr-test");

        capturedHeaders.ShouldContainKey("x-api-key");
        capturedHeaders["x-api-key"].ShouldBe("sk-ant-fake");
        capturedHeaders.ShouldContainKey("anthropic-version");
        capturedHeaders["anthropic-version"].ShouldBe(ClaudeManagedAgentsClient.AnthropicVersionHeader);
        capturedHeaders.ShouldContainKey("anthropic-beta");
        capturedHeaders["anthropic-beta"].ShouldBe(ClaudeManagedAgentsClient.AgentsBetaHeader);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static ClaudeManagedAgentsClient BuildClient(HttpStatusCode status, string json)
    {
        var handler = new StaticHttpMessageHandler(status, json);
        var http = new HttpClient(handler) { BaseAddress = new Uri(ClaudeManagedAgentsClient.BaseUrl) };
        return new ClaudeManagedAgentsClient("sk-ant-test", http);
    }
}

internal sealed class StaticHttpMessageHandler(HttpStatusCode status, string json) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}

internal sealed class ThrowingHttpMessageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromException<HttpResponseMessage>(new HttpRequestException("network failure"));
}

internal sealed class CapturingHttpMessageHandler(
    HttpStatusCode status, string json, Action<string> onBody) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is not null
            ? await request.Content.ReadAsStringAsync(ct)
            : string.Empty;
        onBody(body);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }
}

internal sealed class HeaderCapturingHandler(
    HttpStatusCode status, string json, Dictionary<string, string?> captured) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        foreach (var header in request.Headers)
            captured[header.Key] = header.Value.FirstOrDefault();

        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
    }
}
