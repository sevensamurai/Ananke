using System.Diagnostics.CodeAnalysis;

namespace Ananke.Roles.Roles;

/// <summary>
/// Catalog of named agent roles.
/// </summary>
public interface IAgentRoleCatalog
{
    /// <summary>
    /// Attempts to look up a role by name.
    /// </summary>
    bool TryGet(string name, [NotNullWhen(true)] out AgentRole? role);

    /// <summary>
    /// All registered roles.
    /// </summary>
    IReadOnlyList<AgentRole> All { get; }
}
