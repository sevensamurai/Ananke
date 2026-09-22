using System.Text.Json;

namespace Ananke.Federation.Validation;

/// <summary>
/// Single source of truth for the platform-capability catalogue shipped in the
/// embedded <c>platform-capabilities.json</c> resource. All platform validators
/// query this instead of maintaining their own hardcoded sets.
/// </summary>
/// <remarks>
/// Data is loaded once per <see cref="AppDomain"/> and cached. Update
/// <c>platform-capabilities.json</c> to add or change capabilities; no code
/// changes needed in the individual validator classes.
/// </remarks>
public static class PlatformCapabilities
{
    private static readonly Lazy<Data> _data = new(Load, LazyThreadSafetyMode.PublicationOnly);

    /// <summary>
    /// Returns the set of capabilities declared for <paramref name="platform"/> in
    /// <c>platform-capabilities.json</c>, resolving aliases via
    /// <see cref="PlatformIdentifiers"/>. Returns an empty set for unknown platforms.
    /// </summary>
    /// <remarks>
    /// An empty result is ambiguous by design — it means either "known platform declaring no
    /// capabilities" or "platform not recognised". Callers that need to tell those apart must use
    /// <see cref="TryGetForPlatform"/>; scoring or gating on a silently-empty set is how a renamed
    /// identifier turns into a validator that quietly stops validating.
    /// </remarks>
    public static IReadOnlySet<string> GetForPlatform(string platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        return _data.Value.Capabilities.TryGetValue(PlatformIdentifiers.Resolve(platform), out var caps)
            ? caps
            : EmptySet;
    }

    /// <summary>
    /// Resolves <paramref name="platform"/> and returns its declared capabilities, reporting
    /// whether the platform was recognised at all.
    /// </summary>
    /// <param name="platform">A canonical platform identifier or a declared alias.</param>
    /// <param name="capabilities">The declared capabilities; empty when unrecognised.</param>
    /// <returns><see langword="false"/> when <paramref name="platform"/> is not a known platform.</returns>
    public static bool TryGetForPlatform(string platform, out IReadOnlySet<string> capabilities)
    {
        ArgumentNullException.ThrowIfNull(platform);

        if (_data.Value.Capabilities.TryGetValue(PlatformIdentifiers.Resolve(platform), out var caps))
        {
            capabilities = caps;
            return true;
        }

        capabilities = EmptySet;
        return false;
    }

    /// <summary>All platform identifiers present in <c>platform-capabilities.json</c>.</summary>
    public static IReadOnlySet<string> KnownPlatforms => _data.Value.Platforms;

    // ── internals ────────────────────────────────────────────────────

    private static readonly HashSet<string> EmptySet = [];

    internal static Data Raw => _data.Value;

    internal sealed record Data(
        HashSet<string> Platforms,
        Dictionary<string, HashSet<string>> Capabilities);

    private static Data Load()
    {
        var assembly = typeof(PlatformCapabilities).Assembly;
        using var stream = assembly.GetManifestResourceStream("Ananke.Federation.platform-capabilities.json");
        if (stream is null)
            return new Data([], []);

        using var doc = JsonDocument.Parse(stream);
        var platforms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var capabilities = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        if (!doc.RootElement.TryGetProperty("platforms", out var platformsElement))
            return new Data(platforms, capabilities);

        foreach (var platform in platformsElement.EnumerateObject())
        {
            platforms.Add(platform.Name);
            var caps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (platform.Value.TryGetProperty("capabilities", out var capsArray))
            {
                foreach (var cap in capsArray.EnumerateArray())
                {
                    if (cap.GetString() is { } value)
                        caps.Add(value);
                }
            }

            capabilities[platform.Name] = caps;
        }

        return new Data(platforms, capabilities);
    }
}
