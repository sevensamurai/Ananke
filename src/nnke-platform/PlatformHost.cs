using System.Reflection;
using System.Text.Json;
using Ananke.Federation.Adapters;
using Ananke.Federation.Deployment;
using Ananke.Federation.Paths;

namespace Ananke.Tool.Platform;

/// <summary>
/// Owns the shared runtime state for a single CLI invocation:
/// the <see cref="IDeploymentRegistry"/> and the <see cref="FederationDeployerRegistry"/>
/// surface used to resolve platform adapters.
/// </summary>
/// <remarks>
/// <para>
/// On construction this host:
/// <list type="number">
///   <item>Creates the <see cref="IDeploymentRegistry"/> (file-backed by default, in-memory under <c>--in-memory</c>).</item>
///   <item>Probes <see cref="AdaptersDirectory"/> for <c>*.adapter.json</c> manifests, validates
///         the <c>targetCliVersion</c> range, and loads only compatible adapter DLLs so their
///         module initializers fire and call <see cref="FederationDeployerRegistry.RegisterFactory"/>.
///         All outcomes are recorded in <see cref="AdapterDiagnostics"/>.</item>
///   <item>Calls <see cref="FederationDeployerRegistry.MaterializeFactories"/> so every registered
///         factory receives the live <see cref="IDeploymentRegistry"/>.</item>
/// </list>
/// </para>
/// </remarks>
internal sealed class PlatformHost : IDisposable
{
    /// <summary>
    /// Primary adapters probe directory: <c>~/.ananke/adapters/</c>.
    /// Resolved via <see cref="AnankePaths.AdaptersDirectory"/>.
    /// </summary>
    public static string AdaptersDirectory => AnankePaths.AdaptersDirectory;

    /// <summary>
    /// The version of this CLI assembly, used for adapter compatibility checks.
    /// Falls back to <c>0.0.0</c> when the assembly carries no version (e.g. unit tests).
    /// </summary>
    internal static Version CliVersion { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    private readonly IDisposable? _ownedRegistry;

    /// <summary>The deployment registry for this invocation.</summary>
    public IDeploymentRegistry Registry { get; }

    /// <summary>
    /// Initialises a new <see cref="PlatformHost"/>.
    /// </summary>
    /// <param name="inMemory">
    /// When <see langword="true"/> an <see cref="InMemoryDeploymentRegistry"/> is used;
    /// otherwise a <see cref="JsonFileDeploymentRegistry"/> backed by
    /// <see cref="JsonFileDeploymentRegistry.DefaultPath"/> is created.
    /// </param>
    public PlatformHost(bool inMemory = false)
    {
        if (inMemory)
        {
            Registry = new InMemoryDeploymentRegistry();
        }
        else
        {
            var fileRegistry = new JsonFileDeploymentRegistry();
            Registry = fileRegistry;
            _ownedRegistry = fileRegistry;
        }

        LoadAdapterAssemblies();
        FederationDeployerRegistry.MaterializeFactories(Registry);
    }

    /// <summary>
    /// Tries to resolve a deployer for the given platform from the
    /// <see cref="FederationDeployerRegistry"/>. Returns <see langword="null"/> when no
    /// adapter is registered, so the caller can emit a helpful install hint.
    /// </summary>
    public IFederationDeployer? ResolveDeployer(string platform) =>
        FederationDeployerRegistry.TryResolve(platform, out var deployer) ? deployer : null;

    /// <inheritdoc />
    public void Dispose() => _ownedRegistry?.Dispose();

    // ── private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Probes <see cref="AdaptersDirectory"/> for <c>*.adapter.json</c> manifests, validates
    /// CLI-version compatibility, and loads the entry assembly for each compatible adapter.
    /// All outcomes — loaded, skipped, or failed — are recorded in <see cref="AdapterDiagnostics"/>.
    /// </summary>
    private static void LoadAdapterAssemblies()
    {
        if (Directory.Exists(AnankePaths.AdaptersDirectory))
            LoadFromDirectory(AnankePaths.AdaptersDirectory);
    }

    private static void LoadFromDirectory(string directory)
    {
        foreach (var manifestFile in Directory.EnumerateFiles(directory, "*.adapter.json"))
        {
            AdapterManifest? manifest = null;
            try
            {
                manifest = AdapterManifest.FromJson(File.ReadAllText(manifestFile));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                AdapterDiagnostics.Record(new AdapterLoadResult
                {
                    AdapterId = Path.GetFileNameWithoutExtension(manifestFile),
                    Status = AdapterLoadStatus.InvalidManifest,
                    Path = manifestFile,
                    ErrorMessage = ex.Message,
                });
                continue;
            }

            if (!manifest.IsCompatibleWith(CliVersion))
            {
                AdapterDiagnostics.Record(new AdapterLoadResult
                {
                    AdapterId = manifest.Id,
                    Status = AdapterLoadStatus.VersionMismatch,
                    Path = manifestFile,
                    Manifest = manifest,
                    ErrorMessage =
                        $"Adapter '{manifest.Id}' v{manifest.Version} requires nnke-platform " +
                        $">={manifest.MinCliVersion}" +
                        (manifest.MaxCliVersionExclusive is not null
                            ? $" <{manifest.MaxCliVersionExclusive}"
                            : string.Empty) +
                        $"; running v{CliVersion.ToString(3)}. " +
                        $"Run 'dotnet tool update nnke-platform' or 'dotnet tool update nnke-platform-{manifest.Id}' to fix.",
                });
                continue;
            }

            var dllPath = Path.Combine(directory, manifest.EntryAssembly);
            if (!File.Exists(dllPath))
            {
                AdapterDiagnostics.Record(new AdapterLoadResult
                {
                    AdapterId = manifest.Id,
                    Status = AdapterLoadStatus.LoadFailed,
                    Path = dllPath,
                    Manifest = manifest,
                    ErrorMessage = $"Entry assembly '{manifest.EntryAssembly}' not found in adapters directory.",
                });
                continue;
            }

            try
            {
                Assembly.LoadFrom(dllPath);
                AdapterDiagnostics.Record(new AdapterLoadResult
                {
                    AdapterId = manifest.Id,
                    Status = AdapterLoadStatus.Loaded,
                    Path = dllPath,
                    Manifest = manifest,
                });
            }
            catch (Exception ex)
            {
                AdapterDiagnostics.Record(new AdapterLoadResult
                {
                    AdapterId = manifest.Id,
                    Status = AdapterLoadStatus.LoadFailed,
                    Path = dllPath,
                    Manifest = manifest,
                    ErrorMessage = ex.Message,
                });
            }
        }
    }
}
