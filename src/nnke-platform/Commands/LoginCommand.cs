using Ananke.Federation.Validation;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Platform.Commands;

/// <summary>
/// Handles <c>nnke-platform login --platform &lt;p&gt;</c> — prints the environment a platform
/// needs, and how to obtain it. <b>Stores nothing.</b>
/// </summary>
/// <remarks>
/// <para>
/// <b>Rewritten.</b> It used to prompt interactively and write
/// <c>~/.ananke/credentials.json</c>, a file nothing ever read — so a plaintext API key sat on disk
/// with no compensating function. It also prompted for an Azure <i>subscription id</i>, which is not
/// what the adapter needs (it needs a project endpoint), and read secrets with
/// <c>Console.ReadKey</c>, so it could not be scripted, piped or run in CI on any platform.
/// </para>
/// <para>
/// <b>Ananke does not own a secret.</b> All three target clouds ship mature credential chains —
/// <c>az login</c>, Application Default Credentials, <c>AWS_PROFILE</c> — and competing with those
/// is how a fourth copy of a secret goes stale. This command's job is to say what to set and where
/// to get it, then get out of the way.
/// </para>
/// <para>
/// <b>The variable names here are a hint, not the contract.</b> Each adapter reads its own
/// configuration and reports its own failure; <c>nnke-platform whoami</c> and
/// <c>adapters doctor</c> surface those messages verbatim, and they are authoritative if this table
/// ever drifts.
/// </para>
/// </remarks>
internal static class LoginCommand
{
    public static Command Create()
    {
        var platformOption = new Option<string>("--platform", "-p")
        {
            Description = "Platform to describe: azure, vertex-ai, claude, or local.",
            Required = true
        };

        var command = new Command("login",
            "Print the environment variables a platform needs. Stores nothing — Ananke does not hold your credentials.")
        {
            platformOption
        };

        command.SetAction(parseResult =>
        {
            var platform = parseResult.GetValue(platformOption)!;
            var json = parseResult.GetValue<bool>("--json");
            return Execute(platform, json);
        });

        return command;
    }

    private static int Execute(string platform, bool json)
    {
        // Resolved like every other verb. This command used to be the only one that did not, so
        // `login --platform vertex-ai` — which the guide told users to run — exited 1 while
        // `deploy --platform vertex-ai` worked.
        var canonical = PlatformIdentifiers.Resolve(platform);

        var guidance = Guidance(canonical);
        if (guidance is null)
        {
            var known = string.Join(", ", PlatformIdentifiers.Canonical.Order());
            if (json)
                JsonOutput.Write(new { status = "error", message = $"Unknown platform '{platform}'. Known: {known}." });
            else
                Console.Error.WriteLine($"  ✗ Unknown platform '{platform}'. Known: {known}.");
            return 1;
        }

        if (json)
        {
            JsonOutput.Write(new
            {
                status = "ok",
                platform = canonical,
                variables = guidance.Variables.Select(v => new { name = v.Name, description = v.Description }),
                howTo = guidance.HowTo
            });
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"  {canonical} — {guidance.Summary}");
        Console.WriteLine();

        if (guidance.Variables.Count == 0)
        {
            Console.WriteLine("  Nothing to configure.");
            Console.WriteLine();
            return 0;
        }

        foreach (var v in guidance.Variables)
            Console.WriteLine($"  export {v.Name}=   # {v.Description}");

        Console.WriteLine();
        Console.WriteLine($"  {guidance.HowTo}");
        Console.WriteLine();
        Console.WriteLine("  Then confirm with: nnke-platform whoami");
        Console.WriteLine();
        return 0;
    }

    private static PlatformGuidance? Guidance(string canonical) => canonical switch
    {
        PlatformHost.LocalPlatform => new(
            "the in-process substrate",
            [],
            "No credentials, no cloud account, no adapter to install."),

        "azure" => new(
            "Microsoft Foundry Agent Service",
            [new("AZURE_AI_ENDPOINT",
                "Foundry *project* endpoint: https://<resource>.services.ai.azure.com/api/projects/<project>")],
            "Sign in so the credential chain resolves — 'az login' locally, managed identity in CI. "
            + "Note this is the project endpoint, not the model endpoint used by AZURE_OPENAI_ENDPOINT."),

        "vertex-ai" => new(
            "Gemini Enterprise Agent Platform",
            [
                new("GOOGLE_CLOUD_PROJECT", "GCP project id"),
                new("GOOGLE_CLOUD_LOCATION", "region; defaults to us-central1 when unset")
            ],
            "Authenticate with Application Default Credentials: "
            + "'gcloud auth application-default login' locally, Workload Identity in GCP."),

        "claude" => new(
            "Anthropic Claude Managed Agents (preview)",
            [new("ANTHROPIC_API_KEY", "API key on a workspace with Claude Agents beta access")],
            "The key needs the agents beta entitlement; without it the API authenticates and then "
            + "404s on /v1/agents."),

        _ => null
    };

    private sealed record PlatformGuidance(
        string Summary,
        IReadOnlyList<PlatformVariable> Variables,
        string HowTo);

    private sealed record PlatformVariable(string Name, string Description);
}
