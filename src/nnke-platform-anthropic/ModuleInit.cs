using System.Runtime.CompilerServices;
using Ananke.Federation.Anthropic;
using Ananke.Federation.Deployment;

namespace Ananke.Tool.Platform.Anthropic;

/// <summary>
/// Module initializer for the <c>nnke-platform-anthropic</c> companion tool.
/// Registers a factory for <c>"claude"</c> into <see cref="FederationDeployerRegistry"/>.
/// </summary>
/// <remarks>
/// Targets the Anthropic Beta managed-agents API (<c>agents-2025-05-14</c>).
/// See <c>Ananke.Federation.Anthropic/README.md</c> for the Beta dependency notice.
/// </remarks>
internal static class ModuleInit
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        FederationDeployerRegistry.RegisterFactory("claude", registry =>
        {
            var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            // apiKey may be null here — ClaudeCredentialProvider will re-read the
            // env var at credential resolution time, so a missing key at module-init
            // time is acceptable (the user may set it between init and deploy).
            var credentials = new ClaudeCredentialProvider(apiKey);
            return new ClaudeDeployer(credentials, registry);
        });
    }
}
