using System.Runtime.CompilerServices;
using Ananke.Federation.Azure;
using Ananke.Federation.Deployment;

namespace Ananke.Tool.Platform.Azure;

/// <summary>
/// Module initializer for the <c>nnke-platform-azure</c> companion tool.
/// Runs automatically when this assembly is loaded by <c>nnke-platform</c>'s adapter probing.
/// Registers a factory for <c>"azure-ai"</c> into <see cref="FederationDeployerRegistry"/>
/// so the CLI can deploy workflows to Azure AI Agent Service without a direct project reference.
/// </summary>
internal static class ModuleInit
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        FederationDeployerRegistry.RegisterFactory("azure-ai", registry =>
        {
            var endpointStr = Environment.GetEnvironmentVariable("AZURE_AI_ENDPOINT")
                ?? throw new InvalidOperationException(
                    "AZURE_AI_ENDPOINT environment variable is not set. " +
                    "Set it to your Azure AI Foundry project endpoint before running 'nnke-platform deploy'.");

            var endpoint = new Uri(endpointStr);
            var credentials = new AzureAgentCredentialProvider(endpoint);
            return new AzureAgentDeployer(credentials, registry);
        });
    }
}
