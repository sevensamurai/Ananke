using Ananke.Organics.Division;

namespace Ananke.Organics.Healing;

/// <summary>
/// Classifies why a workflow execution failed or underperformed. Used by
/// <see cref="IHealthMonitor"/> to distinguish upstream transient errors,
/// genuine workflow degradation, and capability mismatches — enabling
/// <see cref="IHealingPolicy"/> to choose the correct response.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three failure lanes:</b>
/// </para>
/// <list type="bullet">
///   <item><b>Upstream</b> — the API/model is broken. Wait, don't heal.</item>
///   <item><b>Workflow</b> — the workflow logic is broken. Heal (rollback/restart).</item>
///   <item><b>Capability mismatch</b> — the cell received work it can't serve.
///         Don't heal — reroute. This feeds back into the routing layer.</item>
/// </list>
/// <para>
/// Classification is best-effort based on exception type, execution state,
/// and response content heuristics. Unknown errors default to
/// <see cref="Unknown"/> which is treated as potentially transient.
/// </para>
/// </remarks>
public enum FailureOrigin
{
    /// <summary>The execution succeeded — not a failure.</summary>
    None,

    /// <summary>
    /// The failure originated from an external dependency (API timeout,
    /// HTTP 429/503, network error, model refusal). These are transient —
    /// the workflow itself is healthy, the upstream is not.
    /// </summary>
    Upstream,

    /// <summary>
    /// The failure originated from the workflow's own logic (unhandled
    /// exception in a code job, state mapping error, missing tool).
    /// The workflow itself is broken.
    /// </summary>
    Workflow,

    /// <summary>
    /// Infrastructure-level failure: cancellation, budget exceeded, OOM.
    /// Not a workflow problem and not an upstream problem — operational.
    /// </summary>
    Infrastructure,

    /// <summary>
    /// The LLM/agent completed without error but could not meaningfully
    /// serve the request. The tools available don't fit the prompt, the
    /// agent doesn't understand the domain, or the response is a deflection
    /// ("I don't know how to help with that").
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is NOT a health problem — the cell is healthy but mismatched.
    /// The correct response is rerouting (move traffic to a better-fit cell),
    /// not healing (rollback/restart won't help). Feeds into
    /// <c>RoutingAffinityTracker</c> as a negative affinity signal.
    /// </para>
    /// <para>
    /// Detection requires inspecting the agent's response content for
    /// deflection patterns. See <see cref="FailureClassifier"/> for the
    /// heuristic.
    /// </para>
    /// </remarks>
    CapabilityMismatch,

    /// <summary>
    /// Cannot determine the origin. Treated as potentially transient by
    /// default — counted toward error rate but with lower weight.
    /// </summary>
    Unknown
}
