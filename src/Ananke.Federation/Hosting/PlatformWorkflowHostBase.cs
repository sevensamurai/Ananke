using System.Collections.Concurrent;
using Ananke.Design;
using Ananke.Federation.Deployment;
using Ananke.Orchestration.Tools;
using Ananke.Organics.Kernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ananke.Federation.Hosting;

/// <summary>
/// Shared lifecycle base for platform-specific <see cref="IWorkflowHost"/> implementations.
/// Handles the deploy → keep-alive → teardown loop; subclasses provide only the
/// platform-specific deploy/teardown calls.
/// </summary>
/// <remarks>
/// <para>
/// The <c>workflowLoop</c> passed to <see cref="StartAsync"/> is
/// <b>not executed locally</b> — the agent runs on the remote platform.
/// The parameter is accepted for interface compatibility but is replaced by a
/// lightweight cancellable <c>Task.Delay(Infinite)</c> that keeps the cell
/// registered until <see cref="StopAsync"/> or <see cref="DisposeAsync"/> is called.
/// </para>
/// <para>
/// Subclass pattern:
/// <code>
/// public sealed class AcmeWorkflowHost(AcmeDeployer deployer, WorkflowManifest manifest, ToolKit toolKit,
///     ILogger&lt;AcmeWorkflowHost&gt;? logger = null)
///     : PlatformWorkflowHostBase(manifest, toolKit, logger)
/// {
///     protected override string Platform => deployer.Platform;
///
///     protected override Task&lt;DeploymentRecord&gt; DeployCoreAsync(
///         WorkflowManifest manifest, ToolKit toolKit, DeployOptions options, CancellationToken ct)
///         => deployer.DeployAsync(manifest, toolKit, options, ct);
///
///     protected override Task TeardownCoreAsync(string deploymentId, CancellationToken ct)
///         => deployer.TeardownAsync(deploymentId, ct);
///
///     protected override Task MarkDeploymentFailedAsync(string deploymentId, CancellationToken ct)
///         => deployer.MarkFailedAsync(deploymentId, ct);
/// }
/// </code>
/// </para>
/// </remarks>
public abstract class PlatformWorkflowHostBase : IWorkflowHost
{
    private readonly WorkflowManifest _manifest;
    private readonly ToolKit _toolKit;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, CellEntry> _cells = new();

    /// <summary>
    /// Initialises the base with the manifest, toolkit, and optional logger.
    /// </summary>
    protected PlatformWorkflowHostBase(WorkflowManifest manifest, ToolKit toolKit, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(toolKit);
        _manifest = manifest;
        _toolKit = toolKit;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Platform identifier (e.g. <c>"azure-ai"</c>). Used to build <see cref="DeployOptions"/>.</summary>
    protected abstract string Platform { get; }

    /// <summary>
    /// Submits the manifest and toolkit to the remote platform and returns the deployment record.
    /// </summary>
    protected abstract Task<DeploymentRecord> DeployCoreAsync(
        WorkflowManifest manifest,
        ToolKit toolKit,
        DeployOptions options,
        CancellationToken ct);

    /// <summary>
    /// Tears down a previously created remote deployment.
    /// Should be a best-effort no-op when the deployment does not exist.
    /// </summary>
    protected abstract Task TeardownCoreAsync(string deploymentId, CancellationToken ct);

    /// <summary>
    /// Marks a deployment as failed in the registry. Called when deploy throws.
    /// Default is a no-op; override to call <c>deployer.MarkFailedAsync</c>.
    /// </summary>
    protected virtual Task MarkDeploymentFailedAsync(string deploymentId, CancellationToken ct)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartAsync(string name, Func<CancellationToken, Task> workflowLoop, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var cts = new CancellationTokenSource();
        var placeholder = new CellEntry(Task.CompletedTask, cts, DeploymentId: null);

        if (!_cells.TryAdd(name, placeholder))
        {
            cts.Dispose();
            throw new InvalidOperationException($"A cell named '{name}' is already alive.");
        }

        var task = Task.Run(() => DeployAndMonitorAsync(name, cts.Token), CancellationToken.None);
        _cells[name] = placeholder with { Loop = task };

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!_cells.TryRemove(name, out var entry))
            return;

        if (entry.DeploymentId is not null)
        {
            // Teardown always runs to completion regardless of the caller's ct —
            // best-effort, and abandoning it would leak the remote deployment.
            try { await TeardownCoreAsync(entry.DeploymentId, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogDebug(ex, "Best-effort teardown failed for deployment '{Id}'", entry.DeploymentId); }
        }

        await CancelAndDispose(entry, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListActive() => [.. _cells.Keys];

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        var entries = _cells.ToArray();
        _cells.Clear();

        foreach (var (_, entry) in entries)
        {
            if (entry.DeploymentId is not null)
            {
                try { await TeardownCoreAsync(entry.DeploymentId, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogDebug(ex, "Best-effort teardown failed for deployment '{Id}' during dispose", entry.DeploymentId); }
            }

            await CancelAndDispose(entry).ConfigureAwait(false);
        }
    }

    private async Task DeployAndMonitorAsync(string name, CancellationToken ct)
    {
        try
        {
            var options = new DeployOptions { Platform = Platform };
            var record = await DeployCoreAsync(_manifest, _toolKit, options, ct).ConfigureAwait(false);

            if (_cells.TryGetValue(name, out var existing))
                _cells.TryUpdate(name, existing with { DeploymentId = record.DeploymentId }, existing);

            // Keep the cell "alive" until cancelled — the agent runs remotely.
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected — cell was stopped.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cell '{Name}' deployment failed on platform '{Platform}'", name, Platform);

            if (_cells.TryGetValue(name, out var failing) && failing.DeploymentId is not null)
            {
                try { await MarkDeploymentFailedAsync(failing.DeploymentId, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception inner)
                {
                    _logger.LogWarning(inner, "Could not mark deployment '{Id}' as failed", failing.DeploymentId);
                }
            }
        }
    }

    private static async Task CancelAndDispose(CellEntry entry, CancellationToken ct = default)
    {
        try
        {
            await entry.Cts.CancelAsync().ConfigureAwait(false);
            await entry.Loop.WaitAsync(ct).ConfigureAwait(false);
        }
        // Best-effort teardown: the loop may fault or throw while being cancelled, but this
        // helper runs during Stop/Dispose, so there is nothing left to report the failure to.
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (Exception) when (!ct.IsCancellationRequested) { }
        finally { entry.Cts.Dispose(); }
    }

    private sealed record CellEntry(Task Loop, CancellationTokenSource Cts, string? DeploymentId);
}
