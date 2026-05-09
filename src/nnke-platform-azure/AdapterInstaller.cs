using Ananke.Federation.Adapters;
using Ananke.Federation.Paths;

namespace Ananke.Tool.Platform.Azure;

/// <summary>
/// Copies this assembly (and its dependencies) into the nnke-platform adapters probe directory.
/// Invoked once when the user runs <c>nnke-platform-azure</c> after installation.
/// </summary>
internal static class AdapterInstaller
{
    private const string AdapterId = "azure-ai";
    private const string ManifestFileName = "azure-ai.adapter.json";

    internal static void Run(string[] args)
    {
        if (args.Length == 1 &&
            string.Equals(args[0], "--uninstall", StringComparison.OrdinalIgnoreCase))
        {
            Remove();
            return;
        }

        Install();
    }

    private static string AdaptersDirectory => AnankePaths.AdaptersDirectory;

    internal static void Install()
    {
        var sourceDir = AppContext.BaseDirectory;
        var targetDir = AdaptersDirectory;
        Directory.CreateDirectory(targetDir);

        var copied = 0;
        foreach (var dll in Directory.EnumerateFiles(sourceDir, "*.dll"))
        {
            var dest = Path.Combine(targetDir, Path.GetFileName(dll));
            File.Copy(dll, dest, overwrite: true);
            copied++;
        }

        WriteManifest(targetDir);

        Console.WriteLine($"nnke-platform-azure: installed {copied} file(s) to {targetDir}");
        Console.WriteLine("Run 'nnke-platform deploy --platform azure-ai' to deploy.");
    }

    private static void WriteManifest(string targetDir)
    {
        var manifest = new AdapterManifest
        {
            Id = AdapterId,
            DisplayName = "Azure AI Agent Service",
            Version = typeof(AdapterInstaller).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            MinCliVersion = "0.8",
            MaxCliVersionExclusive = "1.0",
            EntryAssembly = "nnke-platform-azure.dll",
        };
        File.WriteAllText(Path.Combine(targetDir, ManifestFileName), manifest.ToJson());
    }

    private static void Remove()
    {
        var dir = AdaptersDirectory;
        if (!Directory.Exists(dir))
            return;

        // Only remove DLLs that belong to this adapter (prefixed with known assembly names).
        var patterns = new[] { "nnke-platform-azure", "Ananke.Federation.Azure", "Azure.AI.Projects" };
        var removed = 0;
        foreach (var dll in Directory.EnumerateFiles(dir, "*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(dll);
            if (patterns.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                File.Delete(dll);
                removed++;
            }
        }

        var manifestPath = Path.Combine(dir, ManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);

        Console.WriteLine($"nnke-platform-azure: removed {removed} file(s) from {dir}");
    }
}
