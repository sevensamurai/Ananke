using Ananke.Design;
using Ananke.Federation.Deployment;
using Ananke.Federation.Validation;
using Ananke.Orchestration.Tools;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Platform.Commands;

/// <summary>
/// Handles <c>nnke-platform deploy &lt;file&gt; --platform &lt;platform&gt;</c> —
/// deploys a workflow manifest to a target platform. Runs validation first,
/// then invokes the appropriate <see cref="IFederationDeployer"/> resolved from
/// <see cref="FederationDeployerRegistry"/>.
/// </summary>
internal static class DeployCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<FileInfo>("file")
        {
            Description = "Path to the .ananke.yml manifest file."
        };

        var platformOption = new Option<string>("--platform", "-p")
        {
            Description = "Target platform (e.g. azure-ai, gemini-agent-platform, vertex-ai, claude).",
            Required = true
        };

        var profileOption = new Option<string?>("--profile")
        {
            Description = "Deployment profile name from the manifest's profiles: section."
        };

        var forceOption = new Option<bool>("--force")
        {
            Description = "Force re-deployment even if an active deployment exists."
        };

        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Validate and show what would be deployed without actually deploying."
        };

        var command = new Command("deploy", "Deploy a workflow to a target platform.")
        {
            fileArg,
            platformOption,
            profileOption,
            forceOption,
            dryRunOption
        };

        command.SetAction(async parseResult =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var platform = parseResult.GetValue(platformOption)!;
            var profile = parseResult.GetValue(profileOption);
            var force = parseResult.GetValue(forceOption);
            var dryRun = parseResult.GetValue(dryRunOption);
            var json = parseResult.GetValue<bool>("--json");
            var inMemory = parseResult.GetValue<bool>("--in-memory");

            using var host = new PlatformHost(inMemory);
            return await ExecuteAsync(host, file, platform, profile, force, dryRun, json);
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(
        PlatformHost host,
        FileInfo file,
        string platform,
        string? profileName,
        bool force,
        bool dryRun,
        bool json)
    {
        // ── 1. Resolve adapter ───────────────────────────────────────────────
        var deployer = host.ResolveDeployer(platform);
        if (deployer is null)
        {
            var hint = AdapterInstallHint(platform);
            if (json)
                JsonOutput.Write(new { status = "error", message = $"No adapter registered for '{platform}'.", installHint = hint });
            else
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"  ✗ No adapter registered for '{platform}'.");
                Console.Error.WriteLine($"    {hint}");
                Console.Error.WriteLine();
            }
            return 1;
        }

        // ── 2. Load manifest ─────────────────────────────────────────────────
        if (!file.Exists)
        {
            WriteError(json, $"File not found: {file.FullName}");
            return 1;
        }

        WorkflowManifest manifest;
        try { manifest = WorkflowManifest.Load(file.FullName); }
        catch (Exception ex) { WriteError(json, $"Failed to parse manifest: {ex.Message}"); return 1; }

        // ── 3. Build toolkit stub ─────────────────────────────────────────────
        var toolKit = BuildToolKit(manifest, profileName, out var profileError);
        if (toolKit is null) { WriteError(json, profileError!); return 1; }

        // ── 4. Structural validation ─────────────────────────────────────────
        var validator = new DeployabilityValidator();
        var report = validator.Validate(manifest, toolKit, platform);

        if (!report.IsDeployable)
        {
            if (json)
                JsonOutput.Write(new
                {
                    status = "blocked",
                    workflow = manifest.Name,
                    platform,
                    diagnostics = report.Diagnostics.Select(d => new
                    {
                        severity = d.Severity.ToString().ToLowerInvariant(),
                        code = d.Code,
                        message = d.Message,
                        suggestion = d.Suggestion
                    })
                });
            else
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"  ✗ Manifest validation failed for '{platform}' — fix errors before deploying.");
                foreach (var d in report.Errors)
                    Console.Error.WriteLine($"    [{d.Code}] {d.Message}");
                Console.Error.WriteLine();
            }
            return 2;
        }

        // ── 5. Dry-run short-circuit ─────────────────────────────────────────
        if (dryRun)
        {
            if (json)
                JsonOutput.Write(new { status = "dry-run", workflow = manifest.Name, platform, profile = profileName, deployable = true });
            else
            {
                Console.WriteLine();
                Console.WriteLine($"  [DRY RUN] '{manifest.Name}' → '{platform}'" +
                    (profileName is not null ? $" (profile: {profileName})" : ""));
                Console.WriteLine("  Manifest is deployable. No resources were created.");
                Console.WriteLine();
            }
            return 0;
        }

        // ── 6. Check for existing active deployment ──────────────────────────
        if (!force)
        {
            var existing = (await host.Registry.ListAsync(manifest.Name))
                .FirstOrDefault(r => r.Platform == platform && r.Status == DeploymentStatus.Active);

            if (existing is not null)
            {
                if (json)
                    JsonOutput.Write(new { status = "skipped", message = "Active deployment already exists. Use --force to re-deploy.", deploymentId = existing.DeploymentId });
                else
                {
                    Console.WriteLine();
                    Console.WriteLine($"  ⚠ Active deployment '{existing.DeploymentId}' already exists for '{manifest.Name}' on '{platform}'.");
                    Console.WriteLine("    Use --force to re-deploy.");
                    Console.WriteLine();
                }
                return 0;
            }
        }

        // ── 7. Deploy ────────────────────────────────────────────────────────
        DeploymentRecord record;
        try
        {
            record = await deployer.DeployAsync(manifest, toolKit, new DeployOptions { Platform = platform, Force = force });
        }
        catch (Exception ex) { WriteError(json, $"Deployment failed: {ex.Message}"); return 2; }

        // ── 8. Persist record ────────────────────────────────────────────────
        await host.Registry.RegisterAsync(record);

        if (json)
            JsonOutput.Write(new
            {
                status = "deployed",
                deploymentId = record.DeploymentId,
                workflow = record.WorkflowName,
                platform = record.Platform,
                version = record.Version,
                deploymentStatus = record.Status.ToString().ToLowerInvariant()
            });
        else
        {
            Console.WriteLine();
            Console.WriteLine($"  ✓ Deployed '{record.WorkflowName}' to '{record.Platform}'");
            Console.WriteLine($"    Deployment ID : {record.DeploymentId}");
            Console.WriteLine($"    Version       : {record.Version}");
            Console.WriteLine($"    Status        : {record.Status}");
            Console.WriteLine();
        }

        return 0;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static ToolKit? BuildToolKit(WorkflowManifest manifest, string? profileName, out string? error)
    {
        error = null;
        var toolKit = new ToolKit("deploy-stub");

        if (profileName is null)
            return toolKit;

        if (!manifest.Profiles.TryGetValue(profileName, out var profileDef))
        {
            error = $"Profile '{profileName}' not found in manifest. Available: {string.Join(", ", manifest.Profiles.Keys)}";
            return null;
        }

        foreach (var (toolName, _) in profileDef.Tools)
            toolKit.AddTool(toolName, $"Stub for {toolName}", b => b.OnExecute(_ => ToolResult.Ok("stub")));

        var boundProfile = new DeploymentProfile
        {
            Name = profileName,
            Tools = profileDef.Tools.ToDictionary(
                kvp => kvp.Key,
                kvp => new ToolBinding { Execute = kvp.Value.Execute, Platform = kvp.Value.Platform, Endpoint = kvp.Value.Endpoint })
        };

        return boundProfile.Bind(toolKit);
    }

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

    internal static string AdapterInstallHint(string platform) =>
        platform switch
        {
            "azure-ai" => "Install nnke-platform-azure: dotnet tool install -g nnke-platform-azure",
            "vertex-ai" or "gemini-agent-platform" => "Install nnke-platform-google: dotnet tool install -g nnke-platform-google",
            "claude" => "Install nnke-platform-anthropic: dotnet tool install -g nnke-platform-anthropic",
            _ => $"Install the adapter for '{platform}' and ensure it is loaded before invoking this command."
        };
}
