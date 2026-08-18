using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Usage;

/// <summary>
/// What a single model call consumed. Callers report this and stop caring —
/// buffering, aggregation and persistence are the recorder's business.
/// </summary>
/// <param name="Usage">Tokens consumed by the call.</param>
/// <param name="ModelCost">
/// Per-call cost when model-specific rates are available (a cost-resolving router).
/// <c>null</c> when only token counts are known.
/// </param>
public readonly record struct UsageRecord(TokenUsage Usage, decimal? ModelCost = null);

/// <summary>
/// An immutable point-in-time total. Never a live reference into recorder state —
/// a caller that holds one cannot observe or cause later mutation.
/// </summary>
public sealed record UsageSnapshot
{
    /// <summary>An empty total.</summary>
    public static UsageSnapshot Empty { get; } = new();

    /// <summary>Tokens accumulated so far.</summary>
    public TokenUsage Usage { get; init; } = TokenUsage.Zero;

    /// <summary>
    /// Cost accumulated from model-specific rates. Meaningful only when
    /// <see cref="HasModelBasedCost"/> is <c>true</c>.
    /// </summary>
    public decimal AccumulatedCost { get; init; }

    /// <summary>
    /// Whether any per-call model-based cost was reported. Informational: a budget prices
    /// <see cref="UncostedUsage"/> at flat rates regardless, so this no longer selects between
    /// two whole-total strategies.
    /// </summary>
    public bool HasModelBasedCost { get; init; }

    /// <summary>
    /// The portion of <see cref="Usage"/> that arrived with no per-call cost, and therefore has
    /// to be priced at flat rates.
    /// </summary>
    /// <remarks>
    /// Tracked separately because a workflow can mix jobs on a cost-resolving router with jobs
    /// on a plain model. Treating cost as all-or-nothing made every plain-model job in such a
    /// workflow spend invisibly once any routed job had reported a cost.
    /// </remarks>
    public TokenUsage UncostedUsage { get; init; } = TokenUsage.Zero;

    /// <summary>
    /// The delta between this snapshot and an earlier <paramref name="baseline"/>.
    /// </summary>
    /// <remarks>
    /// Lets one recorder answer for several nested runs: a sub-workflow inherits its parent's
    /// recorder, so its own total is what accrued after it started. Without this a child would
    /// report the parent's spend as its own.
    /// </remarks>
    public UsageSnapshot Since(UsageSnapshot baseline) => new()
    {
        Usage = new TokenUsage
        {
            InputTokens = Usage.InputTokens - baseline.Usage.InputTokens,
            OutputTokens = Usage.OutputTokens - baseline.Usage.OutputTokens
        },
        UncostedUsage = new TokenUsage
        {
            InputTokens = UncostedUsage.InputTokens - baseline.UncostedUsage.InputTokens,
            OutputTokens = UncostedUsage.OutputTokens - baseline.UncostedUsage.OutputTokens
        },
        AccumulatedCost = AccumulatedCost - baseline.AccumulatedCost,
        // Whether *this* span saw model-based cost, not whether the recorder ever did.
        HasModelBasedCost = AccumulatedCost > baseline.AccumulatedCost
    };
}

/// <summary>
/// Records what model calls consume. Implementations own their own state: no caller
/// holds, assigns, or reaches through an accumulator.
/// </summary>
/// <remarks>
/// Replaces the earlier <c>TokenUsageCapture</c>, which exposed a mutable accumulator
/// through an ambient reference that callers assigned. Correctness then depended on every
/// caller preserving that reference's identity — an unstated contract, and one that fork
/// branches and sub-workflows both broke. See ADR-arch-028 Part B.
/// <para>
/// Implementations must be safe for concurrent callers: fork branches record in parallel.
/// </para>
/// </remarks>
public interface IUsageRecorder
{
    /// <summary>Records one model call's consumption.</summary>
    Task RecordUsageAsync(UsageRecord record, CancellationToken ct = default);

    /// <summary>Returns the current total as an immutable snapshot.</summary>
    Task<UsageSnapshot> ReadAsync(CancellationToken ct = default);

    /// <summary>Clears the total, beginning a new cycle.</summary>
    Task ResetAsync(CancellationToken ct = default);
}
