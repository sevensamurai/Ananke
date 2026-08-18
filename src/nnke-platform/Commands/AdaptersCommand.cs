using Ananke.Federation.Adapters;
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

    private static int ExecuteDoctor(bool json)
    {
        var results = AdapterDiagnostics.Results;
        var unhealthy = results.Where(r => r.Status != AdapterLoadStatus.Loaded).ToList();
        var healthy = results.Where(r => r.Status == AdapterLoadStatus.Loaded).ToList();
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
                    issue = r.Status.ToString().ToLowerInvariant(),
                    error = r.ErrorMessage,
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
                Console.WriteLine($"  ✗ {r.AdapterId}  [{r.Status}]");
                Console.WriteLine($"    {r.ErrorMessage}");
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
