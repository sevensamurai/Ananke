using System.Runtime.CompilerServices;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Routing;
using Shouldly;

#pragma warning disable ANNKEEXP004

namespace Ananke.Orchestration.Tests;

/// <summary>
/// <see cref="SpendingLimit"/> on models called directly, outside a workflow's budget: each call is added up, and once a
/// limit is reached the next call is refused before it is made. The answer that crossed the limit is kept.
/// </summary>
[TestFixture]
public class SpendingLimitTests
{
    private static AgentRequest Request => new() { Messages = [AgentMessage.User("hello")] };

    [Test]
    public async Task GenerateAsync_UnderTheLimits_PassesTheCallThrough()
    {
        var inner = new Answers(cost: 0.1m);
        var model = inner.WithSpendingLimit(new SpendingLimit(maxCost: 1m));

        var response = await model.GenerateAsync(Request);

        response.Text.ShouldBe("answer 1");
        inner.Calls.ShouldBe(1);
    }

    [Test]
    public async Task GenerateAsync_OnceTheCostIsReached_RefusesTheNextCall()
    {
        var inner = new Answers(cost: 0.4m);
        var limit = new SpendingLimit(maxCost: 1m);
        var model = inner.WithSpendingLimit(limit);

        for (var i = 0; i < 3; i++)
            await model.GenerateAsync(Request);
        var error = await Should.ThrowAsync<SpendingLimitExceededException>(() => model.GenerateAsync(Request));

        inner.Calls.ShouldBe(3);
        limit.Spent.ShouldBe(1.2m);
        error.Message.ShouldContain("cost");
        error.Message.ShouldContain("1.2");
    }

    [Test]
    public async Task GenerateAsync_NoCostReported_PricesTokensWithTheFallbackRates()
    {
        var inner = new Answers(cost: null, input: 1_000, output: 500);
        var limit = new SpendingLimit(maxCost: 1m, fallbackRates: ModelCostRates.PerMillion(input: 1m, output: 2m));

        await inner.WithSpendingLimit(limit).GenerateAsync(Request);

        limit.Spent.ShouldBe(0.002m);
        limit.UnpricedCalls.ShouldBe(0);
    }

    [Test]
    public async Task GenerateAsync_NoCostAndNoRates_CountsTheCallAsUnpriced()
    {
        var inner = new Answers(cost: null);
        var limit = new SpendingLimit(maxCost: 1m);

        await inner.WithSpendingLimit(limit).GenerateAsync(Request);

        limit.Spent.ShouldBe(0m);
        limit.UnpricedCalls.ShouldBe(1);
    }

    [Test]
    public async Task GenerateAsync_OnceTheTokensAreReached_RefusesTheNextCall()
    {
        var inner = new Answers(cost: null, input: 40, output: 20);
        var model = inner.WithSpendingLimit(new SpendingLimit(maxTokens: 100));

        await model.GenerateAsync(Request);
        await model.GenerateAsync(Request);
        var error = await Should.ThrowAsync<SpendingLimitExceededException>(() => model.GenerateAsync(Request));

        inner.Calls.ShouldBe(2);
        error.Message.ShouldContain("tokens");
    }

    [Test]
    public async Task GenerateAsync_MoreRepliesCutAtTheirCapThanAllowed_RefusesTheNextCall()
    {
        var inner = new Answers(cost: 0m, stop: AgentStopReason.MaxTokens);
        var limit = new SpendingLimit(maxCappedReplies: 2);
        var model = inner.WithSpendingLimit(limit);

        for (var i = 0; i < 3; i++)
            await model.GenerateAsync(Request);
        var error = await Should.ThrowAsync<SpendingLimitExceededException>(() => model.GenerateAsync(Request));

        limit.CappedReplies.ShouldBe(3);
        error.Message.ShouldContain("cut at their output limit");
    }

    [Test]
    public async Task GenerateAsync_TwoModelsSharingALimit_SpendFromOneBudget()
    {
        var limit = new SpendingLimit(maxCost: 0.5m);
        var first = new Answers(cost: 0.3m).WithSpendingLimit(limit);
        var second = new Answers(cost: 0.3m).WithSpendingLimit(limit);

        await first.GenerateAsync(Request);
        await second.GenerateAsync(Request);

        await Should.ThrowAsync<SpendingLimitExceededException>(() => first.GenerateAsync(Request));
        limit.Calls.ShouldBe(2);
    }

    [Test]
    public async Task GenerateStreamAsync_CountsTheCompletedResponse_AndRefusesOnceReached()
    {
        var limit = new SpendingLimit(maxCost: 0.5m);
        var model = (IStreamingAgentModel)new Answers(cost: 0.6m).WithSpendingLimit(limit);

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in model.GenerateStreamAsync(Request))
            chunks.Add(chunk);

        chunks.ShouldContain(c => c.CompletedResponse != null);
        limit.Spent.ShouldBe(0.6m);
        await Should.ThrowAsync<SpendingLimitExceededException>(async () =>
        {
            await foreach (var _ in model.GenerateStreamAsync(Request))
            {
            }
        });
    }

    [Test]
    public void SpendingLimitExceededException_IsTerminal() =>
        TerminalProviderError.Is(new InvalidOperationException("wrapped", new SpendingLimitExceededException("spent"))).ShouldBeTrue();

    [Test]
    public void Constructor_NoLimitAtAll_Throws() =>
        Should.Throw<ArgumentException>(() => new SpendingLimit());

    /// <summary>Answers every call with the same usage, cost and stop reason, counting the calls.</summary>
    private sealed class Answers(decimal? cost, int input = 10, int output = 10, AgentStopReason stop = AgentStopReason.EndTurn)
        : IStreamingAgentModel
    {
        public int Calls { get; private set; }

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new AgentResponse
            {
                Text = $"answer {Calls}",
                Usage = new TokenUsage { InputTokens = input, OutputTokens = output, Cost = cost },
                StopReason = stop,
            });
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
