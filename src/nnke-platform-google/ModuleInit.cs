using System.Runtime.CompilerServices;
using Ananke.Federation.Deployment;
using Ananke.Federation.Google;

namespace Ananke.Tool.Platform.Google;

/// <summary>
/// Module initializer for the <c>nnke-platform-google</c> companion tool.
/// Registers a factory for <c>"vertex-ai"</c> into <see cref="FederationDeployerRegistry"/>.
/// </summary>
internal static class ModuleInit
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        FederationDeployerRegistry.RegisterFactory("vertex-ai", registry =>
        {
            var project = Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT")
                ?? throw new InvalidOperationException(
                    "GOOGLE_CLOUD_PROJECT environment variable is not set. " +
                    "Set it to your Google Cloud project ID before running 'nnke-platform deploy'.");

            var location = Environment.GetEnvironmentVariable("GOOGLE_CLOUD_LOCATION")
                ?? "us-central1";

            var credentials = new VertexAICredentialProvider(project, location);
            return new VertexAIDeployer(credentials, registry);
        });
    }
}
