using Ananke.Federation.Paths;
using Ananke.Tool.Shared;
using System.CommandLine;
using System.Text.Json;

namespace Ananke.Tool.Platform.Commands;

/// <summary>
/// Handles <c>nnke-platform whoami</c> — reads persisted credentials and reports
/// the configured identity for each platform.
/// </summary>
internal static class WhoAmICommand
{
    private static string CredentialsPath => AnankePaths.CredentialsFile;

    public static Command Create()
    {
        var command = new Command("whoami",
            "Show the currently configured identity for each federation platform.");

        command.SetAction(parseResult =>
        {
            var json = parseResult.GetValue<bool>("--json");
            Execute(json);
        });

        return command;
    }

    private static void Execute(bool json)
    {
        if (!File.Exists(CredentialsPath))
        {
            if (json)
                JsonOutput.Write(new { status = "not-configured", message = "No credentials found. Run 'nnke-platform login --platform <p>'." });
            else
            {
                Console.WriteLine();
                Console.WriteLine("  No credentials configured.");
                Console.WriteLine("  Run: nnke-platform login --platform <azure|google|anthropic>");
                Console.WriteLine();
            }
            return;
        }

        Dictionary<string, string> store;
        try
        {
            store = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(CredentialsPath))
                    ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            if (json) JsonOutput.Write(new { status = "error", message = $"Failed to read credentials: {ex.Message}" });
            else Console.Error.WriteLine($"  Failed to read credentials: {ex.Message}");
            return;
        }

        var identities = store.Select(kv =>
        {
            try
            {
                var cred = JsonSerializer.Deserialize<Dictionary<string, string>>(kv.Value)
                           ?? new Dictionary<string, string>();
                var identity = kv.Key switch
                {
                    "azure" => cred.TryGetValue("subscriptionId", out var sub) ? $"subscription: {sub}" : "(configured)",
                    "google" => cred.TryGetValue("serviceAccountKeyPath", out var path) ? $"key: {path}" : "(configured)",
                    "anthropic" => cred.TryGetValue("apiKey", out var key)
                        ? $"sk-ant-...{key[^4..]}"
                        : "(configured)",
                    _ => "(configured)"
                };
                return new { platform = kv.Key, identity, status = "configured" };
            }
            catch
            {
                return new { platform = kv.Key, identity = "(unreadable)", status = "error" };
            }
        }).ToList();

        if (json)
        {
            JsonOutput.Write(new { status = "ok", credentialsPath = CredentialsPath, platforms = identities });
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"  Credentials: {CredentialsPath}");
        Console.WriteLine();
        foreach (var id in identities)
            Console.WriteLine($"  {id.platform,-12}  {id.identity}");

        Console.WriteLine();
        Console.WriteLine("  Note: Use 'nnke-platform login --platform <p>' to update credentials.");
        Console.WriteLine();
    }
}
