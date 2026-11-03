namespace Ananke.Abstractions.Agents;

/// <summary>
/// Token consumption metadata returned by an LLM call.
/// </summary>
public sealed record TokenUsage
{
    /// <summary>Number of input/prompt tokens consumed.</summary>
    public int InputTokens { get; init; }

    /// <summary>Number of output/completion tokens generated.</summary>
    public int OutputTokens { get; init; }

    /// <summary>Total tokens (input + output).</summary>
    public int TotalTokens => InputTokens + OutputTokens;

    /// <summary>
    /// How many of <see cref="OutputTokens"/> the model spent reasoning, as the provider reports it, or
    /// <see langword="null"/> when it reports none.
    /// </summary>
    public int? ReasoningTokens { get; init; }

    /// <summary>
    /// How many of <see cref="InputTokens"/> the provider read from its cache of an earlier request, as
    /// it reports them, or <see langword="null"/> when it reports none.
    /// </summary>
    public int? CachedInputTokens { get; init; }

    /// <summary>
    /// What the provider says the call cost, in the currency it bills in, or <see langword="null"/> when
    /// it says nothing. Never worked out from rates.
    /// </summary>
    public decimal? Cost { get; init; }

    /// <summary>
    /// Returns a new <see cref="TokenUsage"/> that is the sum of this and <paramref name="other"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="ReasoningTokens"/>, <see cref="CachedInputTokens"/> and <see cref="Cost"/> are summed
    /// where both carry them, and kept where one does, so a sum over calls of which some reported no cost
    /// is the cost of those that did.
    /// </remarks>
    public TokenUsage Add(TokenUsage other) => new()
    {
        InputTokens = InputTokens + other.InputTokens,
        OutputTokens = OutputTokens + other.OutputTokens,
        ReasoningTokens = ReasoningTokens is { } reasoning ? reasoning + (other.ReasoningTokens ?? 0) : other.ReasoningTokens,
        CachedInputTokens = CachedInputTokens is { } cached ? cached + (other.CachedInputTokens ?? 0) : other.CachedInputTokens,
        Cost = Cost is { } cost ? cost + (other.Cost ?? 0m) : other.Cost
    };

    /// <summary>Shared zero-value instance.</summary>
    public static TokenUsage Zero { get; } = new();
}
