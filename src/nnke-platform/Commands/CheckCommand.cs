using Ananke.Design;
using Ananke.Federation.Deployment;
using Ananke.Federation.Validation;
using Ananke.Orchestration.Tools;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Platform.Commands;

/// <summary>
/// Handles <c>nnke-platform check &lt;file&gt; --platform &lt;p&gt;</c> — the live half of
/// validation. Resolves the platform's deployer and calls
/// <see cref="IFederationDeployer.ValidateAsync"/>, which performs credential, model-availability
/// and quota checks against the platform itself. <b>Creates nothing.</b>
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <c>IFederationDeployer</c> has always had a deliberate two-phase
/// contract — validate, then deploy — and the CLI exposed only the second phase.
/// <c>validate</c> runs the offline <see cref="DeployabilityValidator"/> and never constructs a
/// deployer; <c>deploy --dry-run</c> short-circuits before one is asked for anything. So
/// <c>ValidateAsync</c> was reachable only from inside <c>DeployAsync</c> — by which point the
/// command is already creating cloud resources. There was no way to ask "are my credentials good?"
/// without also saying "please create an agent".
/// </para>
/// <para>
/// <b>Named <c>check</c>, not <c>doctor</c>.</b> <c>adapters doctor</c> already exists and answers a
/// narrower question — is what I installed loadable and resolvable — with no platform contact at
/// all. Overloading the word would blur the one distinction that matters here: <c>doctor</c> stays
/// local, <c>check</c> goes out to the platform.
/// </para>
/// </remarks>
internal static class CheckCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<FileInfo>("file")
        {
            Description = "Path to the .ananke.yml manifest file."
        };

        var platformOption = new Option<string>("--platform", "-p")
        {
            Description = "Platform to check against (e.g. local, azure, vertex-ai, claude).",
            Required = true
        };

        var catalogOption = new Option<FileInfo?>("--catalog")
        {
            Description = "Manifest whose models: section resolves `ref:` model aliases (e.g. roles.ananke.yml)."
        };

        var command = new Command("check",
            "Check credentials and platform readiness for a manifest. Contacts the platform; creates nothing.")
        {
            fileArg,
            platformOption,
            catalogOption
        };

        command.SetAction(async parseResult =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var platform = parseResult.GetValue(platformOption)!;
            var catalog = parseResult.GetValue(catalogOption);
            var json = parseResult.GetValue<bool>("--json");

            using var host = new PlatformHost(inMemory: true);
            try
            {
                return await ExecuteAsync(host, file, platform, catalog, json);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("atalogue"))
            {
                WriteError(json, ex.Message);
                return 1;
            }
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(
        PlatformHost host, FileInfo file, string platform, FileInfo? catalog, bool json)
    {
        var deployer = host.ResolveDeployer(platform);
        if (deployer is null)
        {
            WriteError(json,
                $"No adapter registered for '{platform}'. {DeployCommand.AdapterInstallHint(platform)} "
                + "Run 'nnke-platform adapters doctor' to see why an installed adapter did not resolve.");
            return 1;
        }

        PreviewNotice.WriteIfPreview(platform);

        if (!file.Exists)
        {
            WriteError(json, $"File not found: {file.FullName}");
            return 1;
        }

        WorkflowManifest manifest;
        try { manifest = WorkflowManifest.Load(file.FullName); }
        catch (Exception ex) { WriteError(json, $"Failed to parse manifest: {ex.Message}"); return 1; }

        // Structural problems first: there is no sense spending a network round trip — or a
        // credential prompt — on a manifest that cannot deploy for reasons visible offline.
        var structural = new DeployabilityValidator()
            .Validate(manifest, new ToolKit("check-stub"), platform, ModelCatalogue.Load(catalog));

        if (!structural.IsDeployable)
        {
            Report(json, platform, manifest.Name, structural, "structural", contacted: false);
            return 2;
        }

        DeployabilityReport live;
        try
        {
            live = await deployer.ValidateAsync(manifest, new ToolKit("check-stub"));
        }
        catch (Exception ex)
        {
            // A deployer that throws rather than reporting is still an answer, and a useful one —
            // it is what an unreachable endpoint or a rejected credential usually looks like.
            WriteError(json, $"Platform check failed for '{platform}': {ex.Message}");
            return 2;
        }

        // The built-in substrate runs in-process, so a clean result there says nothing about
        // credentials — and "READY" must not be read as "my credentials work" when none were used.
        var contacted = !string.Equals(
            PlatformIdentifiers.Resolve(platform), PlatformHost.LocalPlatform, StringComparison.OrdinalIgnoreCase);

        Report(json, platform, manifest.Name, live, contacted ? "platform" : "in-process", contacted);
        return live.IsDeployable ? 0 : 2;
    }

    private static void Report(
        bool json, string platform, string workflow, DeployabilityReport report, string stage, bool contacted)
    {
        if (json)
        {
            JsonOutput.Write(new
            {
                status = report.IsDeployable ? "ok" : "blocked",
                stage,
                contactedPlatform = contacted,
                workflow,
                platform,
                diagnostics = report.Diagnostics.Select(d => new
                {
                    severity = d.Severity.ToString().ToLowerInvariant(),
                    code = d.Code,
                    message = d.Message,
                    suggestion = d.Suggestion
                })
            });
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"  Checking '{workflow}' against '{platform}'");
        Console.WriteLine("  ─────────────────────────────────────────────────");

        foreach (var d in report.Diagnostics)
            Console.WriteLine($"  {Icon(d.Severity)} [{d.Code}] {d.Message}");

        Console.WriteLine();

        // Said out loud, because "no problems found" means something different depending on whether
        // anything was actually asked. The local substrate needs no credentials, so a clean result
        // there is a statement about the manifest, not about any platform.
        Console.WriteLine(report.IsDeployable
            ? contacted
                ? $"  Result: READY — '{platform}' accepted the configuration."
                : $"  Result: READY — '{platform}' runs in-process; no credentials were used."
            : "  Result: NOT READY — see the diagnostics above.");
        Console.WriteLine();
    }

    private static string Icon(DeployDiagnosticSeverity severity) => severity switch
    {
        DeployDiagnosticSeverity.Error => "✗",
        DeployDiagnosticSeverity.Warning => "⚠",
        _ => "ℹ"
    };

    private static void WriteError(bool json, string message)
    {
        if (json)
            JsonOutput.Write(new { status = "error", message });
        else
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"  ✗ {message}");
            Console.Error.WriteLine();
        }
    }
}
