using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Ananke.Organics.Sensing;

/// <summary>
/// In-memory <see cref="ICapabilityMap"/> for single-process colonies.
/// Signals are stored in a <see cref="ConcurrentDictionary{TKey, TValue}"/>
/// keyed by cell name. Liveness is determined by comparing the signal timestamp
/// to a configurable timeout.
/// </summary>
public sealed class InMemoryCapabilityMap : ICapabilityMap
{
    private readonly ConcurrentDictionary<string, WorkflowSignal> _signals = new();
    private readonly TimeSpan _signalTimeout;

    // Per-cell registration signal channels — buffered so signals written before
    // WhenRegisteredAsync is called are never lost.
    private readonly ConcurrentDictionary<string, Channel<WorkflowSignal>> _registerSignals = new();

    /// <summary>
    /// Creates a new landscape with the specified signal timeout.
    /// Cells that have not signaled within this window are considered dead.
    /// </summary>
    /// <param name="signalTimeout">
    /// Maximum age of a signal before the cell is considered dead.
    /// Defaults to 30 seconds.
    /// </param>
    public InMemoryCapabilityMap(TimeSpan? signalTimeout = null)
    {
        _signalTimeout = signalTimeout ?? TimeSpan.FromSeconds(30);
    }

    /// <inheritdoc />
    public void Register(WorkflowSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        _signals[signal.WorkflowName] = signal;

        var ch = _registerSignals.GetOrAdd(signal.WorkflowName,
            _ => Channel.CreateUnbounded<WorkflowSignal>());
        ch.Writer.TryWrite(signal);
    }

    // ── Internal observation hook ────────────────────────────────────────────
    // Exposed to Ananke.Organics.Tests via the project-level InternalsVisibleTo.

    /// <summary>
    /// Returns a <see cref="Task"/> that completes the next time
    /// <see cref="Register"/> is called for <paramref name="cellName"/>.
    /// Never loses a signal: if <see cref="Register"/> was already called
    /// the task completes immediately on the next await.
    /// Always pair with <c>WaitAsync(TimeSpan.FromSeconds(5))</c>.
    /// </summary>
    internal Task WhenRegisteredAsync(string cellName)
    {
        var ch = _registerSignals.GetOrAdd(cellName,
            _ => Channel.CreateUnbounded<WorkflowSignal>());
        return ch.Reader.ReadAsync().AsTask();
    }

    /// <inheritdoc />
    public IReadOnlyList<SensedCapability> Discover(string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        var now = DateTimeOffset.UtcNow;

        return _signals.Values
            .Where(s => s.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase)
                        && (now - s.Timestamp) <= _signalTimeout)
            .Select(s => ToCapability(s, alive: true))
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<SensedCapability> DiscoverAll()
    {
        var now = DateTimeOffset.UtcNow;

        return _signals.Values
            .Where(s => (now - s.Timestamp) <= _signalTimeout)
            .Select(s => ToCapability(s, alive: true))
            .ToList();
    }

    /// <inheritdoc />
    public void Remove(string workflowName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);
        _signals.TryRemove(workflowName, out _);
    }

    private static SensedCapability ToCapability(WorkflowSignal signal, bool alive) => new()
    {
        WorkflowName = signal.WorkflowName,
        Domain = signal.Domain,
        Capabilities = signal.Capabilities,
        LastSensed = signal.Timestamp,
        Alive = alive
    };
}
