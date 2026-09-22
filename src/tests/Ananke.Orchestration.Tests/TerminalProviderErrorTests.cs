using System.Net;
using System.Runtime.CompilerServices;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Middleware;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Telling an account that is busy from one that is empty, when both answer HTTP 429.
/// </summary>
/// <remarks>
/// <para>
/// Found live: a depleted key cost <b>three round trips per node</b> across a whole plan, because
/// "you are going too fast" and "you have run out of credit" are the same status code — and the
/// sentence explaining it was shown on the third attempt rather than the first.
/// </para>
/// <para>
/// The direction of doubt is the part worth pinning. A wrong <em>terminal</em> ends a run that
/// waiting would have rescued; a wrong <em>transient</em> costs two more calls. So anything that
/// states a delay, names a window that moves, or is simply not recognised stays retryable.
/// </para>
/// </remarks>
[TestFixture]
public class TerminalProviderErrorTests
{
    // ── What is terminal ──

    [Test]
    public void Is_AnOpenAIInsufficientQuota_IsTerminal() =>
        TerminalProviderError.Is(new Exception(
            "You exceeded your current quota, please check your plan and billing details. "
            + "(code: insufficient_quota)")).ShouldBeTrue();

    [Test]
    public void Is_AnAnthropicCreditBalance_IsTerminal() =>
        TerminalProviderError.Is(new Exception(
            "Your credit balance is too low to access the Anthropic API.")).ShouldBeTrue();

    [Test]
    public void Is_TheProviderExceptionWrapped_IsStillFound()
    {
        // What a caller actually catches: the engine's own exception around the SDK's.
        var wrapped = new InvalidOperationException(
            "[agent] LLM call failed after 3 attempts.",
            new Exception("You exceeded your current quota. (code: insufficient_quota)"));

        TerminalProviderError.Is(wrapped).ShouldBeTrue();
    }

    [Test]
    public void Is_TheGoogleCreditsFromTheRunThatFoundThis_IsTerminal() =>
        // The live body, verbatim, re-captured from the API on 2026-08-30 — the same account state
        // that produced live run 8. Note what it does not carry: no retryDelay, and no window that
        // moves. Nothing about it changes without somebody paying.
        TerminalProviderError.Is(new Exception(
            "Your prepayment credits are depleted. Please go to AI Studio at "
            + "https://ai.studio/projects to manage your project and billing. Learn more at "
            + "https://ai.google.dev/gemini-api/docs/billing#prepay. "))
            .ShouldBeTrue();

    // ── What is not, and why ──

    [Test]
    public void Is_AFreeTierWindowWordedAsDepletion_IsNotTerminal() =>
        // The case that decides the order of the two tests inside. A free tier answers a per-minute
        // limit with a depleted account's exact wording, and only the quota id says which it is.
        TerminalProviderError.Is(new Exception(
            "You exceeded your current quota, please check your plan and billing details. "
            + "quota_id: GenerateRequestsPerMinutePerProjectPerModel-FreeTier"))
            .ShouldBeFalse();


    [Test]
    public void Is_AGooglePerMinuteQuota_IsNotTerminal() =>
        // The wording that makes this worth testing: an ordinary rate limit described as a quota.
        TerminalProviderError.Is(new Exception(
            "Quota exceeded for quota metric 'GenerateContent requests per minute'."))
            .ShouldBeFalse();

    [Test]
    public void Is_AnErrorThatStatesADelay_IsNotTerminal_WhateverElseItSays()
    {
        // The provider's own instruction outranks anything read out of its prose: something that
        // says when to come back has classified itself, and it did not say "never".
        TerminalProviderError.Is(new Exception(
            "You exceeded your current quota. Please retry in 30s.")).ShouldBeFalse();
    }

    [Test]
    public void Is_AnUnrecognisedError_IsNotTerminal() =>
        // When in doubt, retry. The cost of being wrong here is two extra calls; the cost of being
        // wrong the other way is a run that dies of something waiting would have fixed.
        TerminalProviderError.Is(new Exception("Internal server error.")).ShouldBeFalse();

    [Test]
    public void Is_Nothing_IsNotTerminal() => TerminalProviderError.Is(null).ShouldBeFalse();

    // ── Through the two retry loops ──

    [Test]
    public void DefaultShouldRetry_ADepletedAccount_IsNotRetried()
    {
        var depleted = new HttpRequestException(
            "You exceeded your current quota, please check your plan and billing details.",
            null, HttpStatusCode.TooManyRequests);

        AgentJobEngine<string, string>.DefaultShouldRetry(depleted).ShouldBeFalse();

        // And the narrower question is unchanged: it is still a 429, which is what a caller
        // composing its own predicate is asking.
        ResilientAgentModel.IsRateLimitException(depleted).ShouldBeTrue();
    }

    [Test]
    [CancelAfter(30_000)]
    public async Task Execute_ADepletedAccount_CostsOneRoundTripAndSaysWhyOnTheFirstAttempt(
        CancellationToken ct)
    {
        var model = new Depleted();

        var agent = AgentJobFactory.Create<string>("depleted", model)
            .WithPrompt(s => s)
            .WithRetry(maxAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(10))
            .MapResult((_, text) => text)
            .Build();

        var thrown = await Should.ThrowAsync<HttpRequestException>(agent.ExecuteAsync("go", ct));

        model.Calls.ShouldBe(1, "waiting cannot fix an account with no allowance left");

        // Its own words, not a count of attempts. Whatever records this — a plan node's failure, a
        // log line — keeps the message, so the message has to be the useful one.
        thrown.Message.ShouldContain("insufficient_quota");
    }

