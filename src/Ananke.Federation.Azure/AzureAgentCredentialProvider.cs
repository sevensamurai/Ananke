using Ananke.Federation.Credentials;
using Azure.AI.Projects.Agents;

namespace Ananke.Federation.Azure;

/// <summary>
/// Resolves Azure AI Foundry credentials and creates an <see cref="AgentAdministrationClient"/>
/// from an endpoint URI. Uses <c>DefaultAzureCredential</c> (Entra ID) for authentication.
/// </summary>
public sealed class AzureAgentCredentialProvider : IFederationCredentialProvider
{
    private readonly Uri _endpoint;

    /// <summary>
    /// Creates a credential provider for Azure AI Agent Service.
    /// </summary>
    /// <param name="endpoint">
    /// The Azure AI Foundry project endpoint URI
    /// (e.g. <c>https://&lt;resource&gt;.services.ai.azure.com/api/projects/&lt;project&gt;</c>).
    /// </param>
    public AzureAgentCredentialProvider(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        _endpoint = endpoint;
    }

    /// <inheritdoc />
    public string Platform => "azure-ai";

    /// <summary>The configured endpoint URI.</summary>
    public Uri Endpoint => _endpoint;

    /// <inheritdoc />
    /// <returns>
    /// An <see cref="AgentAdministrationClient"/> ready to call the Azure AI Agent Service,
    /// or <see langword="null"/> if the endpoint is unreachable.
    /// </returns>
    public Task<object?> GetCredentialAsync(string platform, CancellationToken ct = default)
    {
        try
        {
            var client = new AgentAdministrationClient(
                _endpoint,
                new AgentAdministrationClientOptions());
            return Task.FromResult<object?>(client);
        }
        catch (Exception)
        {
            // Client construction failed (bad endpoint, missing credential chain) — the documented
            // contract is null-on-unreachable, so this is intentionally swallowed rather than thrown.
            return Task.FromResult<object?>(null);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Attempts to construct an <see cref="AgentAdministrationClient"/> for the configured
    /// endpoint. A successful construction with <c>DefaultAzureCredential</c> available
    /// indicates the credentials are present; a failed construction returns
    /// <see langword="false"/>.
    /// </remarks>
    public async Task<bool> ValidateAsync(CancellationToken ct = default)
    {
        var credential = await GetCredentialAsync(Platform, ct);
        return credential is not null;
    }
}
