using Ananke.Abstractions.Tools;
using Ananke.Orchestration.Tools.Gating;

namespace Ananke.Orchestration.Tools.Faults;

/// <summary>
/// In-process <see cref="IToolFaultObserver"/> that translates fault events
/// into <see cref="IToolMemory"/> health changes.
/// </summary>
/// <remarks>
/// <para>
/// Mapping:
/// <list type="bullet">
///   <item><see cref="ToolFaultEvent.ContractBreak"/> = <see langword="true"/> →
///     <see cref="ToolHealth.Offline"/> (permanent for this session).</item>
///   <item><see cref="ToolFaultEvent.Transient"/> = <see langword="true"/> →
///     <see cref="ToolHealth.Cooldown"/> (recovers via <see cref="ToolHealthRecovery"/>).</item>
///   <item>Both <see langword="false"/> (unexpected error, not classified) →
///     <see cref="ToolHealth.Degraded"/>.</item>
/// </list>
/// </para>
/// <para>
/// Thread-safe. All mutations delegate to <see cref="IToolMemory.MarkHealthAsync"/>
/// which is itself thread-safe in every shipped implementation.
/// </para>
/// <para>
/// Wire up alongside <see cref="ToolHealthRecovery"/> for automatic Cooldown → Healthy recovery:
/// </para>
/// <code>
/// var observer = new InMemoryToolFaultObserver(memory);
/// var recovery = new ToolHealthRecovery(memory, checkInterval: TimeSpan.FromMinutes(1));
/// </code>
/// </remarks>
public sealed class InMemoryToolFaultObserver : IToolFaultObserver
{
    private readonly IToolMemory _memory;

    /// <summary>Creates a fault observer that writes health changes into <paramref name="memory"/>.</summary>
    public InMemoryToolFaultObserver(IToolMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        _memory = memory;
    }

    /// <inheritdoc />
    public ValueTask ReportAsync(ToolFaultEvent fault, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fault);

        var health = fault.ContractBreak
            ? ToolHealth.Offline
            : fault.Transient
                ? ToolHealth.Cooldown
                : ToolHealth.Degraded;

        ToolMetrics.FaultReported.Add(1,
            new KeyValuePair<string, object?>("kit", fault.KitName),
            new KeyValuePair<string, object?>("tool", fault.ToolName),
            new KeyValuePair<string, object?>("contract_break", fault.ContractBreak));

        return new ValueTask(_memory.MarkHealthAsync(fault.KitName, fault.ToolName, health, ct));
    }
}
