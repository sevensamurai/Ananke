namespace Ananke.Organics.Sensing;

/// <summary>
/// the kernel's sensory model — aggregates cell signals into a live capability
/// landscape. Not a registry: entries are derived from signals and decay when
/// signals stop.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the registry pattern (explicit Register/Unregister) with a
/// signal-based model that is self-healing: if a cell crashes without cleanup,
/// its signals simply stop and it fades from awareness after a timeout.
/// </para>
/// <para>
/// Built-in implementation: <see cref="InMemoryCapabilityMap"/>.
/// </para>
/// </remarks>
public interface ICapabilityMap
{
    /// <summary>
    /// Process an incoming cell signal. Updates the landscape with the cell's
    /// current capabilities and resets its liveness timer.
    /// </summary>
    void Register(WorkflowSignal signal);

    /// <summary>
    /// Sense which cells can handle a given domain. Returns only alive cells
    /// (those whose last signal is within the timeout window).
    /// </summary>
    IReadOnlyList<SensedCapability> Discover(string domain);

    /// <summary>
    /// Sense all currently alive capabilities. Use sparingly — the kernel
    /// shouldn't need to enumerate all cells, but diagnostics may.
    /// </summary>
    IReadOnlyList<SensedCapability> DiscoverAll();

    /// <summary>
    /// Mark a cell as dead. Immediate removal — used during division
    /// (parent is killed) rather than waiting for signal timeout.
    /// </summary>
    void Remove(string workflowName);
}
