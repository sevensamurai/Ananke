using System.Diagnostics.Metrics;
using Ananke.Abstractions;

namespace Ananke.Orchestration.Tools;

/// <summary>
/// <see cref="System.Diagnostics.Metrics"/> counters for the tool gate and health pipeline.
/// Meter name: <c>Ananke.Orchestration.Tools</c> (see <see cref="AnankeSourceNames.OrchestrationTools"/>).
/// </summary>
/// <remarks>
/// Register the meter name with your OpenTelemetry <c>MeterProvider</c> to collect
/// these metrics via any OTEL exporter:
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithMetrics(b => b.AddMeter(AnankeSourceNames.OrchestrationTools));
/// // or, using the Ananke.OpenTelemetry package:
/// //     .WithMetrics(b => b.AddMeter(Sources.OrchestrationTools));
/// </code>
/// </remarks>
internal static class ToolMetrics
{
    private static readonly Meter Meter = new(AnankeSourceNames.OrchestrationTools, "1.0.0");

    /// <summary>
    /// Incremented by the number of tools returned by the semantic gate per turn.
    /// Tags: <c>kit</c>.
    /// </summary>
    internal static readonly Counter<long> RecallHit =
        Meter.CreateCounter<long>(
            "tool_gate.recall_hit",
            description: "Number of tools returned by the gate per agent turn.");

    /// <summary>
    /// Incremented each time a <see cref="Gating.ToolFaultEvent"/> is reported.
    /// Tags: <c>kit</c>, <c>tool</c>, <c>contract_break</c>.
    /// </summary>
    internal static readonly Counter<long> FaultReported =
        Meter.CreateCounter<long>(
            "tool.fault_reported",
            description: "Number of tool fault events reported via IToolFaultObserver.");

    /// <summary>
    /// Incremented each time a tool entry is removed from <c>IToolMemory</c> by <see cref="Faults.ToolPruner"/>.
    /// Tags: <c>kit</c>, <c>tool</c>.
    /// </summary>
    internal static readonly Counter<long> Pruned =
        Meter.CreateCounter<long>(
            "tool.pruned",
            description: "Number of tools removed from IToolMemory by ToolPruner.");
}
