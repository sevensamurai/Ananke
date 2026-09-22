using System.Net.Http.Headers;

namespace Ananke.Orchestration.Bedrock;

/// <summary>
/// Attaches a Bedrock API key as a bearer token. The simpler of Bedrock's two authentication
/// modes; <see cref="AwsSigV4Handler"/> covers the case where an organisation forbids long-lived
/// keys and mandates IAM.
/// </summary>
/// <remarks>
/// A handler rather than the client's own API-key option, so that both wire shapes authenticate
/// through the same seam. The Anthropic client's key option sends <c>x-api-key</c>, which is the
/// Anthropic-native scheme, not the bearer token Bedrock expects.
/// </remarks>
public sealed class BedrockApiKeyHandler : DelegatingHandler
{
    private readonly Func<CancellationToken, ValueTask<string>> _keyProvider;

    /// <summary>Creates a handler that sends a fixed API key.</summary>
    /// <param name="apiKey">Bedrock API key, as issued in the console or <c>AWS_BEARER_TOKEN_BEDROCK</c>.</param>
    public BedrockApiKeyHandler(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _keyProvider = _ => ValueTask.FromResult(apiKey);
    }

    /// <summary>
    /// Creates a handler that fetches the key per request — for a key held in a secret store,
    /// or rotated on a schedule.
    /// </summary>
    /// <param name="keyProvider">Called for every request.</param>
    public BedrockApiKeyHandler(Func<CancellationToken, ValueTask<string>> keyProvider)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);
        _keyProvider = keyProvider;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = await _keyProvider(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
