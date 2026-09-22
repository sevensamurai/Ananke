namespace Ananke.Orchestration.Planning;

/// <summary>
/// How many times the loop re-issues an attempt that produced nothing to evaluate.
/// </summary>
/// <remarks>
/// <para>
/// <b>One budget for both mechanical retries, because they are the same class of thing (R29).</b> A
/// reply that will not parse and a provider that fell over both mean <em>the executor produced no
/// response</em> — nothing was applied, nothing was built, no criterion was measured, and so there is
/// no outcome yet for anybody to deviate from. Neither is a judgement, so both may live in the loop;
/// a retry that could conclude a failure was unimportant could not.
/// </para>
/// <para>
/// <b>Nothing here waits, deliberately.</b> Backoff belongs to the provider layer, which knows what
/// the provider said: <c>ResilientAgentModel</c> reads <c>Retry-After</c> and falls back to
/// exponential with jitter. A second delay here would wait twice for one busy server, and would do it
/// with a number this tier made up.
/// </para>
/// <para>
/// <b>And nothing here is readable from the plan.</b> The count bounds the loop and never reaches the
/// tree: a plan that could see how many goes a step has had is a plan whose planner can reason about
/// it, and attempt counts are exactly the kind of infrastructure detail that starts steering
/// decisions it has no business steering (S9).
/// </para>
/// </remarks>
public sealed record PlanRetryPolicy
{
    /// <summary>
    /// Total attempts at one node in one pass, including the first. <c>1</c> disables retrying.
    /// </summary>
    /// <remarks>
    /// Three: enough to ride out a restarting provider or a model that garbled one reply, few enough
    /// that a genuinely broken executor is reported rather than hammered.
    /// </remarks>
    public int Attempts { get; init; } = 3;

    /// <summary>The default: three attempts.</summary>
    public static PlanRetryPolicy Default { get; } = new();

    /// <summary>No retrying at all — the first attempt is the only one.</summary>
    public static PlanRetryPolicy Once { get; } = new() { Attempts = 1 };
}
