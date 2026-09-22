using System.Net;
using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Ananke.Abstractions.Agents;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Ananke.Orchestration.Bedrock.Tests;

/// <summary>
/// Runs the <b>real</b> adapters against a stub transport — no network, no credentials, no cost.
/// The code under test is the shipped <c>OpenAIChatAgentModel</c> and <c>AnthropicAgentModel</c>
/// reached through the shipped Bedrock composition, not a hand-written fake.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this reaches further than <see cref="AwsSigV4HandlerTests"/>.</b> That fixture proves the
/// signing algorithm against a golden vector, but it signs a request the test itself constructed.
/// Here the request is the one the OpenAI SDK actually serialises, which closes the gap the golden
/// vector cannot: that the payload hash covers the body really sent, and that the signature is
/// computed after the SDK has finished adding its own headers. Signing a request before its content
/// is final produces a signature that verifies locally and earns a 403 from AWS.
/// </para>
/// <para>
/// This harness is also the prerequisite for subclassing the orchestration conformance suite with a
/// real adapter — see the degradation-conformance notes.
/// </para>
/// </remarks>
[TestFixture]
public sealed class BedrockStubTransportTests
{
    private const string ChatCompletionJson = """
        {
          "id": "chatcmpl-stub",
          "object": "chat.completion",
          "created": 1755600000,
          "model": "openai.gpt-oss-20b-1:0",
          "choices": [
            {
              "index": 0,
              "message": { "role": "assistant", "content": "OK" },
              "finish_reason": "stop"
            }
          ],
          "usage": { "prompt_tokens": 9, "completion_tokens": 2, "total_tokens": 11 }
        }
        """;

    private const string MessagesJson = """
        {
          "id": "msg_stub",
          "type": "message",
          "role": "assistant",
          "model": "anthropic.claude-sonnet-5-v1:0",
          "content": [ { "type": "text", "text": "OK" } ],
          "stop_reason": "end_turn",
          "stop_sequence": null,
          "usage": { "input_tokens": 9, "output_tokens": 2 }
        }
        """;

    private static AgentRequest Hello() =>
        new() { Messages = [AgentMessage.User("hello")] };

    // ── the real OpenAI adapter, over Bedrock's compatible endpoint ───

    [Test]
    public async Task Chat_completions_response_is_parsed_by_the_real_adapterAsync()
    {
        var (model, _) = ChatModel(ChatCompletionJson);

        var response = await model.GenerateAsync(Hello());

        response.Text.ShouldBe("OK");
        response.Usage!.InputTokens.ShouldBe(9);
        response.Usage.OutputTokens.ShouldBe(2);
    }

    [Test]
    public async Task Chat_completions_request_reaches_the_openai_compatible_pathAsync()
    {
        var (model, transport) = ChatModel(ChatCompletionJson);

        await model.GenerateAsync(Hello());

        transport.LastUri!.AbsolutePath.ShouldBe("/openai/v1/chat/completions");
        transport.LastUri.Host.ShouldBe("bedrock-runtime.us-east-1.amazonaws.com");
        transport.LastBody!.ShouldContain("\"hello\"");
    }

    // ── AgentRequest.Temperature reaches the wire ────────────────────

    /// <summary>
    /// The strongest available check that <c>AgentRequest.Temperature</c> is honoured: it inspects
    /// the body the SDK actually sent, through the real OpenAI adapter, rather than an option
    /// object the test constructed.
    /// </summary>
    [Test]
    public async Task Temperature_is_sent_when_setAsync()
    {
        var (model, transport) = ChatModel(ChatCompletionJson);

        await model.GenerateAsync(new AgentRequest
        {
            Messages = [AgentMessage.User("hello")],
            Temperature = 0.7
        });

        transport.LastBody!.ShouldContain("\"temperature\"");
        transport.LastBody!.ShouldContain("0.7");
    }

    /// <summary>
    /// Unset must send nothing at all. If <c>null</c> were serialised as <c>0</c>, every request
    /// would silently pin deterministic sampling that the caller never asked for.
    /// </summary>
    [Test]
    public async Task Temperature_is_absent_when_unsetAsync()
    {
        var (model, transport) = ChatModel(ChatCompletionJson);

        await model.GenerateAsync(Hello());

        transport.LastBody!.ShouldNotContain("temperature");
    }

