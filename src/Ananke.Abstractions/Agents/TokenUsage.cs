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
    /// Returns a new <see cref="TokenUsage"/> that is the sum of this and <paramref name="other"/>.
    /// </summary>
    public TokenUsage Add(TokenUsage other) => new()
    {
        InputTokens = InputTokens + other.InputTokens,
        OutputTokens = OutputTokens + other.OutputTokens
    };

    /// <summary>Shared zero-value instance.</summary>
    public static TokenUsage Zero { get; } = new();
}
