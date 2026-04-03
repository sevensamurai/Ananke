using System.Net;
using System.Runtime.CompilerServices;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class ResilientAgentModelTests
{
    // ── Non-streaming retry ──────────────────────────────────────

    [Test]
    public async Task GenerateAsync_TransientFailure_RetriesAndSucceeds()
    {
        var model = new FailNTimesModel(failCount: 2);
        var resilient = ResilientAgentModel.Create(model, maxRetryAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(10));

        var response = await resilient.GenerateAsync(MakeRequest());

        response.Text.ShouldBe("success");
        model.CallCount.ShouldBe(3); // 2 failures + 1 success
    }

    [Test]
    public async Task GenerateAsync_AllAttemptsExhausted_Throws()
    {
        var model = new FailNTimesModel(failCount: 10);
        var resilient = ResilientAgentModel.Create(model, maxRetryAttempts: 2,
            baseDelay: TimeSpan.FromMilliseconds(10));

        await Should.ThrowAsync<HttpRequestException>(
            () => resilient.GenerateAsync(MakeRequest()));

        model.CallCount.ShouldBe(3); // 1 initial + 2 retries
    }

    [Test]
    public async Task GenerateAsync_NonRetryableException_DoesNotRetry()
    {
        var model = new FailWithModel(new InvalidOperationException("bad input"));
        var resilient = ResilientAgentModel.Create(model, maxRetryAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(10));

        await Should.ThrowAsync<InvalidOperationException>(
            () => resilient.GenerateAsync(MakeRequest()));

        model.CallCount.ShouldBe(1);
    }

    // ── Streaming retry (first-chunk establishment) ──────────────

    [Test]
    public async Task GenerateStreamAsync_TransientOnFirstChunk_RetriesAndStreams()
    {
        var model = new FailNTimesModel(failCount: 1);
        var resilient = ResilientAgentModel.Create(model, maxRetryAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(10));

        var chunks = new List<string>();
        await foreach (var chunk in resilient.GenerateStreamAsync(MakeRequest()))
        {
            if (chunk.TextDelta is not null)
                chunks.Add(chunk.TextDelta);
        }

        chunks.ShouldBe(["success"]);
        model.CallCount.ShouldBe(2); // 1 failure + 1 success
    }

    [Test]
    public async Task GenerateStreamAsync_NoFailure_StreamsNormally()
    {
        var model = new FailNTimesModel(failCount: 0);
        var resilient = ResilientAgentModel.Create(model, maxRetryAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(10));

        var chunks = new List<string>();
        await foreach (var chunk in resilient.GenerateStreamAsync(MakeRequest()))
        {
            if (chunk.TextDelta is not null)
                chunks.Add(chunk.TextDelta);
        }

        chunks.ShouldBe(["success"]);
        model.CallCount.ShouldBe(1);
    }

    // ── IsRateLimitException detection ───────────────────────────

    [Test]
    public void IsRateLimitException_HttpRequestException429_ReturnsTrue()
    {
        var ex = new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests);
        ResilientAgentModel.IsRateLimitException(ex).ShouldBeTrue();
    }

    [Test]
    public void IsRateLimitException_HttpRequestException500_ReturnsFalse()
    {
        var ex = new HttpRequestException("server error", null, HttpStatusCode.InternalServerError);
        ResilientAgentModel.IsRateLimitException(ex).ShouldBeFalse();
    }

    [Test]
    public void IsRateLimitException_InnerException429_ReturnsTrue()
    {
        var inner = new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests);
        var wrapper = new Exception("outer", inner);
        ResilientAgentModel.IsRateLimitException(wrapper).ShouldBeTrue();
    }

    [Test]
    public void IsRateLimitException_UnrelatedType_ReturnsFalse()
    {
        var ex = new InvalidOperationException("nope");
        ResilientAgentModel.IsRateLimitException(ex).ShouldBeFalse();
    }

    // ── Custom retry predicate ───────────────────────────────────

    [Test]
    public async Task GenerateAsync_CustomPredicate_RetriesOnCustomException()
    {
        var model = new FailWithModel(new InvalidOperationException("transient"), failCount: 1);
        var resilient = ResilientAgentModel.Create(model, maxRetryAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(10),
            shouldRetry: ex => ex is InvalidOperationException);

        var response = await resilient.GenerateAsync(MakeRequest());

        response.Text.ShouldBe("success");
        model.CallCount.ShouldBe(2);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static AgentRequest MakeRequest() => new()
    {
        Messages = [AgentMessage.User("test")]
    };

    /// <summary>
    /// Throws HttpRequestException(429) for the first N calls, then returns success.
    /// </summary>
    private sealed class FailNTimesModel(int failCount) : IStreamingAgentModel
    {
        public int CallCount { get; private set; }

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            CallCount++;
            if (CallCount <= failCount)
                throw new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests);
            return Task.FromResult(new AgentResponse { Text = "success" });
        }

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            CallCount++;
            if (CallCount <= failCount)
                throw new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests);

            await Task.Yield();
            yield return new AgentStreamChunk { TextDelta = "success" };
            yield return new AgentStreamChunk
            {
                CompletedResponse = new AgentResponse { Text = "success" }
            };
        }
    }

    /// <summary>
    /// Throws a specific exception for the first N calls, then returns success.
    /// </summary>
    private sealed class FailWithModel(Exception ex, int failCount = int.MaxValue) : IStreamingAgentModel
    {
        public int CallCount { get; private set; }

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            CallCount++;
            if (CallCount <= failCount)
                throw ex;
            return Task.FromResult(new AgentResponse { Text = "success" });
        }

        public IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
