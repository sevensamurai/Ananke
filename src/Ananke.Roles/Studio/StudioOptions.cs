using Ananke.Design;

namespace Ananke.Roles.Studio;

/// <summary>
/// Options for configuring a studio host.
/// </summary>
public sealed record StudioOptions
{
    /// <summary>
    /// Model alias map used when translating roles into workflow manifests.
    /// </summary>
    public IReadOnlyDictionary<string, ModelDefinition> ModelAliasMap { get; init; } =
        new Dictionary<string, ModelDefinition>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Token budget caps keyed by role or workflow name.
    /// </summary>
    public IReadOnlyDictionary<string, long> PerRoleTokenBudgetCaps { get; init; } =
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Channel-name to role-name mapping, kept string-based so this package stays platform-agnostic.
    /// </summary>
    public IReadOnlyDictionary<string, string> ChannelRoleMap { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
