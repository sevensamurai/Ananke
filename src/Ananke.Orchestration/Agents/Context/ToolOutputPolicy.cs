namespace Ananke.Orchestration.Agents.Context;

/// <summary>
/// Work-tier hygiene for tool results: how large one may be when it enters the window, and how
/// far an older one may be reduced when the window is under pressure.
/// </summary>
/// <remarks>
/// <b>This is about what the model sees, not what the call costs.</b> An unbounded tool result does
/// not merely make a request expensive — it crowds out the goal, the constraints and the recent
/// exchange, and it keeps doing so on every subsequent round. Shedding it is how the rest stays
/// legible.
/// </remarks>
/// <remarks>
/// <para>
/// Opt-in and off by default. Without a policy an agent behaves exactly as before — which today
/// means <b>no cap at all</b>: a tool returning a four-megabyte file read puts four megabytes into
/// the next request.
/// </para>
/// <para>
/// The two limits do different jobs and that is why there are two. <see cref="MaxResultChars"/>
/// bounds a <em>new</em> result as it arrives, before it is ever sent. <see cref="PrunedResultChars"/>
/// reclaims an <em>older</em> one that is still being carried round after round, long after its
/// conclusion reached the transcript — and it is applied only when there is a budget to compare
/// against, so nothing is discarded speculatively.
/// </para>
/// <para>
/// Sizes are in characters rather than tokens deliberately: a character count is exact, needs no
/// estimator, and is the unit the reduction is actually reported in.
/// </para>
/// </remarks>
public sealed record ToolOutputPolicy
{
    /// <summary>
    /// Largest tool result carried inline. Anything longer is spilled (when
    /// <see cref="SpillStore"/> is set) or truncated.
    /// </summary>
    public int MaxResultChars { get; init; } = 16_000;

    /// <summary>How much of an oversized result's head is kept inline as a preview.</summary>
    public int PreviewChars { get; init; } = 1_000;

    /// <summary>What an older tool result is reduced to when pruning runs.</summary>
    public int PrunedResultChars { get; init; } = 256;

    /// <summary>
    /// How many of the most recent tool results are never pruned. These are the ones the model is
    /// still working from; pruning them is how a run loses the thread.
    /// </summary>
    public int KeepRecentResults { get; init; } = 2;

    /// <summary>
    /// Where oversized output is persisted. When <see langword="null"/> the excess is discarded
    /// instead — the window is still bounded, but the tail is gone.
    /// </summary>
    public IToolOutputSpillStore? SpillStore { get; init; }

    /// <summary>Throws if any limit is incoherent. Called by the builders at configuration time.</summary>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxResultChars, 1, nameof(MaxResultChars));
        ArgumentOutOfRangeException.ThrowIfLessThan(PreviewChars, 0, nameof(PreviewChars));
        ArgumentOutOfRangeException.ThrowIfLessThan(PrunedResultChars, 0, nameof(PrunedResultChars));
        ArgumentOutOfRangeException.ThrowIfLessThan(KeepRecentResults, 0, nameof(KeepRecentResults));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(PreviewChars, MaxResultChars, nameof(PreviewChars));
    }
}
