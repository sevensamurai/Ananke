using System.Diagnostics.CodeAnalysis;

namespace Ananke.Roles.Roles;

/// <summary>
/// Dictionary-backed in-memory <see cref="IAgentRoleCatalog"/>.
/// </summary>
public sealed class AgentRoleCatalog : IAgentRoleCatalog
{
    private readonly Dictionary<string, AgentRole> _roles = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IReadOnlyList<AgentRole> All => _roles.Values.ToList();

    /// <summary>
    /// Adds a role to the catalog.
    /// </summary>
    public AgentRoleCatalog Add(AgentRole role)
    {
        ArgumentNullException.ThrowIfNull(role);

        if (!_roles.TryAdd(role.Name, role))
            throw new InvalidOperationException($"A role named '{role.Name}' is already registered.");

        return this;
    }

    /// <inheritdoc />
    public bool TryGet(string name, [NotNullWhen(true)] out AgentRole? role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _roles.TryGetValue(name, out role);
    }
}
