namespace Ananke.Orchestration.Agents.Context;

/// <summary>
/// What one assembly may spend, and the real capacity it must not exceed.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the bare token count a strategy used to be constructed with. A budget fixed at
/// construction cannot be right for a router with heterogeneous candidates, because model selection
/// happens per request: the same strategy instance may be asked to fit a 4 k local model on one call
/// and a 200 k hosted one on the next.
/// </para>
/// <para>
/// <b>Two numbers pointing in opposite directions.</b> <see cref="MaxTokens"/> is an *allocation* —
/// what the caller has decided this assembly may use. <see cref="ModelContextTokens"/> is a *cap* —
/// what the selected model will actually accept. The allocation never raises the cap, and the cap
/// never raises the allocation.
/// </para>
/// <para>
/// <b>Nothing here guesses.</b> An unspecified allocation stays unspecified, and a strategy falls
/// back to whatever it was configured with — it is not silently replaced by the model's window,
/// because a caller that asked for a small budget on a large model usually meant it.
/// </para>
/// </remarks>
public sealed record ContextBudget
{
    /// <summary>No allocation and no known capacity: the strategy's own configuration applies.</summary>
    public static ContextBudget Unspecified { get; } = new();

    /// <summary>
    /// Tokens this assembly may spend, or <c>null</c> when the caller has not decided.
    /// </summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// The selected model's real context window, or <c>0</c> when unknown.
    /// </summary>
    /// <remarks>
    /// The <em>effective</em> window — a locally served model is measured by what its server was
    /// launched with, not by what its weights allow.
    /// </remarks>
    public int ModelContextTokens { get; init; }

    /// <summary>The selected model's name, for reporting. Empty when unknown.</summary>
    public string ModelName { get; init; } = string.Empty;

    /// <summary>
    /// Tokens already committed before a strategy gets to choose anything — today, the tool schemas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A strategy is handed the messages and the system prompt, so those are the only things it can
    /// measure. Tool definitions are sent on every call and are frequently the largest single
    /// contributor, and they were reaching the model without ever reaching the strategy. A strategy
    /// aimed at exactly the window therefore overflowed it by the size of the tool schemas — while
    /// reporting that it had compacted successfully.
    /// </para>
    /// <para>
    /// Kept as a number rather than by widening the seam to pass tools through: a strategy has no use
    /// for the schemas themselves, only for how much room they leave.
    /// </para>
    /// </remarks>
    public int ReservedTokens { get; init; }

    /// <summary>Whether the caller supplied an allocation.</summary>
    public bool HasAllocation => MaxTokens is > 0;

    /// <summary>Whether the model's real window is known.</summary>
    public bool HasCapacity => ModelContextTokens > 0;

    /// <summary>
    /// Resolves the budget a strategy should enforce: the caller's allocation when there is one,
    /// otherwise <paramref name="configured"/> — in both cases capped by the real window.
    /// </summary>
    /// <remarks>
    /// The cap is the half that fixes a real defect: a strategy configured for a large window,
    /// composed with a router that may pick a small local model, previously overflowed it silently.
    /// Raising a small configured budget to fill a large window is deliberately <b>not</b> done here
    /// — that is an allocation decision, and it needs something that knows what the work is for.
    /// </remarks>
    public int Resolve(int configured)
    {
        var chosen = MaxTokens is > 0 ? MaxTokens.Value : configured;
        var capped = HasCapacity ? Math.Min(chosen, ModelContextTokens) : chosen;

        // What is already spoken for is not available to spend. Never below zero: tool schemas
        // larger than the whole window are a routing problem, and returning a negative budget would
        // turn it into an arithmetic one somewhere further away.
        return Math.Max(0, capped - ReservedTokens);
    }
}
