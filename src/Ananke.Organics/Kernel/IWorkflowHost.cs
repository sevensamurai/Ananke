namespace Ananke.Organics.Kernel;

/// <summary>
/// Manages the lifecycle of workflow cells. the kernel outlives any individual
/// workflow — it is the organism, workflows are cells. Cells are mortal.
/// </summary>
/// <remarks>
/// <para>
/// The interface is deliberately minimal and hosting-agnostic. What "spawn" and
/// "kill" mean depends on the hosting model:
/// </para>
/// <list type="bullet">
///   <item>In-process: <c>Task.Run(loop)</c> / cancel <c>CancellationTokenSource</c></item>
///   <item>Docker Compose: <c>docker run</c> / <c>docker stop</c> via Docker API</item>
///   <item>Kubernetes: create/delete a <c>WorkflowCell</c> CRD</item>
///   <item>Bare metal: <c>Process.Start</c> / <c>Process.Kill</c></item>
/// </list>
/// <para>
/// Built-in implementation: <see cref="InProcessWorkflowHost"/> (for dev, demos, tests).
/// Production hosting adapters are external implementations.
/// </para>
/// </remarks>
public interface IWorkflowHost : IAsyncDisposable
{
    /// <summary>Spawn a new workflow as a running cell.</summary>
    /// <param name="name">Unique name for this cell.</param>
    /// <param name="workflowLoop">
    /// The cell's main loop. Receives a <see cref="CancellationToken"/> that is
    /// cancelled when the kernel kills the cell.
    /// </param>
    /// <param name="ct">Cancellation token for the spawn operation itself (not the loop).</param>
    Task StartAsync(string name, Func<CancellationToken, Task> workflowLoop, CancellationToken ct = default);

    /// <summary>
    /// Kill a running workflow. Cancels its token and awaits clean shutdown.
    /// No-op if the cell is not alive.
    /// </summary>
    /// <param name="name">Name of the cell to kill.</param>
    /// <param name="ct">Cancels waiting for shutdown — does not skip it.</param>
    Task StopAsync(string name, CancellationToken ct = default);

    /// <summary>List names of currently alive workflow cells.</summary>
    IReadOnlyList<string> ListActive();

    /// <summary>
    /// Pause a running cell — stop accepting new work, finish in-flight
    /// execution. The cell remains alive (listed in <see cref="ListActive"/>)
    /// but does not start new iterations of its loop.
    /// </summary>
    /// <remarks>
    /// Default implementation is a no-op for backward compatibility.
    /// Hosts that support graceful division should override this.
    /// </remarks>
    /// <param name="name">Name of the cell to pause.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PauseAsync(string name, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// Resume a previously paused cell. If the cell is not paused, this
    /// is a no-op.
    /// </summary>
    /// <param name="name">Name of the cell to resume.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ResumeAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
}
