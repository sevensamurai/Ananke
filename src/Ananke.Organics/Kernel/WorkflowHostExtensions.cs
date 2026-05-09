using Ananke.Organics.Kernel.Snapshots;
using Ananke.Organics.Sensing;

namespace Ananke.Organics.Kernel;

/// <summary>
/// Extension methods for <see cref="IWorkflowHost"/> that reduce boilerplate when
/// spawning cells with standard heartbeat loops.
/// </summary>
public static class WorkflowHostExtensions
{
    private static readonly TimeSpan DefaultHeartbeat = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Spawns a cell into the kernel with an automatic heartbeat loop that
    /// continuously emits <see cref="WorkflowSignal"/>s into the landscape.
    /// The cell advertises its tools, domain, and lineage every
    /// <paramref name="heartbeatInterval"/> until killed.
    /// </summary>
    /// <param name="host">the kernel to spawn into.</param>
    /// <param name="cell">
    /// Cell snapshot whose <see cref="WorkflowSnapshot.Name"/>,
    /// <see cref="WorkflowSnapshot.Domain"/>, <see cref="WorkflowSnapshot.Tools"/>,
    /// and <see cref="WorkflowSnapshot.SplitFrom"/> are used for the signal.
    /// </param>
    /// <param name="landscape">Capability landscape that receives heartbeats.</param>
    /// <param name="heartbeatInterval">
    /// Interval between heartbeat signals. Defaults to 200 ms.
    /// </param>
    /// <param name="bootstrapDelay">
    /// Optional delay before the first heartbeat — simulates model loading,
    /// memory seeding, or other startup work.
    /// </param>
    /// <param name="timeProvider">
    /// Time abstraction used for the bootstrap delay and heartbeat interval.
    /// Defaults to <see cref="TimeProvider.System"/>. Pass a
    /// <c>FakeTimeProvider</c> in tests to control time deterministically.
    /// </param>
    public static Task StartWithHealthCheckAsync(
        this IWorkflowHost host,
        WorkflowSnapshot cell,
        ICapabilityMap landscape,
        TimeSpan? heartbeatInterval = null,
        TimeSpan? bootstrapDelay = null,
        TimeProvider? timeProvider = null)
        => StartWithHealthCheckAsync(host, cell, landscape, heartbeatInterval, bootstrapDelay, timeProvider, null);

    /// <summary>
    /// Internal overload that accepts an optional <paramref name="onDelaying"/> callback,
    /// fired immediately before the first timer-based delay starts.
    /// This allows tests to synchronize precisely when the cell enters its first
    /// <see cref="Delay"/> call without relying on wall-clock delays.
    /// </summary>
    internal static Task StartWithHealthCheckAsync(
        this IWorkflowHost host,
        WorkflowSnapshot cell,
        ICapabilityMap landscape,
        TimeSpan? heartbeatInterval,
        TimeSpan? bootstrapDelay,
        TimeProvider? timeProvider,
        Action? onDelaying)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(landscape);

        var interval = heartbeatInterval ?? DefaultHeartbeat;
        var clock = timeProvider ?? TimeProvider.System;

        return host.StartAsync(cell.Name, async ct =>
        {
            if (bootstrapDelay is { } delay)
                await Delay(clock, delay, ct, onDelaying);

            while (!ct.IsCancellationRequested)
            {
                landscape.Register(new WorkflowSignal
                {
                    WorkflowName = cell.Name,
                    Domain = cell.Domain,
                    Capabilities = cell.Tools.ToList(),
                    Timestamp = clock.GetUtcNow(),
                    SplitFrom = cell.SplitFrom
                });

                await Delay(clock, interval, ct);
            }
        });
    }

    private static async Task Delay(TimeProvider timeProvider, TimeSpan delay, CancellationToken ct,
        Action? onTimerRegistered = null)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = ct.Register(static s => ((TaskCompletionSource)s!).TrySetCanceled(), tcs);
        using var timer = timeProvider.CreateTimer(
            static s => ((TaskCompletionSource)s!).TrySetResult(),
            tcs, delay, Timeout.InfiniteTimeSpan);
        onTimerRegistered?.Invoke(); // fires after CreateTimer — safe for test to Advance now
        await tcs.Task.ConfigureAwait(false);
    }
}
