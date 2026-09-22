using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Anthropic;
using Ananke.Orchestration.Google;
using Ananke.Orchestration.OpenAI;
using Anthropic;
using Google.GenAI;
using Google.GenAI.Types;
using OpenAI;
using OpenAI.Chat;
using Shouldly;

namespace Ananke.Orchestration.Tests.Conformance;

/// <summary>
/// Whether the retry classifier can read a status off each provider's own failure exception.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gap this closes is invisible from either side alone.</b> The classifier is tested against
/// exceptions the tests construct, and every adapter is tested against a stub that answers
/// <c>200 OK</c> — so nothing ever asked what a real SDK throws on a real failure, or whether the
/// classifier can read it. <c>Google.GenAI.ClientError</c> declares <c>int StatusCode</c>, hiding
/// <see cref="HttpRequestException.StatusCode"/>, and <c>Type.GetProperty</c> raised
/// <c>AmbiguousMatchException</c> on it. Reached from an exception filter, where the CLR reads a
/// throwing filter as <see langword="false"/>, that made every Gemini failure retryable —
/// terminal ones included, which is precisely what the classifier exists to stop.
/// </para>
/// <para>
/// <b>Failure shape is a provider contract like any other.</b> An adapter is not conformant because
/// it maps a good response correctly; the interesting half is the one that only happens on a bad
/// day, and it is the half nobody runs.
/// </para>
/// <para>
/// No network and no credentials: each provider's transport seam is given a handler that answers
/// with a chosen status and the body that provider really sends.
/// </para>
/// </remarks>
public abstract class ProviderErrorShapeTests
{
    /// <summary>A moving window — the case retrying exists for.</summary>
    protected const string RateLimitedBody =
        """{"error":{"code":429,"message":"Rate limit reached for requests per minute.","status":"RESOURCE_EXHAUSTED","type":"rate_limit_error"}}""";

    /// <summary>An account with nothing left — the same status code, and waiting cannot fix it.</summary>
    protected const string DepletedBody =
        """{"error":{"code":429,"message":"You exceeded your current quota, please check your plan and billing details.","status":"RESOURCE_EXHAUSTED","type":"insufficient_quota"}}""";

    protected const string NotFoundBody =
        """{"error":{"code":404,"message":"models/nope is not found for API version v1beta.","status":"NOT_FOUND","type":"not_found_error"}}""";

    protected const string UnauthorizedBody =
        """{"error":{"code":401,"message":"Incorrect API key provided.","status":"UNAUTHENTICATED","type":"authentication_error"}}""";

    /// <summary>A model whose transport answers every call with this status and body.</summary>
    protected abstract IStreamingAgentModel CreateFailing(HttpStatusCode status, string body);

    [Test]
    public async Task A429_IsReadAsARateLimit()
    {
        var thrown = await FailureFrom(HttpStatusCode.TooManyRequests, RateLimitedBody);

        ResilientAgentModel.IsRateLimitException(thrown).ShouldBeTrue(
            $"a 429 must be readable as one; the SDK threw {thrown.GetType().FullName}");

        AgentJobEngine<string, string>.DefaultShouldRetry(thrown).ShouldBeTrue(
            "a moving window is the case retrying exists for");
    }

    [Test]
    public async Task ADepleted429_IsARateLimitButIsNotRetried()
    {
        var thrown = await FailureFrom(HttpStatusCode.TooManyRequests, DepletedBody);

        ResilientAgentModel.IsRateLimitException(thrown).ShouldBeTrue(
            "it is still a 429, which is what a caller composing its own predicate asks");

        TerminalProviderError.Is(thrown).ShouldBeTrue(
            "an account out of allowance is not a busy one");

        AgentJobEngine<string, string>.DefaultShouldRetry(thrown).ShouldBeFalse(
            "waiting cannot fix an empty account, and three round trips per node is the cost");
    }

