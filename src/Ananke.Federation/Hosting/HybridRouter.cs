using Ananke.Federation.Deployment;

namespace Ananke.Federation.Hosting;

/// <summary>
/// Decides where a workflow cell should be hosted: locally or on a specific
/// remote platform. Uses deployment registry, capability matching, and
/// cost/latency heuristics to route cells.
/// </summary>
/// <remarks>
/// <para>
/// Routing decisions are made at cell start time and are sticky for the
/// lifetime of the cell. Migration (moving a cell between platforms) is a
/// teardown + re-deploy operation, not a live migration.
/// </para>
/// </remarks>
public sealed class HybridRouter
{
    private readonly IDeploymentRegistry _registry;
    private readonly IReadOnlyList<RoutingRule> _rules;

    /// <summary>
    /// Creates a hybrid router with the given deployment registry and routing rules.
    /// Rules are evaluated in order — first match wins. If no rule matches, the
    /// cell is routed to the local host.
    /// </summary>
    /// <param name="registry">Deployment registry for querying active remote deployments.</param>
    /// <param name="rules">Ordered routing rules. First match wins.</param>
    public HybridRouter(IDeploymentRegistry registry, IReadOnlyList<RoutingRule>? rules = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
        _rules = rules ?? [];
    }

    /// <summary>
    /// Determines the hosting target for a cell by name. Returns the platform
    /// identifier (e.g. <c>"azure-ai"</c>, <c>"vertex-ai"</c>) or <see langword="null"/>
    /// for local hosting.
    /// </summary>
    /// <param name="cellName">The cell name to route.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Platform identifier for remote hosting, or <see langword="null"/> for local.
    /// </returns>
    public async Task<string?> ResolveAsync(string cellName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cellName);

        // Check if there's already an active deployment for this cell
        var deployments = await _registry.ListAsync(cellName, ct);
        var active = deployments.FirstOrDefault(d => d.Status == DeploymentStatus.Active);
        if (active is not null)
            return active.Platform;

        // Evaluate routing rules
        foreach (var rule in _rules)
        {
            if (rule.Matches(cellName))
                return rule.TargetPlatform;
        }

        // Default: local
        return null;
    }

    /// <summary>
    /// Resolves all currently active remote deployments, keyed by workflow name.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, DeploymentRecord>> GetActiveDeploymentsAsync(CancellationToken ct = default)
    {
        var all = await _registry.ListAsync(ct: ct);
        return all
            .Where(d => d.Status == DeploymentStatus.Active)
            .ToDictionary(d => d.WorkflowName);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="platform"/> is a
    /// <c>local-emulated:&lt;platform&gt;</c> tier value, and extracts the
    /// emulated platform name.
    /// </summary>
    /// <param name="platform">The platform string returned by <see cref="ResolveAsync"/>.</param>
    /// <param name="emulatedPlatform">
    /// The platform being emulated (e.g. <c>"azure-ai"</c>) when the method returns
    /// <see langword="true"/>; <see langword="null"/> otherwise.
    /// </param>
    public static bool IsLocalEmulated(string? platform, out string? emulatedPlatform)
    {
        const string prefix = "local-emulated:";
        if (platform is not null &&
            platform.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            emulatedPlatform = platform[prefix.Length..];
            return true;
        }

        emulatedPlatform = null;
        return false;
    }
}

/// <summary>
/// A routing rule that maps cell names (by prefix, suffix, or exact match)
/// to a target platform.
/// </summary>
public sealed record RoutingRule
{
    /// <summary>
    /// The target platform identifier (e.g. <c>"azure-ai"</c>, <c>"vertex-ai"</c>, <c>"claude"</c>).
    /// Use <see langword="null"/> to explicitly force local hosting.
    /// </summary>
    public required string? TargetPlatform { get; init; }

    /// <summary>Exact cell name match. Takes priority over prefix/suffix.</summary>
    public string? ExactName { get; init; }

    /// <summary>Cell name prefix match (e.g. <c>"search-"</c>).</summary>
    public string? Prefix { get; init; }

    /// <summary>Cell name suffix match (e.g. <c>"-heavy"</c>).</summary>
    public string? Suffix { get; init; }

    /// <summary>
    /// Evaluates whether this rule matches the given cell name.
    /// </summary>
    public bool Matches(string cellName)
    {
        if (ExactName is not null)
            return string.Equals(cellName, ExactName, StringComparison.OrdinalIgnoreCase);

        if (Prefix is not null && cellName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return true;

        if (Suffix is not null && cellName.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
            return true;

        return ExactName is null && Prefix is null && Suffix is null;
    }

    // ── Factory helpers ─────────────────────────────────────────────

    /// <summary>
    /// Creates a rule that routes all cells to the local emulator for
    /// <paramref name="platform"/> (i.e. <c>local-emulated:&lt;platform&gt;</c>).
    /// </summary>
    /// <param name="platform">Platform to emulate (e.g. <c>"azure-ai"</c>).</param>
    public static RoutingRule EmulateAll(string platform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        return new RoutingRule { TargetPlatform = $"local-emulated:{platform}" };
    }

    /// <summary>
    /// Creates a rule that routes the exact cell named <paramref name="cellName"/>
    /// to the local emulator for <paramref name="platform"/>.
    /// </summary>
    public static RoutingRule EmulateCell(string cellName, string platform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cellName);
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        return new RoutingRule
        {
            ExactName = cellName,
            TargetPlatform = $"local-emulated:{platform}"
        };
    }
}
