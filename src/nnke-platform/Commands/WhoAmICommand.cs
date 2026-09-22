using Ananke.Federation.Adapters;
using Ananke.Federation.Deployment;
using Ananke.Federation.Paths;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Platform.Commands;

/// <summary>
/// Handles <c>nnke-platform whoami</c> — reports the configuration <c>deploy</c> would actually
/// use, per platform.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rewritten.</b> It used to read back
/// <c>~/.ananke/credentials.json</c> — a file <b>no deployer, validator or credential provider ever
/// read</b>. Its output therefore described a store with no bearing on what would happen, keyed by
/// platform names (<c>google</c>, <c>anthropic</c>) that <c>deploy --platform</c> does not accept.
/// </para>
/// <para>
/// It now answers the question people actually ask it — <i>what will <c>deploy</c> use?</i> — from
/// live resolution state rather than from a file. A platform that resolves is usable; one that does
/// not carries <b>the adapter's own explanation</b>, which is why this command needs no table of
/// per-platform environment variables and cannot drift from what the adapters require.
/// </para>
/// </remarks>
internal static class WhoAmICommand
{
    public static Command Create()
    {
        var command = new Command("whoami",
            "Show the configuration deploy would use for each platform, and why any are unavailable.");

        command.SetAction(parseResult =>
        {
            var json = parseResult.GetValue<bool>("--json");
            using (var host = new PlatformHost(inMemory: true))
                return Execute(json, host);
        });

        return command;
    }

    private static int Execute(bool json, PlatformHost host)
    {
        var probed = AdapterDiagnostics.Results
            .GroupBy(r => r.AdapterId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.FirstOrDefault(r => r.ErrorMessage is not null) ?? g.First())
            .ToList();

        var platforms = new List<PlatformState>
        {
            // Always available and needing no credentials — that is the point of it.
            new(PlatformHost.LocalPlatform, Available: true, Source: "built-in", Detail: "in-process; no credentials required")
        };

        platforms.AddRange(probed.Select(r => new PlatformState(
            r.AdapterId,
            Available: host.ResolveDeployer(r.AdapterId) is not null,
            Source: "adapter",
            Detail: host.ResolveDeployer(r.AdapterId) is not null
                ? r.Manifest is not null ? $"v{r.Manifest.Version}" : "installed"
                : r.ErrorMessage ?? "installed but registered no deployer")));

        var legacy = File.Exists(AnankePaths.CredentialsFile) ? AnankePaths.CredentialsFile : null;

        if (json)
        {
            JsonOutput.Write(new
            {
                status = "ok",
                adaptersDirectory = PlatformHost.AdaptersDirectory,
                platforms = platforms.Select(p => new
                {
                    platform = p.Platform,
                    available = p.Available,
                    source = p.Source,
                    detail = p.Detail
                }),
                legacyCredentialsFile = legacy
            });
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"  Adapters directory: {PlatformHost.AdaptersDirectory}");
        Console.WriteLine();

        foreach (var p in platforms)
            Console.WriteLine($"  {(p.Available ? "✓" : "✗")} {p.Platform,-12} {p.Detail}");

        Console.WriteLine();
        Console.WriteLine("  Credentials come from the environment — run 'nnke-platform login --platform <p>'");
        Console.WriteLine("  to print the variables a platform needs.");

        if (legacy is not null)
        {
            Console.WriteLine();
            Console.WriteLine($"  Note: {legacy} exists but is no longer read by anything.");
            Console.WriteLine("  It is safe to delete.");
        }

        Console.WriteLine();
        return 0;
    }

    private sealed record PlatformState(string Platform, bool Available, string Source, string Detail);
}
