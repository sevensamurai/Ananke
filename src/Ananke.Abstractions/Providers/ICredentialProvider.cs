namespace Ananke.Abstractions.Providers;

/// <summary>
/// Resolves runtime credentials for a specific LLM/platform provider.
/// Implementations live in <c>Ananke.Orchestration.{Provider}</c> and are consumed
/// by both the matching agent model and by <c>Ananke.Federation.{Provider}</c> via DI.
/// </summary>
/// <remarks>
/// The interface is intentionally provider-agnostic: it does not leak vendor SDK types.
/// Provider implementations expose vendor-specific accessors on their concrete types
/// only when unavoidable (e.g. Google Application Default Credentials).
/// </remarks>
public interface ICredentialProvider
{
    /// <summary>Platform identifier this provider targets (e.g. <c>"openai"</c>, <c>"anthropic"</c>).</summary>
    string Platform { get; }

    /// <summary>
    /// Resolves credentials for the target platform.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An opaque credential object understood by the provider's SDK,
    /// or <see langword="null"/> if no credentials are configured.
    /// </returns>
    Task<object?> GetCredentialAsync(CancellationToken ct = default);

    /// <summary>
    /// Validates that the credentials are present and accepted by the provider.
    /// Implementations should perform a real validation (a live round-trip or
    /// equivalent check) rather than a simple null/presence check.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if credentials are valid;
    /// <see langword="false"/> if they are absent or rejected.
    /// </returns>
    Task<bool> ValidateAsync(CancellationToken ct = default);
}
