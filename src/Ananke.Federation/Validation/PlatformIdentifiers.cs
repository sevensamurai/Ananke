using Ananke.Federation.Recommendation;

namespace Ananke.Federation.Validation;

/// <summary>
/// The single source of truth for platform identifier aliasing. Resolves an alias
/// (<c>"foundry"</c>, <c>"gemini-enterprise"</c>) to its canonical identifier
/// (<c>"azure-ai"</c>, <c>"vertex-ai"</c>), reading the <c>aliases</c> arrays declared per
/// platform in the embedded <c>platform-profiles.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Before this type existed the same alias pairs were hard-coded in three places —
/// <c>FederationDeployerRegistry</c>, <c>DeployabilityValidator</c> and
/// <c>PlatformRecommender</c> — with a comment in the third acknowledging the duplication.
/// A fourth, undocumented one turned up while consolidating them: <c>PlatformProfiles</c>
/// registers each profile under its aliases as well as its canonical name, so its
/// <c>KnownPlatforms</c> is alias-inflated. Vendors rename platforms often enough that four
/// copies is a live drift hazard rather than a theoretical one;.
/// </para>
/// <para>
/// <b>Aliases are permanent.</b> A platform identifier is persisted in
/// <see cref="Deployment.IDeploymentRegistry"/> records, so an identifier that has ever been
/// written must stay resolvable. Add to <c>platform-profiles.json</c>; never remove.
/// </para>
/// </remarks>
public static class PlatformIdentifiers
{
    private static readonly Lazy<Data> _data = new(Load, LazyThreadSafetyMode.PublicationOnly);

    /// <summary>
    /// Resolves <paramref name="platform"/> to its canonical identifier. A canonical identifier
    /// resolves to itself; an unrecognised identifier is returned unchanged, so callers that
    /// pass through to a platform API keep working.
    /// </summary>
    /// <param name="platform">A canonical identifier or a declared alias.</param>
    /// <returns>The canonical identifier, or <paramref name="platform"/> if unrecognised.</returns>
    public static string Resolve(string platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        return _data.Value.Aliases.TryGetValue(platform, out var canonical) ? canonical : platform;
    }

    /// <summary>
    /// Resolves <paramref name="platform"/> and reports whether the result is a platform this
    /// build knows about. Use this instead of <see cref="Resolve"/> when "unrecognised" needs to
    /// be distinguishable from "recognised but empty".
    /// </summary>
    /// <param name="platform">A canonical identifier or a declared alias.</param>
    /// <param name="canonical">The canonical identifier, or <paramref name="platform"/> unchanged.</param>
    /// <returns><see langword="true"/> if <paramref name="canonical"/> is a known platform.</returns>
    public static bool TryResolve(string platform, out string canonical)
    {
        canonical = Resolve(platform);
        return _data.Value.Canonical.Contains(canonical);
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="platform"/> is a known canonical
    /// identifier or a declared alias of one.
    /// </summary>
    public static bool IsKnown(string platform) => TryResolve(platform, out _);

    /// <summary>All canonical platform identifiers declared in <c>platform-profiles.json</c>.</summary>
    public static IReadOnlySet<string> Canonical => _data.Value.Canonical;

    // ── internals ────────────────────────────────────────────────────

    private sealed record Data(
        IReadOnlySet<string> Canonical,
        IReadOnlyDictionary<string, string> Aliases);

    private static Data Load()
    {
        var canonical = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // CanonicalPlatforms, not KnownPlatforms: the latter is a lookup-key set that already
        // contains every alias, so iterating it would self-map each alias and defeat resolution.
        foreach (var name in PlatformProfiles.CanonicalPlatforms)
        {
            canonical.Add(name);

            // A canonical identifier resolves to its own declared spelling, so callers that
            // compare resolved values agree on casing as well as identity.
            aliases[name] = name;

            if (PlatformProfiles.Get(name) is not { } profile)
                continue;

            foreach (var alias in profile.Aliases)
            {
                // First declaration wins. A single alias claimed by two platforms is a data bug
                // in platform-profiles.json, not something to arbitrate at load time.
                if (!aliases.ContainsKey(alias))
                    aliases[alias] = name;
            }
        }

        return new Data(canonical, aliases);
    }
}
