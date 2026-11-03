using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Routing;

namespace Ananke.Orchestration.Agents;

/// <summary>
/// A budget for model calls made directly, outside a workflow's <c>WithBudget</c>: what they cost, how many tokens
/// they use, and how many replies are cut at their output limit. Every model wrapped with the same
/// <see cref="SpendingLimit"/> spends from it. Once a limit is reached, the next call is refused with a
/// <see cref="SpendingLimitExceededException"/> before it is made.
/// </summary>
/// <remarks>
/// <para>
/// <b>The call that crosses a limit keeps its answer.</b> Its tokens are already paid for, so refusing it would cost
/// the answer and save nothing; it is the next call that is refused.
/// </para>
/// <para>
/// <b>Cost</b> is what the provider reports (<see cref="TokenUsage.Cost"/>), or else the tokens priced at
/// <c>fallbackRates</c>. A call with neither counts its tokens but not its cost, and is counted in
/// <see cref="UnpricedCalls"/>: a cost limit can't see it, so pair one with a token limit or rates.
/// </para>
/// <para>
/// <b>Replies cut at their limit</b> (<see cref="AgentStopReason.MaxTokens"/>) are the sign of a model that writes
/// until it is stopped, such as one repeating a schema it can't keep to.
/// </para>
/// </remarks>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class SpendingLimit
{
    private readonly Lock _lock = new();
    private readonly decimal? _maxCost;
    private readonly long? _maxTokens;
    private readonly int? _maxCappedReplies;
    private readonly ModelCostRates? _fallbackRates;

    /// <summary>A budget of at least one limit.</summary>
    /// <param name="maxCost">The cost at which further calls are refused, in the currency the provider reports.</param>
    /// <param name="maxTokens">The tokens, input and output together, at which further calls are refused.</param>
    /// <param name="maxCappedReplies">How many replies may be cut at their output limit before further calls are refused.</param>
    /// <param name="fallbackRates">The rates a call is priced at when its provider reports no cost.</param>
    /// <exception cref="ArgumentException">No limit is given.</exception>
    public SpendingLimit(decimal? maxCost = null, long? maxTokens = null, int? maxCappedReplies = null, ModelCostRates? fallbackRates = null)
    {
        if (maxCost is null && maxTokens is null && maxCappedReplies is null)
            throw new ArgumentException("A spending limit needs a cost, a token or a capped-reply limit.");
        if (maxCost < 0)
            throw new ArgumentOutOfRangeException(nameof(maxCost), "Must not be negative.");
        if (maxTokens < 0)
            throw new ArgumentOutOfRangeException(nameof(maxTokens), "Must not be negative.");
        if (maxCappedReplies < 0)
            throw new ArgumentOutOfRangeException(nameof(maxCappedReplies), "Must not be negative.");

        _maxCost = maxCost;
        _maxTokens = maxTokens;
        _maxCappedReplies = maxCappedReplies;
        _fallbackRates = fallbackRates;
    }

    /// <summary>The cost of the calls so far.</summary>
    public decimal Spent { get; private set; }

    /// <summary>The tokens the calls so far used, input and output.</summary>
    public long Tokens { get; private set; }

    /// <summary>How many replies so far were cut at their output limit.</summary>
    public int CappedReplies { get; private set; }

    /// <summary>How many calls had no cost reported and no rates to price them.</summary>
    public int UnpricedCalls { get; private set; }

    /// <summary>How many calls were made.</summary>
    public int Calls { get; private set; }

    /// <summary>Throws when a limit has been reached, so no further call is made.</summary>
    internal void ThrowIfReached()
    {
        lock (_lock)
        {
            if (Spent >= _maxCost)
                throw Exceeded($"cost {Format(Spent)} has reached the limit of {Format(_maxCost.Value)}");
            if (Tokens >= _maxTokens)
                throw Exceeded($"{Tokens} tokens have reached the limit of {_maxTokens}");
            if (CappedReplies > _maxCappedReplies)
                throw Exceeded($"{CappedReplies} replies were cut at their output limit, more than the {_maxCappedReplies} allowed");
        }
    }

    /// <summary>Adds one call's usage.</summary>
    internal void Record(AgentResponse response)
    {
        var usage = response.Usage ?? TokenUsage.Zero;
        var cost = usage.Cost
            ?? (_fallbackRates is { } rates
                ? usage.InputTokens / 1000m * rates.CostPer1KInputTokens + usage.OutputTokens / 1000m * rates.CostPer1KOutputTokens
                : null);

        lock (_lock)
        {
            Calls++;
            Tokens += usage.TotalTokens;
            if (cost is { } priced)
                Spent += priced;
            else
                UnpricedCalls++;
            if (response.StopReason == AgentStopReason.MaxTokens)
                CappedReplies++;
        }
    }

    private SpendingLimitExceededException Exceeded(string what) =>
        new($"Spending limit reached after {Calls} call{(Calls == 1 ? "" : "s")}: {what}. No further model call is made.");

    private static string Format(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}

/// <summary>
/// A <see cref="SpendingLimit"/> was reached, so a model call was refused before it was made. Terminal: it is not
/// retried (<see cref="TerminalProviderError.Is"/>).
/// </summary>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class SpendingLimitExceededException(string message) : Exception(message);

/// <summary>A model whose calls spend from a <see cref="SpendingLimit"/>, and are refused once it is reached.</summary>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class SpendingLimitedAgentModel : IStreamingAgentModel, IModelContextResolver
{
    private readonly IAgentModel _inner;

    /// <summary>Holds <paramref name="inner"/>'s calls to <paramref name="limit"/>.</summary>
    public SpendingLimitedAgentModel(IAgentModel inner, SpendingLimit limit)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(limit);
        _inner = inner;
        Limit = limit;
    }

    /// <summary>The limit this model's calls spend from.</summary>
    public SpendingLimit Limit { get; }

    /// <inheritdoc />
    public async Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
    {
        Limit.ThrowIfReached();
        var response = await _inner.GenerateAsync(request, ct).ConfigureAwait(false);
        Limit.Record(response);
        return response;
    }

    /// <inheritdoc />
    /// <remarks>The call is counted from its completed response, when the stream ends.</remarks>
    public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
        AgentRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_inner is not IStreamingAgentModel streaming)
        {
            var response = await GenerateAsync(request, ct).ConfigureAwait(false);
            if (response.Text is not null)
                yield return new AgentStreamChunk { TextDelta = response.Text };
            yield return new AgentStreamChunk { CompletedResponse = response };
            yield break;
        }

        Limit.ThrowIfReached();
        await foreach (var chunk in streaming.GenerateStreamAsync(request, ct).ConfigureAwait(false))
        {
            if (chunk.CompletedResponse is { } completed)
                Limit.Record(completed);
            yield return chunk;
        }
    }

    /// <inheritdoc />
    public ModelContextWindow ResolveContextWindow(AgentRequest request) =>
        _inner is IModelContextResolver resolver ? resolver.ResolveContextWindow(request) : ModelContextWindow.Unknown;
}

/// <summary>Holding a model's calls to a spending limit.</summary>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public static class AgentModelSpendingLimitExtensions
{
    /// <summary>
    /// Makes every call to <paramref name="model"/> spend from <paramref name="limit"/>. Give several models the same
    /// limit to hold them to one budget.
    /// </summary>
    /// <example>
    /// <code>
    /// var limit = new SpendingLimit(maxCost: 1.00m, maxCappedReplies: 3);
    /// var extractor = model.WithSpendingLimit(limit).WithCallTimeout(TimeSpan.FromMinutes(3));
    /// </code>
    /// </example>
    public static SpendingLimitedAgentModel WithSpendingLimit(this IAgentModel model, SpendingLimit limit) => new(model, limit);
}
