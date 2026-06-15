using Ananke.Abstractions.Providers;

namespace Ananke.Orchestration.Google.Credentials;

/// <summary>
/// Resolves Google Cloud / Gemini API credentials.
/// For the Gemini Developer API use <see cref="GeminiApiKeyCredentialProvider"/>.
/// For Vertex AI (ADC), see <see cref="VertexAICredentialProvider"/>.
/// </summary>
public sealed class GeminiApiKeyCredentialProvider : ICredentialProvider
{
    private readonly string? _apiKey;

    /// <summary>
    /// Creates a credential provider for the Gemini Developer API.
    /// </summary>
    /// <param name="apiKey">
    /// API key. When <see langword="null"/>, falls back to
    /// <c>GEMINI_API_KEY</c> then <c>GOOGLE_API_KEY</c> environment variables.
    /// </param>
    public GeminiApiKeyCredentialProvider(string? apiKey = null)
    {
        _apiKey = apiKey;
    }

    /// <inheritdoc />
    public string Platform => "google";

    /// <inheritdoc />
    /// <returns>The API key string, or <see langword="null"/> if none is configured.</returns>
    public Task<object?> GetCredentialAsync(CancellationToken ct = default)
    {
        var key = _apiKey
            ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
        return Task.FromResult<object?>(key);
    }

    /// <inheritdoc />
    public Task<bool> ValidateAsync(CancellationToken ct = default)
    {
        var key = _apiKey
            ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
        return Task.FromResult(!string.IsNullOrWhiteSpace(key));
    }
}

/// <summary>
/// Resolves Google Cloud credentials for Vertex AI using Application Default Credentials (ADC).
/// Returns a tuple of (project, location) so the caller can construct a <c>Client</c>.
/// </summary>
public sealed class VertexAICredentialProvider : ICredentialProvider
{
    private readonly string _project;
    private readonly string _location;

    /// <summary>Google Cloud project ID.</summary>
    public string Project => _project;

    /// <summary>Google Cloud region (e.g. <c>"us-central1"</c>).</summary>
    public string Location => _location;

    /// <summary>
    /// Creates a credential provider for Vertex AI.
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
    public string Platform => "vertex-ai";

    /// <inheritdoc />
    /// <returns>A <c>(project, location)</c> tuple as an <see langword="object"/>.</returns>
    public Task<object?> GetCredentialAsync(CancellationToken ct = default)
        => Task.FromResult<object?>((_project, _location));

    /// <inheritdoc />
    public Task<bool> ValidateAsync(CancellationToken ct = default)
        => Task.FromResult(true); // ADC presence is verified at SDK construction time
}
