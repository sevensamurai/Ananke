using Ananke.Federation.Paths;
using Ananke.Tool.Shared;
using System.CommandLine;
using System.Text.Json;

namespace Ananke.Tool.Platform.Commands;

/// <summary>
/// Handles <c>nnke-platform login --platform &lt;p&gt;</c> — launches the
/// platform-specific credential acquisition flow and persists credentials to
/// <c>~/.ananke/credentials.json</c> (chmod 600 on POSIX).
/// </summary>
internal static class LoginCommand
{
    private static string CredentialsPath => AnankePaths.CredentialsFile;

    public static Command Create()
    {
        var platformOption = new Option<string>("--platform", "-p")
        {
            Description = "Platform to authenticate: azure, google, or anthropic.",
            Required = true
        };

        var command = new Command("login", "Configure credentials for a federation platform.")
        {
            platformOption
        };

        command.SetAction(parseResult =>
        {
            var platform = parseResult.GetValue(platformOption)!;
            var json = parseResult.GetValue<bool>("--json");
            Execute(platform, json);
        });

        return command;
    }

    private static void Execute(string platform, bool json)
    {
        string? credential;

        try
        {
            credential = platform.ToLowerInvariant() switch
            {
                "azure" => AcquireAzure(),
                "google" => AcquireGoogle(),
                "anthropic" => AcquireAnthropic(),
                _ => null
            };
        }
        catch (Exception ex)
        {
            if (json) JsonOutput.Write(new { status = "error", platform, message = ex.Message });
            else Console.Error.WriteLine($"  Error during login: {ex.Message}");
            return;
        }

        if (credential is null)
        {
            if (json) JsonOutput.Write(new { status = "error", message = $"Unknown platform '{platform}'. Valid: azure, google, anthropic." });
            else Console.Error.WriteLine($"  Unknown platform '{platform}'. Valid: azure, google, anthropic.");
            return;
        }

        PersistCredential(platform, credential);

        if (json)
            JsonOutput.Write(new { status = "ok", platform, credentialsPath = CredentialsPath });
        else
        {
            Console.WriteLine();
            Console.WriteLine($"  ✓ Credentials saved for {platform}.");
            Console.WriteLine($"    Path: {CredentialsPath}");
            Console.WriteLine();
        }
    }

    // ── Platform-specific flows ──────────────────────────────────────

    private static string AcquireAzure()
    {
        Console.WriteLine();
        Console.WriteLine("  Azure: delegating to 'az login'...");
        Console.WriteLine("  (ensure the Azure CLI is installed: https://aka.ms/azure-cli)");
        Console.WriteLine();

        // In a full implementation: spawn `az login` and capture the resulting token.
        Console.Write("  Enter your Azure subscription ID: ");
        var subscriptionId = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(subscriptionId))
            throw new InvalidOperationException("Subscription ID is required.");

        return JsonSerializer.Serialize(new { provider = "azure", subscriptionId, method = "az-cli" });
    }

    private static string AcquireGoogle()
    {
        Console.WriteLine();
        Console.WriteLine("  Google Cloud: provide a service-account JSON key file path.");
        Console.Write("  Service account JSON path: ");
        var keyPath = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(keyPath) || !File.Exists(keyPath))
            throw new InvalidOperationException($"Service account file not found: {keyPath}");

        return JsonSerializer.Serialize(new { provider = "google", serviceAccountKeyPath = keyPath });
    }

    private static string AcquireAnthropic()
    {
        Console.WriteLine();
        Console.Write("  Anthropic API key (sk-ant-...): ");
        var key = ReadSecret();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("API key is required.");

        return JsonSerializer.Serialize(new { provider = "anthropic", apiKey = key });
    }

    // ── Persistence ─────────────────────────────────────────────────

    private static void PersistCredential(string platform, string credential)
    {
        var dir = Path.GetDirectoryName(CredentialsPath)!;
        Directory.CreateDirectory(dir);

        Dictionary<string, string> store;
        if (File.Exists(CredentialsPath))
        {
            try
            {
                store = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(CredentialsPath))
                        ?? new Dictionary<string, string>();
            }
            catch
            {
                store = new Dictionary<string, string>();
            }
        }
        else
        {
            store = new Dictionary<string, string>();
        }

        store[platform] = credential;
        File.WriteAllText(CredentialsPath, JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true }));

        // Best-effort chmod 600 on POSIX
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(CredentialsPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch { /* non-fatal */ }
        }
    }

    private static string ReadSecret()
    {
        var sb = new System.Text.StringBuilder();
        ConsoleKeyInfo key;
        while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace && sb.Length > 0)
                sb.Remove(sb.Length - 1, 1);
            else if (key.KeyChar != '\0')
                sb.Append(key.KeyChar);
        }
        Console.WriteLine();
        return sb.ToString();
    }
}
