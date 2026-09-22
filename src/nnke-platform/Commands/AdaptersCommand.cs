using Ananke.Federation.Adapters;
using Ananke.Federation.Deployment;
using Ananke.Federation.Paths;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Platform.Commands;

/// <summary>
/// Handles <c>nnke-platform adapters list</c> and <c>nnke-platform adapters doctor</c>.
/// Reads from <see cref="AdapterDiagnostics"/> which is populated by <c>PlatformHost</c>
/// at startup.
/// </summary>
internal static class AdaptersCommand
{
    public static Command Create()
    {
        var command = new Command("adapters", "Inspect installed nnke-platform adapter status.")
        {
            CreateList(),
            CreateDoctor(),
        };
        return command;
    }

    // ── adapters list ─────────────────────────────────────────────────────────

    private static Command CreateList()
    {
        var command = new Command("list", "List all adapters found in the probe directory and their load status.");
        command.SetAction(parseResult =>
        {
            var json = parseResult.GetValue<bool>("--json");
            using (ProbeAdapters())
                return ExecuteList(json);
        });
        return command;
    }

    /// <summary>
    /// Explains an adapter that loaded but whose platform does not resolve — the state
    /// <see cref="AdapterLoadStatus"/> has no value for.
    /// </summary>
    private static string UnresolvedReason(AdapterLoadResult result) =>
        $"Adapter '{result.AdapterId}' loaded but registered no deployer, so 'deploy --platform "
        + $"{result.AdapterId}' cannot resolve it. The adapter and CLI versions may be mismatched.";

    private static int ExecuteList(bool json)
    {
        var results = AdapterDiagnostics.Results;

        if (json)
        {
            JsonOutput.Write(new
            {
                status = "ok",
                adaptersDirectory = PlatformHost.AdaptersDirectory,
                adapters = results.Select(r => new
                {
                    id = r.AdapterId,
                    status = r.Status.ToString().ToLowerInvariant(),
                    version = r.Manifest?.Version,
                    displayName = r.Manifest?.DisplayName,
                    path = r.Path,
                    error = r.ErrorMessage,
                }),
            });
            return 0;
        }

        Console.WriteLine($"  Adapters directory: {PlatformHost.AdaptersDirectory}");
        Console.WriteLine();

        if (results.Count == 0)
        {
            Console.WriteLine("  No adapters found.");
            Console.WriteLine($"  Install one with: dotnet tool install nnke-platform-azure");
            return 0;
        }

        foreach (var r in results)
        {
            var icon = r.Status == AdapterLoadStatus.Loaded ? "✓" : "✗";
            var versionLabel = r.Manifest is not null ? $" v{r.Manifest.Version}" : string.Empty;
            Console.WriteLine($"  {icon} {r.AdapterId}{versionLabel}  [{r.Status}]");
            if (r.Manifest is not null)
                Console.WriteLine($"      {r.Manifest.DisplayName}");
            if (r.ErrorMessage is not null)
                Console.WriteLine($"      {r.ErrorMessage}");
        }

        // 'list' reports what was found and succeeds whenever it could report it; the
        // health verdict (and the non-zero exit that goes with it) belongs to 'doctor'.
        return 0;
    }

    // ── adapters doctor ───────────────────────────────────────────────────────

    private static Command CreateDoctor()
    {
        var command = new Command("doctor",
            "Report adapter health: flags version mismatches, missing manifests, and load failures.");
        command.SetAction(parseResult =>
        {
            var json = parseResult.GetValue<bool>("--json");
            using (ProbeAdapters())
                return ExecuteDoctor(json);
        });
        return command;
    }

    /// <summary>
    /// Constructs a <see cref="PlatformHost"/> purely for its side effect: the constructor
    /// probes the adapters directory and populates <see cref="AdapterDiagnostics"/>, which
    /// both subcommands read. Without this the diagnostics set is always empty and every
    /// installed adapter is reported as missing.
    /// </summary>
    /// <remarks>
    /// An in-memory registry is used unconditionally — inspecting adapters is a read-only
    /// diagnostic and must not create or open the on-disk deployment registry.
    /// </remarks>
    private static PlatformHost ProbeAdapters() => new(inMemory: true);

    /// <summary>
    /// An adapter is healthy when its <b>platform resolves</b>, not merely when its file loaded.
    /// </summary>
    /// <remarks>
    /// <c>AdapterLoadStatus.Loaded</c> records only that <c>Assembly.LoadFrom</c> did not throw. For
    /// the entire life of the probe that was true of every adapter while <c>deploy</c> could resolve
    /// none of them, so <c>doctor</c> printed "All adapters healthy" and <c>deploy</c> failed on the
    /// next line. Checking resolution is what makes this command able to see the
    /// only failure it exists to catch.
    /// </remarks>
    private static bool Resolves(AdapterLoadResult result) =>
        result.Status == AdapterLoadStatus.Loaded
        && FederationDeployerRegistry.TryResolve(result.AdapterId, out _);

    private static int ExecuteDoctor(bool json)
    {
        var results = AdapterDiagnostics.Results;
        var healthy = results.Where(Resolves).ToList();

        // One adapter, one verdict. A factory that threw produces two records for the same id — the
        // Loaded one written by the probe and the LoadFailed one written when materialization
        // failed — and the second carries the actual reason. Reporting both would name the same
        // adapter twice and bury the useful message under a generic one.
        var unhealthy = results
            .Where(r => !Resolves(r))
            .GroupBy(r => r.AdapterId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.FirstOrDefault(r => r.ErrorMessage is not null) ?? g.First())
            .ToList();
        var allOk = unhealthy.Count == 0;

        if (json)
        {
            JsonOutput.Write(new
            {
                status = allOk ? "ok" : "degraded",
                adaptersDirectory = PlatformHost.AdaptersDirectory,
                healthy = healthy.Select(r => new { id = r.AdapterId, version = r.Manifest?.Version }),
                issues = unhealthy.Select(r => new
                {
                    id = r.AdapterId,
                    issue = r.Status == AdapterLoadStatus.Loaded
                        ? "unresolved"
                        : r.Status.ToString().ToLowerInvariant(),
                    error = r.ErrorMessage ?? UnresolvedReason(r),
                    path = r.Path,
                }),
            });
            return allOk ? 0 : 2;
        }

        Console.WriteLine($"  Adapters directory: {PlatformHost.AdaptersDirectory}");
        Console.WriteLine();

        if (results.Count == 0)
        {
            Console.WriteLine("  No adapters installed.");
            Console.WriteLine("  Install with: dotnet tool install nnke-platform-azure  (or -google / -anthropic)");
            return 0;
        }

        foreach (var r in healthy)
            Console.WriteLine($"  ✓ {r.AdapterId} v{r.Manifest!.Version} — {r.Manifest.DisplayName}");

        if (unhealthy.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Issues:");
            foreach (var r in unhealthy)
            {
                var label = r.Status == AdapterLoadStatus.Loaded ? "Unresolved" : r.Status.ToString();
                Console.WriteLine($"  ✗ {r.AdapterId}  [{label}]");
                Console.WriteLine($"    {r.ErrorMessage ?? UnresolvedReason(r)}");
            }

            Console.WriteLine();
            Console.WriteLine("  Run 'nnke-platform adapters list' for full details.");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("  All adapters healthy.");
        }

        return allOk ? 0 : 2;
    }

}
