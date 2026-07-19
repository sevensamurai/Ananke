namespace Ananke.Federation.Paths;

/// <summary>
/// Single source of truth for all on-disk config paths used by <c>nnke-platform</c>
/// and the adapter installer tools.
/// </summary>
/// <remarks>
/// Root is <c>~/.ananke/</c> on all platforms.
/// On Linux/macOS <c>$XDG_DATA_HOME</c> is respected when set, giving
/// <c>$XDG_DATA_HOME/.ananke/</c>.
/// <list type="table">
/// <listheader><term>Property</term><term>Resolved path (example, Windows)</term></listheader>
/// <item><term><see cref="ConfigRoot"/></term><term><c>C:\Users\you\.ananke\</c></term></item>
/// <item><term><see cref="CredentialsFile"/></term><term><c>~/.ananke/credentials.json</c></term></item>
/// <item><term><see cref="DeploymentsFile"/></term><term><c>~/.ananke/deployments/registry.json</c></term></item>
/// <item><term><see cref="AdaptersDirectory"/></term><term><c>~/.ananke/adapters/</c></term></item>
/// </list>
/// </remarks>
public static class AnankePaths
{
    // ── Primary paths ─────────────────────────────────────────────────────────

    /// <summary>
    /// Root config directory: <c>~/.ananke/</c> (or <c>$XDG_DATA_HOME/.ananke/</c>
    /// on Linux/macOS when the environment variable is set).
    /// </summary>
    public static string ConfigRoot { get; } = ResolveConfigRoot();

    /// <summary>Credentials file: <c>&lt;ConfigRoot&gt;/credentials.json</c>.</summary>
    public static string CredentialsFile => Path.Combine(ConfigRoot, "credentials.json");

    /// <summary>Deployment registry file: <c>&lt;ConfigRoot&gt;/deployments/registry.json</c>.</summary>
    public static string DeploymentsFile => Path.Combine(ConfigRoot, "deployments", "registry.json");

    /// <summary>
    /// Adapters probe directory: <c>&lt;ConfigRoot&gt;/adapters/</c>.
    /// Adapter installer tools copy DLLs and manifest sidecars here;
    /// <c>PlatformHost</c> probes it at startup.
    /// </summary>
    public static string AdaptersDirectory => Path.Combine(ConfigRoot, "adapters");

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ResolveConfigRoot()
    {
        string dataBase;
        if (OperatingSystem.IsWindows())
        {
            dataBase = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        else
        {
            dataBase = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local", "share");
        }
        return Path.Combine(dataBase, ".ananke");
    }

}
