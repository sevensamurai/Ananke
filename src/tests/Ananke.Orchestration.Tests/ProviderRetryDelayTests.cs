using System.Diagnostics;
using System.Net;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Middleware;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Reading the one number a rate-limited provider actually gives you.
/// </summary>
/// <remarks>
/// <para>
/// Found live: a Gemini 429 whose body said <c>"retryDelay": "58s"</c> was retried three times in
/// four seconds and the run died. Three attempts against a limit measured in minutes are three
/// attempts spent before the window moves at all.
/// </para>
/// <para>
/// Parsing an error message is not elegant, and the alternative is a package reference per provider
/// inside the engine. What keeps it honest is the cap: a stated delay longer than
/// <see cref="ProviderRetryDelay.Longest"/> is ignored rather than slept through, so a provider
/// asking for an hour fails the call with its own message instead of hanging the run.
/// </para>
/// </remarks>
[TestFixture]
public class ProviderRetryDelayTests
{
    [Test]
    public void From_AGoogleRetryInfoBody_ReadsTheStatedDelay() =>
        ProviderRetryDelay.From(new Exception(
            """{"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"58s"}"""))
            .ShouldBe(TimeSpan.FromSeconds(58));

    [Test]
    public void From_APleaseRetryInSentence_ReadsTheStatedDelay() =>
        ProviderRetryDelay.From(new Exception("Quota exceeded. Please retry in 27.372244281s."))
            .ShouldBe(TimeSpan.FromSeconds(27.372244281));

    [Test]
    public void From_ARetryAfterHeaderQuotedIntoTheMessage_ReadsIt() =>
        ProviderRetryDelay.From(new Exception("429 Too Many Requests (Retry-After: 30)"))
            .ShouldBe(TimeSpan.FromSeconds(30));

    [Test]
    public void From_AnSdkThatModelsItAsAProperty_PrefersTheProperty() =>
        ProviderRetryDelay.From(new RateLimited(TimeSpan.FromSeconds(12), "retry in 99s"))
            .ShouldBe(TimeSpan.FromSeconds(12));

    [Test]
    public void From_TheProviderExceptionWrapped_IsStillFound()
    {
        // What the engine actually catches: its own InvalidOperationException around the SDK's.
        var wrapped = new InvalidOperationException(
            "[agent] LLM call failed after 3 attempts.",
            new Exception("""Quota exceeded. {"retryDelay":"45s"}"""));

        ProviderRetryDelay.From(wrapped).ShouldBe(TimeSpan.FromSeconds(45));
    }

    [Test]
    public void From_AMessageWithNumbersButNoStatedDelay_SaysNothing() =>
        ProviderRetryDelay.From(new Exception("LLM call failed after 3 attempts, 429 seen twice"))
            .ShouldBeNull();

    [Test]
    public void From_AnOrdinaryException_SaysNothing() =>
        ProviderRetryDelay.From(new InvalidOperationException("something else went wrong"))
            .ShouldBeNull();

    [Test]
    public void From_Null_SaysNothing() => ProviderRetryDelay.From(null).ShouldBeNull();

    [Test]
    public void From_ADelayLongerThanTheCap_IsIgnoredRatherThanSleptThrough()
    {
        // A daily quota answers with the time until midnight. Waiting it out inside a retry loop is
        // not resilience, it is a hang with a reason attached.
        var stated = (int)ProviderRetryDelay.Longest.TotalSeconds + 60;

        ProviderRetryDelay.From(new Exception($"Please retry in {stated}s")).ShouldBeNull();
    }

    [Test]
    public void From_AZeroDelay_SaysNothing() =>
        ProviderRetryDelay.From(new Exception("""{"retryDelay":"0s"}""")).ShouldBeNull();

    // ── Through the engine ──

    [Test]
    [CancelAfter(30_000)]
    public async Task Retry_AProviderThatStatesADelay_WaitsThatRatherThanTheConfiguredBackoff(
        CancellationToken ct)
    {
        // The discriminator: the configured backoff is ten seconds and the provider asked for a
        // third of one. A run that honours the curve instead of the provider does not finish here.
        var model = new RateLimitedOnce("Quota exceeded. Please retry in 0.3s.");

        var agent = AgentJobFactory.Create<string>("retry-honours-provider", model)
            .WithPrompt(s => s)
            .WithRetry(maxAttempts: 2, baseDelay: TimeSpan.FromSeconds(10))
            .MapResult((_, text) => text)
            .Build();

        var started = Stopwatch.StartNew();
        var result = await agent.ExecuteAsync("go", ct);
        started.Stop();

        result.ShouldBe("ok");
        started.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250));
        started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Test]
    [CancelAfter(30_000)]
    public async Task Retry_TheResilientMiddleware_AlsoHonoursTheStatedDelay(CancellationToken ct)
    {
        // The other retry loop in the tree. Two places retry model calls, and a fix in one of them
        // is a fix a consumer gets depending on how they wired their model.
        var model = ResilientAgentModel.Create(
            new RateLimitedOnce("Quota exceeded. Please retry in 0.3s."),
            maxRetryAttempts: 2,
            baseDelay: TimeSpan.FromSeconds(10));

        var started = Stopwatch.StartNew();
        var response = await model.GenerateAsync(new AgentRequest { Messages = [AgentMessage.User("go")] }, ct);
        started.Stop();

        response.Text.ShouldBe("ok");
        started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    private sealed class RateLimitedOnce(string message) : IStreamingAgentModel
    {
        private int _calls;

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            if (_calls++ == 0)
                throw new HttpRequestException(message, null, HttpStatusCode.TooManyRequests);

            return Task.FromResult(new AgentResponse { Text = "ok" });
        }

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new AgentStreamChunk { TextDelta = "ok" };
        }
    }

    private sealed class RateLimited(TimeSpan retryAfter, string message) : Exception(message)
    {
        public TimeSpan RetryAfter { get; } = retryAfter;
    }
}
