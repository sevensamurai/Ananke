using Ananke.Design;
using Ananke.Federation.Validation;
using Ananke.Orchestration.Tools;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Platform.Commands;

/// <summary>
/// Handles <c>nnke-platform validate &lt;file&gt; --platform &lt;platform&gt;</c> —
/// loads an <c>.ananke.yml</c> manifest and runs structural deployability checks
/// against the target platform. Reports FED-series diagnostics with severity,
/// suggestions, and profile hints.
/// </summary>
internal static class ValidateCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<FileInfo>("file")
        {
            Description = "Path to the .ananke.yml manifest file."
        };

        var platformOption = new Option<string>("--platform", "-p")
        {
            Description = "Target platform to validate against (e.g. azure-ai, gemini-agent-platform, vertex-ai, claude).",
            Required = true
        };

        var profileOption = new Option<string?>("--profile")
        {
            Description = "Deployment profile name from the manifest's profiles: section. When set, tools are rebound before validation."
        };

        var command = new Command("validate", "Validate a manifest's deployability to a target platform.")
        {
            fileArg,
            platformOption,
            profileOption
        };

        command.SetAction(parseResult =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var platform = parseResult.GetValue(platformOption)!;
            var profile = parseResult.GetValue(profileOption);
            var json = parseResult.GetValue<bool>("--json");
            return Execute(file, platform, profile, json);
        });

        return command;
    }

    private static int Execute(FileInfo file, string platform, string? profileName, bool json)
    {
        if (!file.Exists)
        {
            if (json)
                JsonOutput.Write(new { status = "error", message = $"File not found: {file.FullName}" });
            else
                Console.Error.WriteLine($"  File not found: {file.FullName}");
            return 1;
        }

        WorkflowManifest manifest;
        try
        {
            manifest = WorkflowManifest.Load(file.FullName);
        }
        catch (Exception ex)
        {
            if (json)
                JsonOutput.Write(new { status = "error", message = $"Failed to parse manifest: {ex.Message}" });
            else
                Console.Error.WriteLine($"  Failed to parse manifest: {ex.Message}");
            return 1;
        }

        // Build a toolkit stub — structural validation doesn't execute tools,
        // but needs to see execution modes and capabilities.
        // In a real project the user's ToolKit would be loaded from their assembly.
        // For now we create an empty kit; future: discover from project assembly.
        var toolKit = new ToolKit("validate-stub");

        // Apply deployment profile if specified
        if (profileName is not null)
        {
            if (!manifest.Profiles.TryGetValue(profileName, out var profileDef))
            {
                if (json)
                    JsonOutput.Write(new { status = "error", message = $"Profile '{profileName}' not found in manifest. Available: {string.Join(", ", manifest.Profiles.Keys)}" });
                else
                    Console.Error.WriteLine($"  Profile '{profileName}' not found. Available: {string.Join(", ", manifest.Profiles.Keys)}");
                return 1;
            }

            // Apply the profile's tool bindings to the stub kit
            var boundProfile = new Federation.Deployment.DeploymentProfile
            {
                Name = profileName,
                Tools = profileDef.Tools.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new Federation.Deployment.ToolBinding
                    {
                        Execute = kvp.Value.Execute,
                        Platform = kvp.Value.Platform,
                        Endpoint = kvp.Value.Endpoint
                    })
            };

            // Create placeholder tools for each binding so validation can inspect them
            foreach (var (toolName, binding) in profileDef.Tools)
            {
                toolKit.AddTool(toolName, $"Stub for {toolName}", b =>
                {
                    b.OnExecute(_ => ToolResult.Ok("stub"));
                });
            }

            toolKit = boundProfile.Bind(toolKit);
        }

        var validator = new DeployabilityValidator();
        var report = validator.Validate(manifest, toolKit, platform);

        if (json)
            WriteJson(manifest, report, platform, profileName);
        else
            WriteHuman(manifest, report, platform, profileName);

        // A manifest that cannot be deployed is a failed check, not a successful report.
        return report.IsDeployable ? 0 : 2;
    }

    private static void WriteJson(WorkflowManifest manifest, DeployabilityReport report, string platform, string? profile)
    {
        JsonOutput.Write(new
        {
            status = report.IsDeployable ? "deployable" : "blocked",
            workflow = manifest.Name,
            platform,
            profile,
            diagnostics = report.Diagnostics.Select(d => new
            {
                severity = d.Severity.ToString().ToLowerInvariant(),
                code = d.Code,
                message = d.Message,
                component = d.Component,
                suggestion = d.Suggestion,
            }).ToList(),
            errors = report.Errors.Count(),
            warnings = report.Diagnostics.Count(d => d.Severity == DeployDiagnosticSeverity.Warning),
        });
    }

    private static void WriteHuman(WorkflowManifest manifest, DeployabilityReport report, string platform, string? profile)
    {
        Console.WriteLine();
        Console.WriteLine($"  Validating '{manifest.Name}' for platform '{platform}'" +
            (profile is not null ? $" (profile: {profile})" : ""));
        Console.WriteLine("  ─────────────────────────────────────────────────");

        if (report.Diagnostics.Count == 0)
        {
            Console.WriteLine("  ✓ No issues found — manifest is deployable.");
            Console.WriteLine();
            return;
        }

        foreach (var d in report.Diagnostics)
        {
            var icon = d.Severity switch
            {
                DeployDiagnosticSeverity.Error => "✗",
                DeployDiagnosticSeverity.Warning => "⚠",
                _ => "ℹ"
            };

            Console.WriteLine($"  {icon} [{d.Code}] {d.Message}");
            if (d.Component is not null)
                Console.WriteLine($"           Component: {d.Component}");
            if (d.Suggestion is not null)
                Console.WriteLine($"           Suggestion: {d.Suggestion}");
        }

        Console.WriteLine();
        Console.WriteLine(report.IsDeployable
            ? "  Result: DEPLOYABLE (with warnings)"
            : "  Result: BLOCKED — fix errors before deploying.");
        Console.WriteLine();
    }
}
