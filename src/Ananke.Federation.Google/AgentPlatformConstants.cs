namespace Ananke.Federation.Google;

/// <summary>
/// Internal string constants for the Gemini Enterprise Agent Platform adapter.
/// Centralises the platform discriminator so no other file in the assembly
/// hard-codes the raw <c>"vertex-ai"</c> or <c>"gemini-agent-platform"</c> strings.
/// </summary>
internal static class AgentPlatformConstants
{
    /// <summary>
    /// Primary platform discriminator — returned by all <c>Platform</c> properties for
    /// backwards compatibility with deployment registries built on <c>"vertex-ai"</c>.
    /// </summary>
    internal const string Platform = "vertex-ai";

    /// <summary>
    /// New canonical platform name introduced with the Gemini Enterprise Agent Platform
    /// rebrand. Accepted as an alias wherever a platform string is compared.
    /// </summary>
    internal const string PlatformAlias = "gemini-agent-platform";

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="platform"/> is either the
    /// primary discriminator (<c>"vertex-ai"</c>) or the canonical alias
    /// (<c>"gemini-agent-platform"</c>).
    /// </summary>
    internal static bool IsAcceptedPlatform(string? platform) =>
        string.Equals(platform, Platform, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(platform, PlatformAlias, StringComparison.OrdinalIgnoreCase);
}
