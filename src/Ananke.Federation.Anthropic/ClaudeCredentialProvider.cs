using Ananke.Federation.Credentials;

namespace Ananke.Federation.Anthropic;

/// <summary>
/// Resolves Anthropic API credentials for Claude Managed Agents deployment.
/// Uses an API key (from environment or configuration) to authenticate.
/// </summary>
public sealed class ClaudeCredentialProvider : IFederationCredentialProvider
{
    private readonly string? _apiKey;
    private readonly Func<string, ClaudeManagedAgentsClient>? _clientFactory;

    /// <summary>
    /// Creates a credential provider for Claude. If no API key is provided,
    /// it will attempt to read from the <c>ANTHROPIC_API_KEY</c> environment variable.
    /// </summary>
    /// <param name="apiKey">
    /// Optional API key. When <see langword="null"/>, falls back to environment variable.
    /// </param>
    /// <param name="clientFactory">
    /// Optional factory for <see cref="ClaudeManagedAgentsClient"/>. When provided,
    /// <see cref="ValidateAsync"/> performs a live <c>GET /v1/models</c> round-trip
    /// to confirm the key is accepted by the Anthropic API. Primarily used in tests.
    /// </param>
    public ClaudeCredentialProvider(
        string? apiKey = null,
        Func<string, ClaudeManagedAgentsClient>? clientFactory = null)
    {
        _apiKey = apiKey;
        _clientFactory = clientFactory;
    }

    /// <inheritdoc />
    public string Platform => "claude";

    /// <inheritdoc />
    /// <returns>
    /// The API key as a string, or <see langword="null"/> if no key is available.
    /// </returns>
    public Task<object?> GetCredentialAsync(string platform, CancellationToken ct = default)
    {
        var key = _apiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        return Task.FromResult<object?>(key);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// When a <c>clientFactory</c> was supplied at construction, performs a live
    /// <c>GET /v1/models</c> round-trip via <see cref="ClaudeManagedAgentsClient.PingAsync"/>
    /// to confirm the key is accepted by the Anthropic API.
    /// </para>
    /// <para>
    /// Without a <c>clientFactory</c> (the default for production use), returns
    /// <see langword="true"/> when an API key is present and non-empty. A full live
    /// round-trip is performed by <see cref="ClaudeDeployer.ValidateAsync"/> which has
    /// access to the HTTP client.
    /// </para>
    /// </remarks>
    public async Task<bool> ValidateAsync(CancellationToken ct = default)
    {
        var key = _apiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
            return false;

        if (_clientFactory is not null)
        {
            using var client = _clientFactory(key);
            return await client.PingAsync(ct);
        }

        return true;
    }
}