    [Test]
    public async Task A404_IsNotARateLimitAndIsNotRetried()
    {
        // The live failure that exposed the classifier throwing: a wrong model id, retried three
        // times, with the provider's explanation arriving on the third attempt.
        var thrown = await FailureFrom(HttpStatusCode.NotFound, NotFoundBody);

        ResilientAgentModel.IsRateLimitException(thrown).ShouldBeFalse();
        AgentJobEngine<string, string>.DefaultShouldRetry(thrown).ShouldBeFalse();
    }

    [Test]
    public async Task A401_IsNotARateLimitAndIsNotRetried()
    {
        var thrown = await FailureFrom(HttpStatusCode.Unauthorized, UnauthorizedBody);

        ResilientAgentModel.IsRateLimitException(thrown).ShouldBeFalse();
        AgentJobEngine<string, string>.DefaultShouldRetry(thrown).ShouldBeFalse();
    }

    [Test]
    public async Task Classifying_NeverThrows()
    {
        // The property that actually matters. A classifier reached from `catch (…) when (…)` may not
        // raise: the CLR swallows it and reads the filter as false, so a throw does not fail loudly,
        // it silently inverts the decision.
        foreach (var (status, body) in new (HttpStatusCode, string)[]
        {
            (HttpStatusCode.TooManyRequests, RateLimitedBody),
            (HttpStatusCode.NotFound, NotFoundBody),
            (HttpStatusCode.Unauthorized, UnauthorizedBody),
            (HttpStatusCode.InternalServerError, "not json at all")
        })
        {
            var thrown = await FailureFrom(status, body);

            Should.NotThrow(() => ResilientAgentModel.IsRateLimitException(thrown), $"{status}");
            Should.NotThrow(() => TerminalProviderError.Is(thrown), $"{status}");
            Should.NotThrow(() => AgentJobEngine<string, string>.DefaultShouldRetry(thrown), $"{status}");
        }
    }

    /// <summary>The exception this provider's SDK raises for one status.</summary>
    private async Task<Exception> FailureFrom(HttpStatusCode status, string body)
    {
        var model = CreateFailing(status, body);

        try
        {
            await model
                .GenerateAsync(
                    new AgentRequest { Messages = [AgentMessage.User("go")] },
                    TestContext.CurrentContext.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new InvalidOperationException(
            $"the adapter returned normally on HTTP {(int)status}; there is no failure to classify.");
    }

    /// <summary>Answers every request with one status and body.</summary>
    protected sealed class Failing(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }
}

/// <summary>Gemini's failure shape. The adapter this suite was written for.</summary>
[TestFixture]
public sealed class GeminiErrorShapeTests : ProviderErrorShapeTests
{
    protected override IStreamingAgentModel CreateFailing(HttpStatusCode status, string body)
    {
        var options = new ClientOptions
        {
            HttpClientFactory = () => new HttpClient(new Failing(status, body))
        };

        return new GeminiAgentModel(
            new Client(apiKey: "stub-key", clientOptions: options), "gemini-2.5-flash");
    }
}

/// <summary>OpenAI's failure shape, with the SDK's own retries off so ours is what is measured.</summary>
[TestFixture]
public sealed class OpenAIErrorShapeTests : ProviderErrorShapeTests
{
    protected override IStreamingAgentModel CreateFailing(HttpStatusCode status, string body)
    {
        var options = new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(new HttpClient(new Failing(status, body))),
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
        };

        return new OpenAIChatAgentModel(
            new ChatClient("gpt-4.1-mini", new ApiKeyCredential("stub-key"), options));
    }
}

/// <summary>Anthropic's failure shape.</summary>
[TestFixture]
public sealed class AnthropicErrorShapeTests : ProviderErrorShapeTests
{
    protected override IStreamingAgentModel CreateFailing(HttpStatusCode status, string body)
    {
        var options = new global::Anthropic.Core.ClientOptions
        {
            ApiKey = "stub-key",
            HttpClient = new HttpClient(new Failing(status, body)),
            MaxRetries = 0
        };

        return new AnthropicAgentModel(new AnthropicClient(options), Ananke.Abstractions.Agents.Models.Anthropic.Sonnet5);
    }
}
