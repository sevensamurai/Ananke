using System.Runtime.CompilerServices;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// A model held to a time limit per call: an attempt past it is asked again, and a call past it on
/// every attempt ends the run instead of being retried by whatever called it.
/// </summary>
[TestFixture]
public class CallTimeoutTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(50);

    private static readonly AgentRequest Request = new() { Messages = [AgentMessage.User("Go.")] };

    [Test]
    public async Task GenerateAsync_AnAttemptPastItsLimit_IsAskedAgain_AndTheAnswerIsReturned()
    {
        var model = new SilentFor(calls: 1, answer: "Done.");

        var response = await model.WithCallTimeout(Short).GenerateAsync(Request).WaitAsync(TimeSpan.FromSeconds(10));

        response.Text.ShouldBe("Done.");
        model.Asked.ShouldBe(2);
    }

    [Test]
    public async Task GenerateAsync_EveryAttemptPastItsLimit_ThrowsCallTimeout_WhichIsTerminal()
    {
        var model = new SilentFor(calls: int.MaxValue, answer: "never");

        var thrown = await Should.ThrowAsync<CallTimeoutException>(
            () => model.WithCallTimeout(Short).GenerateAsync(Request).WaitAsync(TimeSpan.FromSeconds(10)));

        model.Asked.ShouldBe(2);
        TerminalProviderError.Is(thrown).ShouldBeTrue();
    }

    [Test]
    public async Task GenerateAsync_TheCallersOwnCancellation_IsACancellation_AndIsNotAskedAgain()
    {
        var model = new SilentFor(calls: int.MaxValue, answer: "never");
        using var caller = new CancellationTokenSource(Short);

        await Should.ThrowAsync<OperationCanceledException>(
            () => model.WithCallTimeout(TimeSpan.FromMinutes(5)).GenerateAsync(Request, caller.Token)
                .WaitAsync(TimeSpan.FromSeconds(10)));

        model.Asked.ShouldBe(1);
    }

    [Test]
    public async Task GenerateStreamAsync_AnAttemptThatSendsNothingWithinItsLimit_IsAskedAgain()
    {
        var model = new SilentFor(calls: 1, answer: "Done.");
        var text = "";

        await foreach (var chunk in model.WithCallTimeout(Short).GenerateStreamAsync(Request)
                           .WithCancellation(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token))
            text += chunk.TextDelta;

        text.ShouldBe("Done.");
        model.Asked.ShouldBe(2);
    }

    [Test]
    public async Task GenerateAsync_TheLimit_IsTimedByTheGivenClock()
    {
        var clock = new FakeTimeProvider();
        var model = new SilentFor(calls: 1, answer: "Done.");

        var call = model.WithCallTimeout(TimeSpan.FromMinutes(5), timeProvider: clock).GenerateAsync(Request);

        model.Asked.ShouldBe(1);
        call.IsCompleted.ShouldBeFalse();

        clock.Advance(TimeSpan.FromMinutes(5));

        (await call.WaitAsync(TimeSpan.FromSeconds(10))).Text.ShouldBe("Done.");
        model.Asked.ShouldBe(2);
    }

    [Test]
    public async Task GenerateStreamAsync_TheLimit_IsTimedByTheGivenClock()
    {
        var clock = new FakeTimeProvider();
        var model = new SilentFor(calls: int.MaxValue, answer: "never");

        var stream = model.WithCallTimeout(TimeSpan.FromMinutes(5), attempts: 1, timeProvider: clock)
            .GenerateStreamAsync(Request).GetAsyncEnumerator();
        var next = stream.MoveNextAsync().AsTask();

        model.Asked.ShouldBe(1);
        clock.Advance(TimeSpan.FromMinutes(5));

        await Should.ThrowAsync<CallTimeoutException>(() => next.WaitAsync(TimeSpan.FromSeconds(10)));
        await stream.DisposeAsync();
    }

    [Test]
    public async Task AgentJob_ACallPastItsLimitOnEveryAttempt_IsNotRetriedByTheEngine()
    {
        // The engine retries a timeout up to three times on its own; the limit's attempts are the only
        // ones a call gets.
        var model = new SilentFor(calls: int.MaxValue, answer: "never");

        var job = AgentJobFactory.Create<string, Answer>("answer", model.WithCallTimeout(Short))
            .WithPrompt(s => s)
            .MapResult((s, r) => r.Summary)
            .Build();

        await Should.ThrowAsync<CallTimeoutException>(
            () => job.ExecuteAsync("question").WaitAsync(TimeSpan.FromSeconds(10)));

        model.Asked.ShouldBe(2);
    }

    private sealed record Answer
    {
        public string Summary { get; init; } = "";
    }

    /// <summary>A model that sends nothing for its first <paramref name="calls"/> calls, until cancelled.</summary>
    private sealed class SilentFor(int calls, string answer) : IStreamingAgentModel
    {
        private int _asked;

        public int Asked => Volatile.Read(ref _asked);

        public async Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _asked) <= calls)
                await Task.Delay(Timeout.Infinite, ct);

            return new AgentResponse { Text = answer };
        }

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            var response = await GenerateAsync(request, ct);
            yield return new AgentStreamChunk { TextDelta = response.Text };
            yield return new AgentStreamChunk { CompletedResponse = response };
        }
    }
}
