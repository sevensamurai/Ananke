using System.Collections.Concurrent;

namespace Ananke.Federation.Deployment;

/// <summary>
/// Global registry that maps platform identifiers to <see cref="IFederationDeployer"/> instances.
/// Companion packages (e.g. <c>nnke-platform-azure</c>) self-register via module initializers;
/// the CLI never directly references the adapter assemblies.
/// </summary>
/// <remarks>
/// <para>
/// Module initializers in companion packages call <see cref="RegisterFactory"/> to register
/// a lazy factory. The CLI host then calls <see cref="MaterializeFactories"/> once the
/// <see cref="IDeploymentRegistry"/> is available, which creates and registers the actual
/// deployer instances.
/// </para>
/// <para>
/// Already-instantiated deployers can be registered directly via <see cref="Register"/>.
/// </para>
/// </remarks>
public static class FederationDeployerRegistry
{
    private static readonly ConcurrentDictionary<string, IFederationDeployer> _deployers =
        new(StringComparer.OrdinalIgnoreCase);

    // Factories are registered by module initializers (fire once per AppDomain).
    // They are intentionally NOT cleared by Reset() so that MaterializeFactories
    // can be called again after a reset in tests.
    private static readonly ConcurrentDictionary<string, Func<IDeploymentRegistry, IFederationDeployer>> _factories =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a deployer for a platform. Throws <see cref="InvalidOperationException"/>
    /// if a deployer for the same platform identifier is already registered.
    /// </summary>
    /// <param name="deployer">The deployer to register.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="deployer"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a deployer for the same platform is already registered.</exception>
    public static void Register(IFederationDeployer deployer)
    {
        ArgumentNullException.ThrowIfNull(deployer);

        if (!_deployers.TryAdd(deployer.Platform, deployer))
            throw new InvalidOperationException(
                $"A deployer for platform '{deployer.Platform}' is already registered.");
    }

    /// <summary>
    /// Tries to resolve the deployer for the given platform identifier.
    /// Accepts canonical identifiers (e.g. <c>"azure-ai"</c>) and post-rebrand aliases
    /// (e.g. <c>"foundry"</c>, <c>"gemini-enterprise"</c>).
    /// </summary>
    /// <param name="platform">The platform identifier.</param>
    /// <param name="deployer">
    /// When this method returns <see langword="true"/>, contains the registered deployer;
    /// otherwise <see langword="null"/>.
    /// </param>
    /// <returns><see langword="true"/> if a deployer was found; otherwise <see langword="false"/>.</returns>
    public static bool TryResolve(string platform, out IFederationDeployer? deployer)
    {
        ArgumentNullException.ThrowIfNull(platform);

        if (_deployers.TryGetValue(platform, out deployer))
            return true;

        // Fall back to canonical alias (e.g. foundry → azure-ai).
        if (PlatformAliases.TryGetValue(platform, out var canonical))
            return _deployers.TryGetValue(canonical, out deployer);

        return false;
    }

    // Maps post-rebrand names to canonical SDK-era identifiers.
    private static readonly IReadOnlyDictionary<string, string> PlatformAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["foundry"] = "azure-ai",
            ["gemini-enterprise"] = "vertex-ai"
        };

    /// <summary>
    /// Returns a snapshot of all currently registered platform identifiers.
    /// </summary>
    public static IReadOnlyList<string> RegisteredPlatforms =>
        _deployers.Keys.ToArray();

    /// <summary>
    /// Registers a deferred deployer factory for a platform. Intended for use by companion
    /// package module initializers. The factory receives the host's <see cref="IDeploymentRegistry"/>
    /// when <see cref="MaterializeFactories"/> is called.
    /// </summary>
    /// <param name="platform">The platform identifier (e.g. <c>"azure-ai"</c>).</param>
    /// <param name="factory">
    /// A factory delegate that creates a deployer bound to the supplied registry.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a factory for the same platform is already registered.</exception>
    public static void RegisterFactory(string platform, Func<IDeploymentRegistry, IFederationDeployer> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentNullException.ThrowIfNull(factory);

        if (!_factories.TryAdd(platform, factory))
            throw new InvalidOperationException(
                $"A deployer factory for platform '{platform}' is already registered.");
    }

    /// <summary>
    /// Creates a deployer from each registered factory and adds it to the active registry.
    /// Call this once per CLI invocation after the <see cref="IDeploymentRegistry"/> is known.
    /// Factories for platforms that already have a live deployer registered are skipped.
    /// </summary>
    /// <param name="deploymentRegistry">The deployment registry to pass to each factory.</param>
    public static void MaterializeFactories(IDeploymentRegistry deploymentRegistry)
    {
        ArgumentNullException.ThrowIfNull(deploymentRegistry);

        foreach (var (platform, factory) in _factories)
        {
            if (_deployers.ContainsKey(platform))
                continue;

            var deployer = factory(deploymentRegistry);
            // Best-effort: another thread may have materialized the same platform concurrently.
            _deployers.TryAdd(deployer.Platform, deployer);
        }
    }

    /// <summary>
    /// Returns a snapshot of all platform identifiers for which a factory has been registered
    /// (includes platforms not yet materialized).
    /// </summary>
    public static IReadOnlyList<string> RegisteredFactoryPlatforms =>
        _factories.Keys.ToArray();

    /// <summary>
    /// Removes all registered deployers and factories. Intended for use in tests only.
    /// </summary>
    internal static void Reset()
    {
        _deployers.Clear();
        _factories.Clear();
    }
}
