using System.Collections.Concurrent;
using Ananke.Design;
using Ananke.Federation.Deployment;
using Ananke.Orchestration.Tools;
using Ananke.Organics.Kernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ananke.Federation.Google;

/// <summary>
/// <see cref="IWorkflowHost"/> that manages cells as Gemini Enterprise Agent Platform agents.
/// <see cref="StartAsync"/> deploys the manifest to Agent Runtime;
/// <see cref="StopAsync"/> tears down the deployment.
/// </summary>
public sealed class VertexAIWorkflowHost(
    VertexAIDeployer deployer,
    WorkflowManifest manifest,
    ToolKit toolKit,
    ILogger<VertexAIWorkflowHost>? logger = null) : IWorkflowHost
{
    private readonly VertexAIDeployer _deployer = deployer ?? throw new ArgumentNullException(nameof(deployer));
    private readonly WorkflowManifest _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
    private readonly ToolKit _toolKit = toolKit ?? throw new ArgumentNullException(nameof(toolKit));
    private readonly ILogger<VertexAIWorkflowHost> _logger = logger ?? NullLogger<VertexAIWorkflowHost>.Instance;
    private readonly ConcurrentDictionary<string, CellEntry> _cells = new();

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
    public async Task StopAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!_cells.TryRemove(name, out var entry))
            return;

        if (entry.DeploymentId is not null)
        {
            try { await _deployer.TeardownAsync(entry.DeploymentId); }
            catch { /* Best-effort teardown */ }
        }

        await CancelAndDispose(entry);
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
                try { await _deployer.TeardownAsync(entry.DeploymentId); }
                catch { /* Best-effort */ }
            }

            await CancelAndDispose(entry);
        }
    }

    private async Task DeployAndMonitorAsync(string name, CancellationToken ct)
    {
        try
        {
            var options = new DeployOptions { Platform = _deployer.Platform };
            var record = await _deployer.DeployAsync(_manifest, _toolKit, options, ct);

            if (_cells.TryGetValue(name, out var existing))
                _cells.TryUpdate(name, existing with { DeploymentId = record.DeploymentId }, existing);

            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cell '{Name}' deployment failed on platform '{Platform}'", name, _deployer.Platform);

            if (_cells.TryGetValue(name, out var failing) && failing.DeploymentId is not null)
            {
                try { await _deployer.MarkFailedAsync(failing.DeploymentId, CancellationToken.None); }
                catch (Exception inner) { _logger.LogWarning(inner, "Could not mark deployment '{Id}' as failed", failing.DeploymentId); }
            }
        }
    }

    private static async Task CancelAndDispose(CellEntry entry)
    {
        try
        {
            await entry.Cts.CancelAsync();
            await entry.Loop;
        }
        catch (OperationCanceledException) { }
        catch { }
        finally { entry.Cts.Dispose(); }
    }

    private sealed record CellEntry(Task Loop, CancellationTokenSource Cts, string? DeploymentId);
}
