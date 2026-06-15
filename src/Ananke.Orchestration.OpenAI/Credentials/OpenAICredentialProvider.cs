using Ananke.Abstractions.Providers;

namespace Ananke.Orchestration.OpenAI.Credentials;

/// <summary>
/// Resolves OpenAI API credentials. Uses an API key supplied at construction
/// or falls back to the <c>OPENAI_API_KEY</c> environment variable.
/// </summary>
public sealed class OpenAICredentialProvider : ICredentialProvider
{
    private readonly string? _apiKey;

    /// <summary>
    /// Creates a credential provider for OpenAI.
    /// </summary>
    /// <param name="apiKey">
    /// API key. When <see langword="null"/>, falls back to the
    /// <c>OPENAI_API_KEY</c> environment variable at resolution time.
    /// </param>
    public OpenAICredentialProvider(string? apiKey = null)
    {
        _apiKey = apiKey;
    }

    /// <inheritdoc />
    public string Platform => "openai";

    /// <inheritdoc />
    /// <returns>The API key string, or <see langword="null"/> if none is configured.</returns>
    public Task<object?> GetCredentialAsync(CancellationToken ct = default)
    {
        var key = _apiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        return Task.FromResult<object?>(key);
    }

    /// <inheritdoc />
    public Task<bool> ValidateAsync(CancellationToken ct = default)
    {
        var key = _apiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        return Task.FromResult(!string.IsNullOrWhiteSpace(key));
    }
}
