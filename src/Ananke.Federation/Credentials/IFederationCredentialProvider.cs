namespace Ananke.Federation.Credentials;

/// <summary>
/// Resolves platform credentials at runtime. Secrets are never stored in manifests —
/// this interface provides them on demand during deployment and monitoring.
/// </summary>
public interface IFederationCredentialProvider
{
    /// <summary>Platform identifier this provider targets.</summary>
    string Platform { get; }

    /// <summary>
    /// Resolves credentials for the target platform.
    /// Returns <see langword="null"/> if no credentials are available.
    /// </summary>
    /// <param name="platform">Platform identifier to resolve credentials for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An opaque credential object, or <see langword="null"/>.</returns>
    Task<object?> GetCredentialAsync(string platform, CancellationToken ct = default);
    /// <summary>
    /// Validates that the credentials for this provider are present and
    /// accepted by the target platform.
    /// </summary>
    /// <remarks>
    /// Implementations must perform a real validation — a live round-trip or equivalent
    /// check is strongly preferred over a simple null/presence check.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if the credentials are valid;
    /// <see langword="false"/> if they are absent or rejected.
    /// </returns>
    Task<bool> ValidateAsync(CancellationToken ct = default);
}
