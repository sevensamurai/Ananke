namespace Ananke.TestHelpers;

/// <summary>
/// Canonical test-workload delegates used in place of raw <c>Task.Delay</c> in test bodies.
/// </summary>
/// <remarks>
/// Category-C rule (see design/20260426-test-suite-deflake-task-delay.md §2):
/// "park forever" loops are the only <c>Task.Delay</c> usage permitted in tests,
/// and must go through this class so they are greppable and never confused with
/// synchronization-by-sleep (category A/B).
/// </remarks>
public static class WorkflowLoops
{
    /// <summary>
    /// Parks the calling workflow loop until its <see cref="CancellationToken"/> is cancelled.
    /// Use as the loop body wherever a cell just needs to be alive with no real work.
    /// </summary>
    /// <example>
    /// <code>
    /// await host.StartAsync("cell", WorkflowLoops.Park);
    /// </code>
    /// </example>
    public static Task Park(CancellationToken ct) => Task.Delay(Timeout.Infinite, ct);

    /// <summary>
    /// Spins in a tight <see cref="Task.Yield"/> loop until <paramref name="ct"/> is cancelled.
    /// Use inside test workloads that need to keep iterating without real delay —
    /// replaces <c>while (!ct) await Task.Delay(N, ct)</c> patterns.
    /// </summary>
    public static async Task Spin(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
            await Task.Yield();
    }
}