    [Test]
    [CancelAfter(30_000)]
    public async Task Generate_ADepletedAccount_IsNotRetriedByTheMiddlewareEither(CancellationToken ct)
    {
        // Two places retry model calls, and which one a consumer gets depends on how they wired
        // their model — so a fix in one of them is a fix half of them receive. The same lesson I6
        // recorded when the stated delay had to be honoured in both.
        var model = new Depleted();
        var resilient = ResilientAgentModel.Create(
            model, maxRetryAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(10));

        await Should.ThrowAsync<HttpRequestException>(
            resilient.GenerateAsync(new AgentRequest { Messages = [AgentMessage.User("go")] }, ct));

        model.Calls.ShouldBe(1);
    }

    [Test]
    [CancelAfter(30_000)]
    public async Task Execute_RetriesThatRunOut_StillCarryTheProvidersOwnWords(CancellationToken ct)
    {
        // The other half of the same complaint. Even when retrying was right, what the caller is
        // handed said only how many attempts had been made.
        var model = new AlwaysRateLimited();

        var agent = AgentJobFactory.Create<string>("busy", model)
            .WithPrompt(s => s)
            .WithRetry(maxAttempts: 2, baseDelay: TimeSpan.FromMilliseconds(10))
            .MapResult((_, text) => text)
            .Build();

        var thrown = await Should.ThrowAsync<InvalidOperationException>(agent.ExecuteAsync("go", ct));

        thrown.Message.ShouldContain("after 2 attempts");
        thrown.Message.ShouldContain("requests per minute");
    }

    // ── Reading the status off a provider's own exception ──

    [Test]
    public void IsRateLimit_AnSdkThatHidesStatusCode_IsStillRead()
    {
        // Google.GenAI.ClientError declares `int StatusCode`, hiding HttpRequestException's
        // `HttpStatusCode? StatusCode`. Type.GetProperty("StatusCode") raises AmbiguousMatchException
        // on exactly that shape — and this is reached from an exception filter, where the CLR
        // swallows the throw and reads the filter as false. Every Gemini failure was therefore
        // retryable, terminal ones included.
        var hiding = new HidesStatusCode("Quota exceeded. Please retry shortly.", 429);

        ResilientAgentModel.IsRateLimitException(hiding).ShouldBeTrue();
    }

    [Test]
    public void IsRateLimit_AHiddenStatusCodeThatIsNota429_IsNotARateLimit()
    {
        // The live case: models/… is not found, retried three times because the classifier threw
        // rather than answering.
        var notFound = new HidesStatusCode(
            "models/gpt-4.1 is not found for API version v1beta.", 404);

        ResilientAgentModel.IsRateLimitException(notFound).ShouldBeFalse();
        AgentJobEngine<string, string>.DefaultShouldRetry(notFound).ShouldBeFalse();
    }

    [Test]
    public void TryGetHttpStatus_AnExceptionThatThrowsWhenRead_AnswersRatherThanThrowing()
    {
        // Nothing about classification may escape into a filter. What it answers matters less than
        // that it answers at all.
        Should.NotThrow(() => ResilientAgentModel.IsRateLimitException(new Hostile()));
    }

    // ── What is not classified, and is worth knowing is not ──

    [Test]
    public void Is_ABedrockServiceQuotaExhaustion_IsNotRecognised() =>
        // Documents a gap rather than asserting a virtue. AWS answers an exhausted model quota with
        // HTTP 400 and this wording, so it is not a 429 and never reaches the retry question — the
        // outcome is right (it fails fast) but nothing marks it terminal, so a reader is handed the
        // raw SDK sentence with no statement that waiting will not help.
        TerminalProviderError.Is(new Exception(
            "You have exceeded the maximum number of tokens for this model."))
            .ShouldBeFalse();

    [Test]
    public void Is_AnInvalidKeyOrMissingModel_IsNotRecognised()
    {
        // Both are as terminal as an empty account and neither is classified, because only 429 is.
        // They are not retried — DefaultShouldRetry needs a 429 — so the cost is that NodeFailure
        // .Terminal reads false for a failure that waiting certainly will not fix.
        TerminalProviderError.Is(new Exception("Incorrect API key provided.")).ShouldBeFalse();
        TerminalProviderError.Is(new Exception("models/x is not found.")).ShouldBeFalse();
    }

    /// <summary>An exception shaped like <c>Google.GenAI.ClientError</c>: it hides the base status.</summary>
    private sealed class HidesStatusCode(string message, int statusCode) : HttpRequestException(message)
    {
        public new int StatusCode { get; } = statusCode;

        public string Status => "NOT_FOUND";
    }

    /// <summary>An exception whose properties refuse to be read.</summary>
    private sealed class Hostile : Exception
    {
        public int StatusCode => throw new InvalidOperationException("no");
    }

    private sealed class Depleted : IStreamingAgentModel
    {
        public int Calls { get; private set; }

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            Calls++;
            throw new HttpRequestException(
                "You exceeded your current quota, please check your plan and billing details. "
                + "(code: insufficient_quota)",
                null, HttpStatusCode.TooManyRequests);
        }

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield break;
        }
    }

    private sealed class AlwaysRateLimited : IStreamingAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            throw new HttpRequestException(
                "Rate limit reached for requests per minute.", null, HttpStatusCode.TooManyRequests);

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield break;
        }
    }
}
