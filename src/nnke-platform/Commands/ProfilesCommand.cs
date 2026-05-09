using Ananke.Design;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Platform.Commands;

/// <summary>
/// Handles <c>nnke-platform profiles &lt;file&gt;</c> — lists deployment profiles
/// defined in a manifest, or shows the tool bindings for a specific profile.
/// </summary>
internal static class ProfilesCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<FileInfo>("file")
        {
            Description = "Path to the .ananke.yml manifest file."
        };

        var profileArg = new Argument<string?>("profile")
        {
            Description = "Profile name to inspect. Omit to list all profiles.",
            DefaultValueFactory = _ => null,
        };

        var command = new Command("profiles", "List or inspect deployment profiles in a manifest.")
        {
            fileArg,
            profileArg
        };

        command.SetAction(parseResult =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var profile = parseResult.GetValue(profileArg);
            var json = parseResult.GetValue<bool>("--json");
            Execute(file, profile, json);
        });

        return command;
    }

    private static void Execute(FileInfo file, string? profileName, bool json)
    {
        if (!file.Exists)
        {
            if (json)
                JsonOutput.Write(new { status = "error", message = $"File not found: {file.FullName}" });
            else
                Console.Error.WriteLine($"  File not found: {file.FullName}");
            return;
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
            return;
        }

        if (manifest.Profiles.Count == 0)
        {
            if (json)
                JsonOutput.Write(new { status = "ok", workflow = manifest.Name, profiles = Array.Empty<object>(), message = "No profiles defined. Add a profiles: section to the manifest." });
            else
            {
                Console.WriteLine();
                Console.WriteLine($"  No profiles defined in '{manifest.Name}'.");
                Console.WriteLine("  Add a profiles: section to the manifest to define platform-specific tool bindings.");
                Console.WriteLine();
            }
            return;
        }

        if (profileName is null)
        {
            // List all profiles
            if (json)
            {
                JsonOutput.Write(new
                {
                    status = "ok",
                    workflow = manifest.Name,
                    profiles = manifest.Profiles.Select(p => new
                    {
                        name = p.Key,
                        toolCount = p.Value.Tools.Count,
                        tools = p.Value.Tools.Keys.ToList(),
                    }).ToList(),
                });
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine($"  Profiles in '{manifest.Name}':");
                Console.WriteLine("  ─────────────────────────────────────────────────");
                foreach (var (name, profile) in manifest.Profiles)
                {
                    Console.WriteLine($"    {name,-20} {profile.Tools.Count} tool binding(s)");
                }
                Console.WriteLine();
            }
            return;
        }

        // Show specific profile
        if (!manifest.Profiles.TryGetValue(profileName, out var profileDef))
        {
            if (json)
                JsonOutput.Write(new { status = "error", message = $"Profile '{profileName}' not found.", available = manifest.Profiles.Keys.ToList() });
            else
                Console.Error.WriteLine($"  Profile '{profileName}' not found. Available: {string.Join(", ", manifest.Profiles.Keys)}");
            return;
        }

        if (json)
        {
            JsonOutput.Write(new
            {
                status = "ok",
                workflow = manifest.Name,
                profile = profileName,
                tools = profileDef.Tools.Select(t => new
                {
                    name = t.Key,
                    execute = t.Value.Execute,
                    platform = t.Value.Platform,
                    endpoint = t.Value.Endpoint,
                }).ToList(),
            });
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine($"  Profile '{profileName}' in '{manifest.Name}':");
            Console.WriteLine("  ─────────────────────────────────────────────────");
            foreach (var (toolName, binding) in profileDef.Tools)
            {
                var detail = binding.Platform is not null
                    ? $"platform: {binding.Platform}"
                    : binding.Endpoint is not null
                        ? $"{binding.Execute} → {binding.Endpoint}"
                        : binding.Execute;
                Console.WriteLine($"    {toolName,-20} {detail}");
            }
            Console.WriteLine();
        }
    }
}
