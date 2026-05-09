using Ananke.Organics.Healing;

namespace Ananke.Organics.Division;

/// <summary>
/// Provides the names of workflow cells running on remote platforms.
/// Used by the organic host to poll remote cells for complexity
/// evaluation on a timer, since remote executions don't flow through
/// the local observation channel.
/// </summary>
/// <remarks>
/// <para>
/// Remote cells are opaque — their executions happen on the platform.
/// The supervisor can still evaluate division by polling
/// <see cref="IHealthMonitor.GetSnapshotAsync"/> for each remote cell name
/// returned here. The monitor computes structural metrics from the manifest
/// and enriches with platform health telemetry (latency, token usage) when
/// available. These are <b>health</b> signals, not surface tension —
/// structural tension for remote cells is computed from the manifest alone.
/// </para>
/// </remarks>
public interface IRemoteCellSource
{
    /// <summary>
    /// Returns the names of all currently active remote cells that should
    /// be periodically evaluated for division.
    /// </summary>
    Task<IReadOnlyList<string>> GetRemoteCellNamesAsync(CancellationToken ct = default);
}
