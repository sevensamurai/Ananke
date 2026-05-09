using Ananke.Federation.Credentials;
using Google.GenAI;

namespace Ananke.Federation.Google;

/// <summary>
/// Resolves Google Cloud credentials using Application Default Credentials (ADC).
/// Returns a configured <see cref="Client"/> for Gemini Enterprise Agent Platform operations.
/// </summary>
public sealed class VertexAICredentialProvider : IFederationCredentialProvider
{
    private readonly string _project;
    private readonly string _location;

    /// <summary>Google Cloud project ID.</summary>
    internal string Project => _project;

    /// <summary>Google Cloud region (e.g. <c>"us-central1"</c>).</summary>
    internal string Location => _location;

    /// <summary>
    /// Creates a credential provider for the specified Google Cloud project and region.
    /// </summary>
    /// <param name="project">Google Cloud project ID.</param>
    /// <param name="location">Google Cloud region (e.g. <c>"us-central1"</c>).</param>
    public VertexAICredentialProvider(string project, string location)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        _project = project;
        _location = location;
    }

    /// <inheritdoc />
    public string Platform => AgentPlatformConstants.Platform;

    /// <inheritdoc />
    /// <returns>A <see cref="Client"/> configured for the Gemini Enterprise Agent Platform backend via ADC, or <see langword="null"/> if ADC is not available.</returns>
    public Task<object?> GetCredentialAsync(string platform, CancellationToken ct = default)
    {
        if (!AgentPlatformConstants.IsAcceptedPlatform(platform))
            return Task.FromResult<object?>(null);

        try
        {
            var client = new Client(project: _project, location: _location, vertexAI: true);
            return Task.FromResult<object?>(client);
        }
        catch (Exception)
        {
            return Task.FromResult<object?>(null);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Attempts to construct a <see cref="Client"/> using Application Default Credentials.
    /// Returns <see langword="false"/> if ADC is unavailable or the project/location are
    /// not accessible.
    /// </remarks>
    public async Task<bool> ValidateAsync(CancellationToken ct = default)
    {
        var credential = await GetCredentialAsync(Platform, ct);
        return credential is not null;
    }
}
