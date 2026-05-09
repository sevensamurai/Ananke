namespace Ananke.Abstractions;

/// <summary>
/// Well-known <see cref="System.Diagnostics.ActivitySource"/> and
/// <see cref="System.Diagnostics.Metrics.Meter"/> name constants used by Ananke packages
/// when constructing their instrumentation instances.
/// </summary>
/// <remarks>
/// <para>
/// Each Ananke package that emits traces or metrics creates its <c>ActivitySource</c>
/// and <c>Meter</c> using these constants as the name argument. Keeping the names here
/// (in the zero-dependency <c>Ananke.Abstractions</c> assembly) means every package can
/// reference them without pulling in the full <c>Ananke.OpenTelemetry</c> setup package.
/// </para>
/// <para>
/// <c>Ananke.OpenTelemetry.Sources</c> re-exposes these values as consumer-facing
/// constants for use in OTEL registration code (e.g. <c>builder.AddSource(…)</c>).
/// </para>
/// </remarks>
public static class AnankeSourceNames
{
    // ── Activity sources ───────────────────────────────────────────────────

    /// <summary><c>ActivitySource</c> name for <c>Ananke.Orchestration</c> workflow spans.</summary>
    public const string Orchestration = "Ananke.Orchestration";

    /// <summary><c>ActivitySource</c> name for <c>Ananke.StateMachine</c> transition spans.</summary>
    public const string StateMachine = "Ananke.StateMachine";

    /// <summary>
    /// <c>ActivitySource</c> name for empirical memory operations (commit, recall, reinforce,
    /// contradict). Emitted by <c>InMemoryEmpiricalMemory</c> and <c>QdrantEmpiricalMemory</c>.
    /// </summary>
    public const string EmpiricalMemory = "Ananke.EmpiricalMemory";

    // ── Meter names ────────────────────────────────────────────────────────

    /// <summary>
    /// <c>Meter</c> name for <c>Ananke.Orchestration</c> tool-gate metrics
    /// (<c>tool_gate.recall_hit</c>, <c>tool.fault_reported</c>, <c>tool.pruned</c>).
    /// </summary>
    public const string OrchestrationTools = "Ananke.Orchestration.Tools";

    /// <summary>
    /// <c>Meter</c> name for empirical memory counters
    /// (<c>empirical.commits</c>, <c>empirical.recalls</c>, <c>empirical.recall_hits</c>,
    /// <c>empirical.reinforcements</c>, <c>empirical.contradictions</c>,
    /// <c>empirical.dedup_merges</c>).
    /// Shared by <c>InMemoryEmpiricalMemory</c> and <c>QdrantEmpiricalMemory</c>.
    /// </summary>
    public const string EmpiricalMemoryMeter = "Ananke.EmpiricalMemory";

    /// <summary>
    /// <c>Meter</c> name for <c>Ananke.Federation</c> remote cell metrics
    /// (tokens/exec, tool-calls/exec, error rate per deployment).
    /// </summary>
    public const string Federation = "Ananke.Federation";
}
