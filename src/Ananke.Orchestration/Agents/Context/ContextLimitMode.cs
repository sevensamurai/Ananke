namespace Ananke.Orchestration.Agents.Context;

/// <summary>
/// Controls when an agent job's context-token limit is measured, relative to the
/// <see cref="IContextStrategy"/> that compacts the message list before each model call.
/// </summary>
/// <remarks>
/// <para>
/// The two modes only differ when an <see cref="IContextStrategy"/> is configured — with no
/// strategy there is nothing to compact and both measure the same message list.
/// </para>
/// </remarks>
public enum ContextLimitMode
{
    /// <summary>
    /// Measure the message list <b>after</b> the context strategy has compacted it — that is,
    /// measure what will actually be sent to the model. The limit is only breached when
    /// compaction could not bring the payload under it. This is the default: configuring a
    /// compaction strategy and then failing on the pre-compaction size would defeat the point
    /// of having the strategy.
    /// </summary>
    PostCompaction = 0,

    /// <summary>
    /// Measure the raw accumulated message list <b>before</b> compaction, and fail without
    /// invoking the strategy at all. Use this to treat the limit as a hard ceiling on how much
    /// history a run may accumulate, independent of how well compaction hides it — for example
    /// when the strategy is expensive (a summarising strategy issues its own model call) and
    /// running it on an already-oversized conversation is not worth the cost.
    /// </summary>
    PreCompaction = 1
}
