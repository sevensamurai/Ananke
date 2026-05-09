using System.Text.Json;

namespace Ananke.Federation.Recommendation;

/// <summary>
/// Loads and caches the qualitative platform profiles from the embedded
/// <c>platform-profiles.json</c> resource.
/// </summary>
internal static class PlatformProfiles
{
    private static readonly Lazy<IReadOnlyDictionary<string, PlatformProfile>> _data =
        new(Load, LazyThreadSafetyMode.PublicationOnly);

    /// <summary>Returns the profile for <paramref name="canonicalPlatform"/>, or <see langword="null"/>.</summary>
    public static PlatformProfile? Get(string canonicalPlatform) =>
        _data.Value.TryGetValue(canonicalPlatform, out var p) ? p : null;

    /// <summary>All canonical platform identifiers present in <c>platform-profiles.json</c>.</summary>
    public static IReadOnlyCollection<string> KnownPlatforms => (IReadOnlyCollection<string>)_data.Value.Keys;

    // ── internals ────────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, PlatformProfile> Load()
    {
        var result = new Dictionary<string, PlatformProfile>(StringComparer.OrdinalIgnoreCase);

        var assembly = typeof(PlatformProfiles).Assembly;
        using var stream = assembly.GetManifestResourceStream("Ananke.Federation.Recommendation.platform-profiles.json");
        if (stream is null)
            return result;

        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("platforms", out var platforms))
            return result;

        foreach (var platform in platforms.EnumerateObject())
        {
            var el = platform.Value;

            var displayName = el.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? platform.Name : platform.Name;
            var aliases = ReadStringArray(el, "aliases");
            var strengths = ReadStringArray(el, "strengths");
            var weaknesses = ReadStringArray(el, "weaknesses");
            var costBand = el.TryGetProperty("costBand", out var cb) ? cb.GetString() ?? "medium" : "medium";
            var latencyBand = el.TryGetProperty("latencyBand", out var lb) ? lb.GetString() ?? "medium" : "medium";
            var regions = ReadStringArray(el, "regions");

            var govFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (el.TryGetProperty("governance", out var gov))
            {
                foreach (var flag in gov.EnumerateObject())
                {
                    if (flag.Value.ValueKind == JsonValueKind.True)
                        govFlags.Add(flag.Name);
                }
            }

            var profile = new PlatformProfile(
                displayName,
                aliases,
                strengths,
                weaknesses,
                govFlags,
                costBand,
                latencyBand,
                regions);

            result[platform.Name] = profile;

            // Also register aliases so callers can look up by either name
            foreach (var alias in aliases)
                result[alias] = profile;
        }

        return result;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var arr))
            return [];

        var list = new List<string>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.GetString() is { } value)
                list.Add(value);
        }
        return list;
    }
}

/// <summary>
/// Qualitative profile for a single platform, loaded from <c>platform-profiles.json</c>.
/// </summary>
internal sealed record PlatformProfile(
    string DisplayName,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Weaknesses,
    IReadOnlySet<string> GovernanceFlags,
    string CostBand,
    string LatencyBand,
    IReadOnlyList<string> Regions);
