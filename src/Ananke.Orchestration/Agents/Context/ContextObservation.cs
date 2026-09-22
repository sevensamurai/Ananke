using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents.Context;

/// <summary>
/// What one model call was about to send, measured against the window it was being sent to.
/// </summary>
/// <remarks>
/// <para>
/// Recorded <em>before</em> the call, because the question it answers — did the assembled prompt
/// fit? — stops being answerable once the provider has rejected it or silently truncated it.
/// </para>
/// <para>
/// <b>This is deliberately not part of <see cref="Usage.UsageRecord"/>.</b> Usage records what a
/// call <em>consumed</em>, and feeds budget enforcement; this records what a call was <em>offered</em>,
/// and feeds diagnostics. Folding the second into the first would put diagnostic fields on the type a
/// budget is computed from.
/// </para>
/// </remarks>
public sealed record ContextObservation
{
    /// <summary>The model the request was routed to. Empty when the router could not name it.</summary>
    public string ModelName { get; init; } = string.Empty;

    /// <summary>Estimated size of the assembled prompt — system prompt, messages and tool schemas.</summary>
    public required int PromptTokens { get; init; }

    /// <summary>
    /// The selected model's real context window, or <c>0</c> when unknown.
    /// </summary>
    /// <remarks>
    /// This is <c>ModelProfile.ContextTokens</c> — the <em>effective</em> window, which a local
    /// deployment may have narrowed — never the declared maximum. A model served on a smaller window
    /// than its weights allow is measured by what it was actually launched with.
    /// </remarks>
    public int ContextTokens { get; init; }

    /// <summary>
    /// What the pinned contract cost in this assembly, or <c>0</c> when the job has none.
    /// </summary>
    /// <remarks>
    /// Recorded here rather than only on a compaction record because a contract is carried by
    /// <em>every</em> assembly, and most runs never compact at all. Reading it only from compaction
    /// made a run that pinned a contract on all sixteen of its calls and never filled a window
    /// report nothing pinned — which is not a smaller version of the truth, it is the opposite of it.
    /// </remarks>
    public int ContractTokens { get; init; }

    /// <summary>Whether the window was known at all. Everything below is meaningless when it is not.</summary>
    public bool WindowKnown => ContextTokens > 0;

    /// <summary>
    /// Whether the assembled prompt exceeded the real window. Binary and deterministic — the
    /// truncation-incidence signal.
    /// </summary>
    public bool ExceededWindow => WindowKnown && PromptTokens > ContextTokens;

    /// <summary>
    /// Assembled prompt as a fraction of the real window, or <c>null</c> when the window is unknown.
    /// </summary>
    /// <remarks>
    /// Report the <b>tail</b> of this across calls (p95/p99), never the mean: a run averaging 40 %
    /// that spikes past 100 % twice is worse than one sitting flat at 80 %, and the mean hides
    /// exactly the calls that break.
    /// </remarks>
    public double? HeadroomRatio => WindowKnown ? (double)PromptTokens / ContextTokens : null;
}
