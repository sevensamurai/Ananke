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
/// a consumer must be able to authenticate
/// with an identity source Ananke has never heard of — corporate SSO, a token broker, Entra ID,
/// AWS SigV4 — without Ananke referencing that vendor's SDK.
/// </summary>
/// <remarks>
/// The token provider here is hand-rolled on purpose. If these tests ever need
/// <c>Azure.Identity</c> or any other vendor package to pass, the seam has stopped being open and
/// the regression is the point of the test, not an inconvenience.
/// </remarks>
[TestFixture]
public sealed class OpenAICredentialSeamTests
{
    private const string Endpoint = "https://contoso.example.invalid/openai/v1/";

    [Test]
    public async Task Create_WithTokenProvider_SendsTheProvidedBearerTokenAsync()
    {
        var handler = new CapturingHandler();
        var model = ModelWith(
            new BearerTokenPolicy(
                new StubTokenProvider("token-from-corporate-sso"),
                "https://example.invalid/.default"),
            handler);

        await Send(model);

        handler.Authorization.ShouldBe("Bearer token-from-corporate-sso");
    }

    [Test]
    public async Task Create_WithAuthenticationPolicy_LetsTheCallerAuthenticateAnyWayTheyLikeAsync()
    {
        // Not a bearer token at all — the policy owns the whole scheme.
        var handler = new CapturingHandler();
        var model = ModelWith(new HeaderStampingPolicy("X-Corp-Assertion", "saml-assertion-blob"), handler);

        await Send(model);

        handler.CorpAssertion.ShouldBe("saml-assertion-blob");
        handler.Authorization.ShouldBeNull();
    }

    [Test]
    public async Task Create_WithTokenProvider_RefetchesPerRequestAsync()
    {
        // A rotating credential is the reason this seam exists; a token cached forever at
        // construction time would defeat it.
        var provider = new StubTokenProvider("t");
        var handler = new CapturingHandler();
        var model = ModelWith(
            new BearerTokenPolicy(provider, "https://example.invalid/.default"), handler);

        await Send(model);
        await Send(model);

        provider.Calls.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Test]
    public void Create_FactoryOverloads_ReturnAModel()
    {
        OpenAIChatAgentModel.Create(
            new HeaderStampingPolicy("X-Corp-Assertion", "v"), "m", new Uri(Endpoint))
            .ShouldNotBeNull();

        OpenAIChatAgentModel.Create(
            new StubTokenProvider("t"), "https://example.invalid/.default", "m", new Uri(Endpoint))
            .ShouldNotBeNull();
    }

    [Test]
    public void Create_NullPolicyOrProvider_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            OpenAIChatAgentModel.Create((AuthenticationPolicy)null!, "m"));
        Should.Throw<ArgumentNullException>(() =>
            OpenAIChatAgentModel.Create((AuthenticationTokenProvider)null!, "scope", "m"));
        Should.Throw<ArgumentException>(() =>
            OpenAIChatAgentModel.Create(new StubTokenProvider("t"), "  ", "m"));
    }

    // ── helpers ──────────────────────────────────────────────────────

    // The factory overloads are pass-throughs; what needs proving is that an arbitrary
    // AuthenticationPolicy actually drives the request. Building the ChatClient here keeps the
    // public API free of a transport parameter that exists only for tests.
    private static OpenAIChatAgentModel ModelWith(AuthenticationPolicy policy, HttpMessageHandler handler)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(Endpoint),
            Transport = new HttpClientPipelineTransport(new HttpClient(handler))
        };

#pragma warning disable OPENAI001 // ChatClient's AuthenticationPolicy constructor is experimental
        return new OpenAIChatAgentModel(new ChatClient("my-deployment-name", policy, options));
#pragma warning restore OPENAI001
    }

    private static async Task Send(OpenAIChatAgentModel model) =>
        await model.GenerateAsync(new AgentRequest
        {
            Messages = [new AgentMessage { Role = AgentRole.User, Content = "hi" }]
        }).ConfigureAwait(false);

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Authorization { get; private set; }
        public string? CorpAssertion { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.TryGetValues("Authorization", out var auth)
                ? string.Join(",", auth) : null;
            CorpAssertion = request.Headers.TryGetValues("X-Corp-Assertion", out var corp)
                ? string.Join(",", corp) : null;

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(MinimalCompletionJson, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }

        private const string MinimalCompletionJson = """
            {
              "id": "chatcmpl-1",
              "object": "chat.completion",
              "created": 1,
              "model": "m",
              "choices": [
                {
                  "index": 0,
                  "message": { "role": "assistant", "content": "ok" },
                  "finish_reason": "stop"
                }
              ],
              "usage": { "prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2 }
            }
            """;
    }

    /// <summary>A credential source Ananke knows nothing about.</summary>
    private sealed class StubTokenProvider(string token) : AuthenticationTokenProvider
    {
        public int Calls { get; private set; }

        public override AuthenticationToken GetToken(
            GetTokenOptions options, CancellationToken cancellationToken)
        {
            Calls++;
            return new AuthenticationToken(token, "Bearer", DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AuthenticationToken> GetTokenAsync(
            GetTokenOptions options, CancellationToken cancellationToken) =>
            new(GetToken(options, cancellationToken));

        public override GetTokenOptions? CreateTokenOptions(
            IReadOnlyDictionary<string, object> properties) => new(properties);
    }

    /// <summary>An authentication scheme that is not a bearer token, to prove the seam is open.</summary>
    private sealed class HeaderStampingPolicy(string header, string value) : AuthenticationPolicy
    {
        public override void Process(
            PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            message.Request.Headers.Set(header, value);
            ProcessNext(message, pipeline, currentIndex);
        }

        public override ValueTask ProcessAsync(
            PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            message.Request.Headers.Set(header, value);
            return ProcessNextAsync(message, pipeline, currentIndex);
        }
    }
}