    [Test]
    public async Task Sigv4_hashes_the_body_the_sdk_actually_sentAsync()
    {
        // The assertion the golden vector cannot make: the signed payload hash must match the bytes
        // that left the process, not a body the test invented.
        var (model, transport) = ChatModel(ChatCompletionJson);

        await model.GenerateAsync(Hello());

        var sent = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(transport.LastBody!)));
        transport.LastHeader("x-amz-content-sha256").ShouldBe(sent);
    }

    [Test]
    public async Task Sigv4_signs_with_the_endpoint_region_and_bedrock_serviceAsync()
    {
        var (model, transport) = ChatModel(ChatCompletionJson, region: "eu-central-1");

        await model.GenerateAsync(Hello());

        var authorization = transport.LastHeader("Authorization")!;
        authorization.ShouldStartWith("AWS4-HMAC-SHA256 ");
        authorization.ShouldContain("Credential=AKID/20260820/eu-central-1/bedrock/aws4_request");
        // Headers the SDK adds must be inside the signature: their presence proves signing happened
        // after the SDK finished with the request, not before.
        var signedHeaders = authorization.Split("SignedHeaders=")[1].Split(',')[0];
        signedHeaders.ShouldContain("content-type");
        signedHeaders.ShouldContain("user-agent");
        signedHeaders.ShouldContain("x-amz-content-sha256");
        signedHeaders.Split(';').ShouldBe(signedHeaders.Split(';').Order(StringComparer.Ordinal));
    }

    [Test]
    public async Task Api_key_path_sends_a_bearer_token_and_no_signatureAsync()
    {
        var transport = new StubTransport(ChatCompletionJson);
        var model = BedrockAgentModel.ChatCompletionsWith(
            BedrockEndpoint.Runtime("us-east-1"),
            "openai.gpt-oss-20b-1:0",
            new BedrockApiKeyHandler("bedrock-key") { InnerHandler = transport });

        await model.GenerateAsync(Hello());

        transport.LastHeader("Authorization").ShouldBe("Bearer bedrock-key");
    }

    // ── the real Anthropic adapter, over Bedrock's native endpoint ────

    [Test]
    public async Task Messages_response_is_parsed_by_the_real_adapterAsync()
    {
        var transport = new StubTransport(MessagesJson);
        var model = BedrockAgentModel.MessagesWith(
            BedrockEndpoint.Runtime("us-east-1"),
            "anthropic.claude-sonnet-5-v1:0",
            new BedrockApiKeyHandler("bedrock-key") { InnerHandler = transport });

        var response = await model.GenerateAsync(Hello());

        response.Text.ShouldBe("OK");
        response.Usage!.InputTokens.ShouldBe(9);
        transport.LastHeader("Authorization").ShouldBe("Bearer bedrock-key");
    }

    // ── harness ──────────────────────────────────────────────────────

    private static (Abstractions.Agents.IStreamingAgentModel Model, StubTransport Transport) ChatModel(
        string responseJson, string region = "us-east-1")
    {
        var transport = new StubTransport(responseJson);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero));
        var auth = new AwsSigV4Handler(
            new BasicAWSCredentials("AKID", "SECRET"), region, "bedrock", clock)
        {
            InnerHandler = transport
        };

        return (
            BedrockAgentModel.ChatCompletionsWith(
                BedrockEndpoint.Runtime(region), "openai.gpt-oss-20b-1:0", auth),
            transport);
    }

    /// <summary>
    /// Terminates the handler chain with a canned response, recording what would have gone to AWS.
    /// The request is captured after every handler has run, so it is exactly what the socket would
    /// have seen.
    /// </summary>
    private sealed class StubTransport(string responseJson) : HttpMessageHandler
    {
        private readonly List<KeyValuePair<string, string>> _headers = [];

        public Uri? LastUri { get; private set; }

        public string? LastBody { get; private set; }

        public string? LastHeader(string name) => _headers
            .FirstOrDefault(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase))
            .Value;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            _headers.Clear();
            foreach (var header in request.Headers.Concat(
                         request.Content?.Headers
                             ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>()))
            {
                _headers.Add(new KeyValuePair<string, string>(
                    header.Key, string.Join(",", header.Value)));
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
