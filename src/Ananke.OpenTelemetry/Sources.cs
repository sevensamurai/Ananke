using Ananke.Abstractions;

namespace Ananke.OpenTelemetry;

/// <summary>
/// Single source of truth for all <see cref="System.Diagnostics.ActivitySource"/> and
/// <see cref="System.Diagnostics.Metrics.Meter"/> names used across Ananke packages.
/// </summary>
/// <remarks>
/// <para>
/// Pass source names to <c>builder.AddSource(…)</c> and meter names to
/// <c>builder.AddMeter(…)</c> in your OpenTelemetry setup:
/// <code>
/// services.AddOpenTelemetry()
///     .WithTracing(b => b
///         .AddSource(Sources.Orchestration)
///         .AddSource(Sources.StateMachine))
///     .WithMetrics(b => b
///         .AddMeter(Sources.OrchestrationTools)
///         .AddMeter(Sources.EmpiricalMemory));
/// </code>
/// </para>
/// <para>
/// Package producers use <see cref="AnankeSourceNames"/> (in <c>Ananke.Abstractions</c>)
/// to avoid pulling the full OpenTelemetry setup package into every library.
/// These constants forward those values for consumer convenience.
/// </para>
/// </remarks>
public static class Sources
{
    // ── Activity sources ───────────────────────────────────────────────────

    /// <summary>Activity source for <c>Ananke.Orchestration</c> workflow spans.</summary>
    public const string Orchestration = AnankeSourceNames.Orchestration;

    /// <summary>Activity source for <c>Ananke.StateMachine</c> transition spans.</summary>
    public const string StateMachine = AnankeSourceNames.StateMachine;

    /// <summary>
    /// Activity source for empirical memory operations (commit, recall, reinforce, contradict).
    /// Emitted by <c>InMemoryEmpiricalMemory</c> and <c>QdrantEmpiricalMemory</c>.
    /// </summary>
    public const string EmpiricalMemory = AnankeSourceNames.EmpiricalMemory;

    // ── Meter names ────────────────────────────────────────────────────────

    /// <summary>
    /// Meter for <c>Ananke.Orchestration</c> tool-gate metrics
    /// (<c>tool_gate.recall_hit</c>, <c>tool.fault_reported</c>, <c>tool.pruned</c>).
    /// Use with <c>builder.AddMeter(Sources.OrchestrationTools)</c>.
    /// </summary>
    public const string OrchestrationTools = AnankeSourceNames.OrchestrationTools;

    /// <summary>
    /// Meter for empirical memory counters
    /// (<c>empirical.commits</c>, <c>empirical.recalls</c>, <c>empirical.recall_hits</c>,
    /// <c>empirical.reinforcements</c>, <c>empirical.contradictions</c>,
    /// <c>empirical.dedup_merges</c>).
    /// Shared by <c>InMemoryEmpiricalMemory</c> and <c>QdrantEmpiricalMemory</c>.
    /// Use with <c>builder.AddMeter(Sources.EmpiricalMemoryMeter)</c>.
    /// </summary>
    public const string EmpiricalMemoryMeter = AnankeSourceNames.EmpiricalMemoryMeter;

    /// <summary>
    /// Meter for <c>Ananke.Federation</c> remote cell metrics
    /// (tokens/exec, tool-calls/exec, error rate per deployment).
    /// Use with <c>builder.AddMeter(Sources.Federation)</c>.
    /// </summary>
    public const string Federation = AnankeSourceNames.Federation;
}
