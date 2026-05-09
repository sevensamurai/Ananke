using System.Collections.Concurrent;
using Ananke.Organics.Kernel;

namespace Ananke.Federation.Hosting;

/// <summary>
/// Composite <see cref="IWorkflowHost"/> that manages cells across a local host
/// and zero or more platform-specific remote hosts. Routes start/stop operations
/// to the appropriate underlying host via <see cref="HybridRouter"/>.
/// </summary>
/// <remarks>
/// <para>
/// Remote hosts represent platform-deployed cells (Azure AI agents, Vertex AI agents,
/// Claude managed agents). Their <see cref="IWorkflowHost.StartAsync"/> creates a proxy loop
/// that monitors the remote deployment, and <see cref="IWorkflowHost.StopAsync"/> tears
/// down the remote agent.
/// </para>
/// <para>
/// If no routing rule matches, or the router returns <see langword="null"/>, the cell
/// runs on the local host (in-process). This ensures graceful degradation — if a
/// platform is unavailable, cells fall back to local execution.
/// </para>
/// </remarks>
public sealed class FederatedWorkflowHost : IWorkflowHost
{
    private readonly IWorkflowHost _localHost;
    private readonly IReadOnlyDictionary<string, IWorkflowHost> _platformHosts;
    private readonly HybridRouter _router;
    private readonly bool _allowFallbackToLocal;
    private readonly ConcurrentDictionary<string, string?> _cellPlatformMap = new();

    /// <summary>
    /// Creates a federated workflow host.
    /// </summary>
    /// <param name="localHost">The local (in-process) host for cells that don't route to a platform.</param>
    /// <param name="platformHosts">
    /// Platform-specific hosts keyed by platform identifier (e.g. <c>"azure-ai"</c>).
    /// </param>
    /// <param name="router">Hybrid router that determines where each cell should run.</param>
    /// <param name="allowFallbackToLocal">
    /// When <see langword="true"/>, cells whose resolved platform is not registered in
    /// <paramref name="platformHosts"/> silently fall back to the local host.
    /// When <see langword="false"/> (the default), an unregistered platform throws
    /// <see cref="InvalidOperationException"/> so misconfiguration is surfaced immediately.
    /// </param>
    public FederatedWorkflowHost(
        IWorkflowHost localHost,
        IReadOnlyDictionary<string, IWorkflowHost> platformHosts,
        HybridRouter router,
        bool allowFallbackToLocal = false)
    {
        ArgumentNullException.ThrowIfNull(localHost);
        ArgumentNullException.ThrowIfNull(platformHosts);
        ArgumentNullException.ThrowIfNull(router);

        _localHost = localHost;
        _platformHosts = platformHosts;
        _router = router;
        _allowFallbackToLocal = allowFallbackToLocal;
    }

    /// <summary>The local (in-process) host.</summary>
    public IWorkflowHost LocalHost => _localHost;

    /// <summary>Platform hosts keyed by platform identifier.</summary>
    public IReadOnlyDictionary<string, IWorkflowHost> PlatformHosts => _platformHosts;

    /// <summary>The router used for cell placement decisions.</summary>
    public HybridRouter Router => _router;

    /// <inheritdoc />
    public async Task StartAsync(string name, Func<CancellationToken, Task> workflowLoop, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(workflowLoop);

        var platform = await _router.ResolveAsync(name, ct);
        var host = ResolveHost(platform);
        _cellPlatformMap[name] = platform;
        await host.StartAsync(name, workflowLoop, ct);
    }

    /// <inheritdoc />
    public async Task StopAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_cellPlatformMap.TryRemove(name, out var platform))
        {
            var host = ResolveHost(platform);
            await host.StopAsync(name);
        }
        else
        {
            // Unknown cell — try all hosts
            await _localHost.StopAsync(name);
            foreach (var host in _platformHosts.Values)
                await host.StopAsync(name);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListActive()
    {
        var result = new List<string>(_localHost.ListActive());
        foreach (var host in _platformHosts.Values)
            result.AddRange(host.ListActive());
        return result;
    }

    /// <summary>
    /// Gets the platform where a cell is currently hosted.
    /// Returns <see langword="null"/> for locally-hosted cells.
    /// </summary>
    public string? GetCellPlatform(string cellName) =>
        _cellPlatformMap.TryGetValue(cellName, out var platform) ? platform : null;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _localHost.DisposeAsync();
        foreach (var host in _platformHosts.Values)
            await host.DisposeAsync();
        _cellPlatformMap.Clear();
    }

    private IWorkflowHost ResolveHost(string? platform)
    {
        if (platform is null)
            return _localHost;

        if (_platformHosts.TryGetValue(platform, out var host))
            return host;

        if (_allowFallbackToLocal)
            return _localHost;

        throw new InvalidOperationException(
            $"Platform '{platform}' is not registered in this FederatedWorkflowHost. " +
            $"Registered platforms: [{string.Join(", ", _platformHosts.Keys)}]. " +
            "Set allowFallbackToLocal: true in the constructor to fall back to local execution instead.");
    }
}
