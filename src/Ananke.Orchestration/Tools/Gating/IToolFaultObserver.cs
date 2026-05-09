using Ananke.Abstractions.Tools;

namespace Ananke.Orchestration.Tools.Gating;

/// <summary>
/// Carries fault metadata emitted by the tool execution pipeline when a tool invocation fails.
/// </summary>
/// <param name="KitName">Name of the <see cref="ToolKit"/> that owns the tool.</param>
/// <param name="ToolName">Name of the failing tool.</param>
/// <param name="Reason">Human-readable failure description.</param>
/// <param name="ContractBreak">
/// <see langword="true"/> when the failure is a schema/contract violation
/// (e.g. the tool returned an invalid shape). Maps to <see cref="ToolHealth.Offline"/>.
/// </param>
/// <param name="Transient">
/// <see langword="true"/> when the failure may succeed on retry (network error, timeout).
/// Maps to <see cref="ToolHealth.Cooldown"/>.
/// </param>
public sealed record ToolFaultEvent(
    string KitName,
    string ToolName,
    string Reason,
    bool ContractBreak,
    bool Transient);

/// <summary>
/// Receives <see cref="ToolFaultEvent"/> notifications from the tool execution pipeline
/// and propagates health changes into <see cref="IToolMemory"/>.
/// </summary>
/// <remarks>
/// <para>
/// The default implementation is <c>InMemoryToolFaultObserver</c>.
/// Implement this interface to route fault events to external systems (metrics, alerting).
/// </para>
/// <para>
/// Corresponds to the nociceptor bus role in the tool fault pipeline.
/// </para>
/// </remarks>
public interface IToolFaultObserver
{
    /// <summary>Reports a tool fault event and updates the tool's health state accordingly.</summary>
    ValueTask ReportAsync(ToolFaultEvent fault, CancellationToken ct = default);
}
