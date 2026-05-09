using Ananke.Federation.Validation;
using Ananke.Tool.Shared;
using System.CommandLine;
using System.Text.Json;

namespace Ananke.Tool.Platform.Commands;

/// <summary>
/// Handles <c>nnke-platform capabilities [--platform &lt;platform&gt;]</c> —
/// lists the known platform-native tool capabilities loaded from the embedded
/// <c>platform-capabilities.json</c> resource. Useful for discovering what
/// capability strings to use in deployment profiles.
/// </summary>
internal static class CapabilitiesCommand
{
    public static Command Create()
    {
        var platformOption = new Option<string?>("--platform", "-p")
        {
            Description = "Filter to a specific platform (e.g. azure-ai). Omit to list all."
        };

        var command = new Command("capabilities", "List known platform-native tool capabilities.")
        {
            platformOption
        };

        command.SetAction(parseResult =>
        {
            var platform = parseResult.GetValue(platformOption);
            var json = parseResult.GetValue<bool>("--json");
            Execute(platform, json);
        });

        return command;
    }

    private static void Execute(string? platform, bool json)
    {
        var data = LoadCapabilitiesData();

        if (platform is not null)
        {
            if (!data.TryGetValue(platform, out var caps))
            {
                if (json)
                    JsonOutput.Write(new { status = "error", message = $"Unknown platform '{platform}'.", available = data.Keys.ToList() });
                else
                    Console.Error.WriteLine($"  Unknown platform '{platform}'. Available: {string.Join(", ", data.Keys)}");
                return;
            }

            if (json)
                JsonOutput.Write(new { status = "ok", platform, capabilities = caps });
            else
            {
                Console.WriteLine();
                Console.WriteLine($"  Capabilities for '{platform}':");
                Console.WriteLine("  ─────────────────────────────────────────────────");
                foreach (var cap in caps)
                    Console.WriteLine($"    {cap}");
                Console.WriteLine();
            }
            return;
        }

        // List all
        if (json)
        {
            JsonOutput.Write(new { status = "ok", platforms = data });
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("  Platform Capabilities");
            Console.WriteLine("  ─────────────────────────────────────────────────");
            foreach (var (name, caps) in data)
            {
                Console.WriteLine($"\n  {name}:");
                foreach (var cap in caps)
                    Console.WriteLine($"    {cap}");
            }
            Console.WriteLine();
        }
    }

    private static Dictionary<string, List<string>> LoadCapabilitiesData()
    {
        var assembly = typeof(DeployabilityValidator).Assembly;
        using var stream = assembly.GetManifestResourceStream("Ananke.Federation.platform-capabilities.json");
        if (stream is null)
            return [];

        using var doc = JsonDocument.Parse(stream);
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (doc.RootElement.TryGetProperty("platforms", out var platforms))
        {
            foreach (var platform in platforms.EnumerateObject())
            {
                var caps = new List<string>();
                if (platform.Value.TryGetProperty("capabilities", out var capsArray))
                {
                    foreach (var cap in capsArray.EnumerateArray())
                    {
                        if (cap.GetString() is { } value)
                            caps.Add(value);
                    }
                }
                result[platform.Name] = caps;
            }
        }

        return result;
    }
}
