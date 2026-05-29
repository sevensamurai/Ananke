using Ananke.Roles.Roles;
using Ananke.Roles.Studio;

namespace Ananke.Roles.Slack;

/// <summary>
/// Strongly-typed wrapper around <see cref="StudioOptions.ChannelRoleMap"/> that resolves
/// a Slack channel id or name to the corresponding <see cref="AgentRole"/>.
/// </summary>
/// <remarks>
/// Channel names in <see cref="StudioOptions.ChannelRoleMap"/> are kept as plain strings so
/// that <c>Ananke.Roles</c> stays platform-agnostic at its core. This helper bridges that
/// string map to typed <see cref="AgentRole"/> lookups using an <see cref="IAgentRoleCatalog"/>.
/// </remarks>
public sealed class SlackChannelMap
{
    private readonly IReadOnlyDictionary<string, string> _channelRoleMap;
    private readonly IAgentRoleCatalog _catalog;

    /// <summary>
    /// Initialises a <see cref="SlackChannelMap"/> from the supplied options and role catalog.
    /// </summary>
    /// <param name="options">Studio options that carry the raw channel → role-name mapping.</param>
    /// <param name="catalog">Catalog used to resolve role names to <see cref="AgentRole"/> instances.</param>
    public SlackChannelMap(StudioOptions options, IAgentRoleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(catalog);

        _channelRoleMap = options.ChannelRoleMap;
        _catalog = catalog;
    }

    /// <summary>
    /// Attempts to resolve a <paramref name="channelId"/> (or channel name) to an
    /// <see cref="AgentRole"/>.
    /// </summary>
    /// <param name="channelId">The Slack channel id or name to look up.</param>
    /// <param name="role">
    /// When this method returns <see langword="true"/>, the resolved role; otherwise
    /// <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the channel is mapped to a known role; otherwise
    /// <see langword="false"/>.
    /// </returns>
    public bool TryResolveRole(string channelId, out AgentRole? role)
    {
        role = null;
        if (string.IsNullOrWhiteSpace(channelId))
            return false;

        if (!_channelRoleMap.TryGetValue(channelId, out var roleName))
            return false;

        return _catalog.TryGet(roleName, out role);
    }

    /// <summary>
    /// Returns all channel ids that are registered in the map and whose role names
    /// exist in the catalog.
    /// </summary>
    public IReadOnlyList<string> MappedChannelIds =>
        _channelRoleMap.Keys
            .Where(ch => _channelRoleMap.TryGetValue(ch, out var rn) && _catalog.TryGet(rn, out _))
            .ToList();
}
